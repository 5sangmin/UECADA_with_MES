// src/Features/UdpRelay/Replay/ReplayWorker.cs
//
// Replay 송신 loop (윈도우 기반 + carry-forward).
//
// 정책 (PR2 윈도우 그루핑 결정):
//   1) 시작 시 TSDB DISTINCT (line_id, equipment_id) 로 장비 슬롯 목록 확정
//   2) [from, from+windowMs) 윈도우 단위로 row 그루핑
//        - 같은 장비가 윈도우 내 여러 번이면 마지막 row 사용
//        - 한 번도 안 들어온 장비는 직전 윈도우 값 carry-forward (record ts 그대로)
//        - 한 번도 본 적 없는 장비는 status=0 빈 슬롯
//   3) header packet_ts = 윈도우 내 가장 늦은 row 의 ts (carry-only 윈도우는 윈도우 종료 시각)
//   4) 송신 간격 = windowMs / speed (라이브 페이스 유지)
//   5) loop=true 면 stream 종료 후 from 부터 다시 시작

using System.Net;
using System.Net.Sockets;
using BeApi.Features.UdpRelay.Wire;
using BeApi.Shared.Settings;

namespace BeApi.Features.UdpRelay.Replay;

public sealed class ReplayWorker
{
    private readonly UdpSettings _udp;
    private readonly ReplaySnapshotReader _reader;
    private readonly ReplaySessionState _state;
    private readonly CancellationToken _ct;
    private readonly ILogger<ReplayWorker> _logger;

    public ReplayWorker(
        UdpSettings udp,
        ReplaySnapshotReader reader,
        ReplaySessionState state,
        CancellationToken ct,
        ILogger<ReplayWorker> logger)
    {
        _udp = udp;
        _reader = reader;
        _state = state;
        _ct = ct;
        _logger = logger;
    }

    public async Task RunAsync()
    {
        var mcast = new IPEndPoint(IPAddress.Parse(_udp.MulticastGroup), _udp.Replay.OutputPort);

        // 1) 장비 슬롯 목록 확정
        var slots = await _reader.GetDistinctEquipmentsAsync(_ct).ConfigureAwait(false);
        if (slots.Count == 0)
        {
            _logger.LogWarning("Replay: 장비 슬롯이 0개입니다. 세션 종료.");
            return;
        }
        _logger.LogInformation("Replay 장비 슬롯 확정. count={count}", slots.Count);

        // 2) 송신 소켓
        using var sender = new UdpClient(AddressFamily.InterNetwork);
        sender.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        sender.Client.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastTimeToLive, _udp.Relay.MulticastTtl);
        if (!string.IsNullOrWhiteSpace(_udp.Relay.MulticastInterface))
        {
            var nicIp = IPAddress.Parse(_udp.Relay.MulticastInterface);
            sender.Client.SetSocketOption(
                SocketOptionLevel.IP,
                SocketOptionName.MulticastInterface,
                nicIp.GetAddressBytes());
        }

        var windowMs = Math.Max(100, _udp.Replay.WindowMs);
        var window = TimeSpan.FromMilliseconds(windowMs);
        var sendInterval = TimeSpan.FromMilliseconds(windowMs / _state.Speed);

        _logger.LogInformation(
            "Replay worker 시작. mcast={mcast}, NIC={nic}, speed={speed}, loop={loop}, windowMs={w}, sendInterval={si}ms",
            mcast, _udp.Relay.MulticastInterface, _state.Speed, _state.Loop, windowMs, sendInterval.TotalMilliseconds);

