// src/Features/UdpRelay/Replay/ReplayWorker.cs
//
// Replay 송신 loop.
//
// 알고리즘:
//   1) ReplaySnapshotReader 로부터 ts 오름차순 record stream 수신
//   2) 같은 ts 의 record 들을 한 패킷으로 묶어 UDP 송신 (header packet_ts = ts)
//   3) "다음 ts - 현재 ts" 만큼 wall-clock 대기 (단, /speed 적용)
//   4) loop=true 이면 stream 이 끝났을 때 from 부터 다시 시작
//
// 송신 NIC 는 live relay 와 동일하게 Udp:Relay:MulticastInterface 사용.

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

        _logger.LogInformation(
            "Replay worker 시작. mcast={mcast}, NIC={nic}, speed={speed}, loop={loop}",
            mcast, _udp.Relay.MulticastInterface, _state.Speed, _state.Loop);

        do
        {
            await StreamOnceAsync(sender, mcast).ConfigureAwait(false);

            if (!_state.Loop) break;
            if (_ct.IsCancellationRequested) break;

            _state.LoopCount++;
            _logger.LogInformation("Replay loop 재시작 (count={c})", _state.LoopCount);
        }
        while (!_ct.IsCancellationRequested);
    }

    private async Task StreamOnceAsync(UdpClient sender, IPEndPoint mcast)
    {
        DateTimeOffset? prevTs = null;
        var buffer = new List<EquipmentRecord>(32);
        DateTimeOffset bufferTs = default;

        await foreach (var item in _reader.StreamAsync(_state.From, _state.To, _ct).ConfigureAwait(false))
        {
            if (_ct.IsCancellationRequested) return;

            if (buffer.Count == 0)
            {
                bufferTs = item.Ts;
            }
            else if (item.Ts != bufferTs)
            {
                // ts 가 바뀜 → 직전 그룹 flush
                await FlushAsync(sender, mcast, bufferTs, buffer, prevTs).ConfigureAwait(false);
                prevTs = bufferTs;
                buffer.Clear();
                bufferTs = item.Ts;
            }
            buffer.Add(item.Record);
        }

        if (buffer.Count > 0 && !_ct.IsCancellationRequested)
        {
            await FlushAsync(sender, mcast, bufferTs, buffer, prevTs).ConfigureAwait(false);
        }
    }

    private async Task FlushAsync(
        UdpClient sender,
        IPEndPoint mcast,
        DateTimeOffset packetTs,
        List<EquipmentRecord> records,
        DateTimeOffset? prevTs)
    {
        // 이전 그룹과의 시간차만큼 대기 (첫 패킷은 즉시 송신)
        if (prevTs.HasValue)
        {
            var deltaMs = (packetTs - prevTs.Value).TotalMilliseconds;
            var waitMs = deltaMs / _state.Speed;
            if (waitMs > 1)
            {
                try
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(waitMs), _ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }

        var packet = UdpPacketCodec.Encode(packetTs.ToUnixTimeMilliseconds(), records);
        try
        {
            await sender.SendAsync(packet, packet.Length, mcast).ConfigureAwait(false);
            _state.SentPackets++;
            _state.SentRecords += records.Count;
            _state.CurrentTs = packetTs;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Replay 송신 실패 (ts={ts}, count={c}).", packetTs, records.Count);
        }
    }
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
