// src/Features/Latest/LatestCache.cs
//
// 슬롯별 최신 EquipmentRecord 메모리 캐시.
// - 키: (lineId, equipmentId)
// - 갱신 정책: last-wins (들어온 record 가 더 최신이면 교체. 같은 ts 도 교체.)
// - 관측 hook 으로 IUdpLiveObserver.NotifyLivePayload 를 받아 갱신.
//
// thread-safe (ConcurrentDictionary + Volatile snapshot).

using System.Collections.Concurrent;
using BeApi.Features.UdpRelay.Live;
using BeApi.Features.UdpRelay.Wire;

namespace BeApi.Features.Latest;

/// <summary>슬롯 키. EquipmentLut 의 wire short 값과 동일 의미.</summary>
public readonly record struct LatestSlotKey(short LineId, short EquipmentId);

/// <summary>캐시에 저장되는 한 슬롯의 스냅샷.</summary>
public sealed class CachedLatest
{
    public EquipmentRecord Record { get; init; }
    public DateTimeOffset CachedAt { get; init; }     // 캐시 갱신 시각 (서버 wall clock)
    public long PacketTsEpochMs { get; init; }        // header packet_ts
}

/// <summary>
/// 라이브 UDP relay payload 를 받아 record 별로 캐시 갱신.
/// IUdpLiveObserver 의 payload hook 을 구현한다.
/// </summary>
public sealed class LatestCache : IUdpLiveObserver
{
    private readonly ConcurrentDictionary<LatestSlotKey, CachedLatest> _store = new();
    private readonly ILogger<LatestCache> _logger;

    public LatestCache(ILogger<LatestCache> logger)
    {
        _logger = logger;
    }

    public void NotifyLivePayload(ReadOnlySpan<byte> payload)
    {
        if (!UdpPacketCodec.TryReadHeader(payload, out var version, out var count, out var packetTs))
        {
            return; // magic 미일치 등은 이미 relay 측에서 drop
        }
        var now = DateTimeOffset.UtcNow;

        for (int i = 0; i < count; i++)
        {
            if (!UdpPacketCodec.TryReadRecord(payload, i, out var rec))
            {
                _logger.LogWarning("LatestCache: record {i} 파싱 실패 (payloadLen={len}, count={count})", i, payload.Length, count);
                break;
            }
            var key = new LatestSlotKey(rec.LineId, rec.EquipmentId);
            var entry = new CachedLatest
            {
                Record = rec,
                CachedAt = now,
                PacketTsEpochMs = packetTs,
            };

            // last-wins. AddOrUpdate 로 atomically.
            _store.AddOrUpdate(
                key,
                addValueFactory: _ => entry,
                updateValueFactory: (_, prev) =>
                    rec.TsEpochMs >= prev.Record.TsEpochMs ? entry : prev);
        }
    }

    // 다른 IUdpLiveObserver 멤버는 캐시 책임 밖이므로 noop / 기본값.
    public void NotifyLivePacket(long packetTsEpochMs, int equipmentCount) { }
    public DateTimeOffset? LastReceivedAt => null;
    public long TotalPackets => 0;
    public long? LastPacketTsEpochMs => null;
    public int LastEquipmentCount => 0;

    /// <summary>슬롯별 스냅샷 조회. 없으면 null.</summary>
    public CachedLatest? TryGet(short lineId, short equipmentId)
    {
        return _store.TryGetValue(new LatestSlotKey(lineId, equipmentId), out var v) ? v : null;
    }

    /// <summary>전체 슬롯 복제 (얕은 copy).</summary>
    public IReadOnlyList<KeyValuePair<LatestSlotKey, CachedLatest>> Snapshot()
    {
        return _store.ToArray();
    }

    public int Count => _store.Count;
}