        do
        {
            await StreamOnceAsync(sender, mcast, slots, window, sendInterval).ConfigureAwait(false);

            if (!_state.Loop) break;
            if (_ct.IsCancellationRequested) break;

            _state.LoopCount++;
            _logger.LogInformation("Replay loop 재시작 (count={c})", _state.LoopCount);
        }
        while (!_ct.IsCancellationRequested);
    }

    private async Task StreamOnceAsync(
        UdpClient sender,
        IPEndPoint mcast,
        IReadOnlyList<EquipmentSlotKey> slots,
        TimeSpan window,
        TimeSpan sendInterval)
    {
        // 슬롯별 최신 EquipmentRecord (carry-forward 용)
        var lastRecord = new Dictionary<EquipmentSlotKey, EquipmentRecord>(slots.Count);

        // 현재 윈도우 안에서 슬롯별로 본 마지막 row (이 윈도우용)
        var windowRecord = new Dictionary<EquipmentSlotKey, EquipmentRecord>(slots.Count);
        DateTimeOffset windowStart = _state.From;
        DateTimeOffset windowEnd = windowStart + window;
        DateTimeOffset? latestRowTsInWindow = null;
        bool isFirstFlush = true;

        await foreach (var item in _reader.StreamAsync(_state.From, _state.To, _ct).ConfigureAwait(false))
        {
            if (_ct.IsCancellationRequested) return;

            // 이 row 가 현재 윈도우보다 뒤이면, 그 사이의 모든 윈도우를 flush 하고 진행
            while (item.Ts >= windowEnd)
            {
                await FlushAsync(
                    sender, mcast, slots, lastRecord, windowRecord,
                    windowStart, windowEnd, latestRowTsInWindow,
                    sendInterval, isFirstFlush).ConfigureAwait(false);
                isFirstFlush = false;
                windowRecord.Clear();
                latestRowTsInWindow = null;
                windowStart = windowEnd;
                windowEnd = windowStart + window;
                if (_ct.IsCancellationRequested) return;
            }

            // row 를 현재 윈도우에 누적
            var key = new EquipmentSlotKey(item.Record.LineId, item.Record.EquipmentId);
            windowRecord[key] = item.Record;
            if (latestRowTsInWindow == null || item.Ts > latestRowTsInWindow.Value)
            {
                latestRowTsInWindow = item.Ts;
            }
        }

        // stream 종료 후 마지막 윈도우 flush (윈도우에 데이터가 있으면, 또는 carry-forward 가능하면)
        if (!_ct.IsCancellationRequested && (windowRecord.Count > 0 || lastRecord.Count > 0))
        {
            await FlushAsync(
                sender, mcast, slots, lastRecord, windowRecord,
                windowStart, windowEnd, latestRowTsInWindow,
                sendInterval, isFirstFlush).ConfigureAwait(false);
        }
    }

    private async Task FlushAsync(
        UdpClient sender,
        IPEndPoint mcast,
        IReadOnlyList<EquipmentSlotKey> slots,
        Dictionary<EquipmentSlotKey, EquipmentRecord> lastRecord,
        Dictionary<EquipmentSlotKey, EquipmentRecord> windowRecord,
        DateTimeOffset windowStart,
        DateTimeOffset windowEnd,
        DateTimeOffset? latestRowTsInWindow,
        TimeSpan sendInterval,
        bool isFirstFlush)
    {
        // 첫 송신이 아니면 sendInterval 만큼 대기 (라이브 페이스)
        if (!isFirstFlush)
        {
            try { await Task.Delay(sendInterval, _ct).ConfigureAwait(false); }
            catch (OperationCanceledException) { return; }
        }

        // 슬롯별 record 채움
        // 우선순위: windowRecord (현재 윈도우 신규 값) > lastRecord (carry-forward) > empty
        var records = new List<EquipmentRecord>(slots.Count);
        foreach (var slot in slots)
        {
            if (windowRecord.TryGetValue(slot, out var fresh))
            {
                records.Add(fresh);
                lastRecord[slot] = fresh; // carry 갱신
            }
            else if (lastRecord.TryGetValue(slot, out var carry))
            {
                records.Add(carry); // ts 그대로 유지 (정책 5-A)
            }
            else
            {
                records.Add(EmptyRecord(slot));
            }
        }

        // packet_ts = 윈도우 내 가장 늦은 row 의 ts. carry-only 면 윈도우 종료 시각.
        var packetTs = latestRowTsInWindow ?? windowEnd;
        var packet = UdpPacketCodec.Encode(packetTs.ToUnixTimeMilliseconds(), records);

        try
        {
            await sender.SendAsync(packet, packet.Length, mcast).ConfigureAwait(false);
            _state.SentPackets++;
            _state.SentRecords += records.Count;
            _state.CurrentTs = packetTs;

            if (_state.SentPackets % 30 == 1)
            {
                _logger.LogDebug(
                    "Replay flush. sent={s}, window=[{ws}, {we}), packetTs={pts}, freshSlots={fresh}/{total}",
                    _state.SentPackets, windowStart, windowEnd, packetTs, windowRecord.Count, slots.Count);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Replay 송신 실패 (window=[{ws}, {we}), count={c}).", windowStart, windowEnd, records.Count);
        }
    }

    private static EquipmentRecord EmptyRecord(EquipmentSlotKey slot) =>
        new EquipmentRecord(
            LineId: slot.LineId,
            EquipmentId: slot.EquipmentId,
            TsEpochMs: 0,
            Heartbeat: 0,
            QualityCode: 0,
            Power: 0,
            Reserved: 0,
            StatusCode: 0,
            Progress: 0f,
            CycleTime: 0f,
            PartCount: 0,
            Data1Setpoint: 0f, Data1Sensor: 0f,
            Data2Setpoint: 0f, Data2Sensor: 0f,
            Data3Setpoint: 0f, Data3Sensor: 0f,
            ExternalData1Sensor: 0f, ExternalData2Sensor: 0f,
            ExternalData3Sensor: 0f, ExternalData4Sensor: 0f,
            CmdId: 0, CmdAccepted: 0, CmdStatus: 0);
}

public sealed class ReplayWorkerFactory
{
    private readonly IServiceProvider _sp;
    public ReplayWorkerFactory(IServiceProvider sp) { _sp = sp; }

    public ReplayWorker Create(ReplaySessionState state, CancellationToken ct)
    {
        var udp = _sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<UdpSettings>>().Value;
        var reader = _sp.GetRequiredService<ReplaySnapshotReader>();
        var logger = _sp.GetRequiredService<ILogger<ReplayWorker>>();
        return new ReplayWorker(udp, reader, state, ct, logger);
    }
}
