// src/Features/Latest/LatestService.cs
//
// 결정 (Q3=A):
//   - 캐시에 값이 있고 NOT stale → 그 값 사용 (source=cache)
//   - 그 외 (캐시 미스 또는 stale) → TSDB fallback (source=tsdb)
// stale 기준: UdpSettings.StaleThresholdSeconds (PR3 의 UDP 체커와 동일 임계값).

using BeApi.Features.UdpRelay.Lut;
using BeApi.Features.UdpRelay.Wire;
using BeApi.Infrastructure.Persistence.Entities;
using BeApi.Shared.Settings;
using Microsoft.Extensions.Options;

namespace BeApi.Features.Latest;

public sealed class LatestService
{
    private readonly LatestCache _cache;
    private readonly LatestRepository _repo;
    private readonly EquipmentLut _lut;
    private readonly UdpSettings _udp;

    public LatestService(
        LatestCache cache,
        LatestRepository repo,
        EquipmentLut lut,
        IOptions<UdpSettings> udpOptions)
    {
        _cache = cache;
        _repo = repo;
        _lut = lut;
        _udp = udpOptions.Value;
    }

    public TimeSpan StaleThreshold => TimeSpan.FromSeconds(Math.Max(1, _udp.StaleThresholdSeconds));

    /// <summary>단일 슬롯의 latest. 캐시 우선 → TSDB fallback.</summary>
    public async Task<EquipmentLatestDto?> GetLatestAsync(short lineId, short equipmentId, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var cached = _cache.TryGet(lineId, equipmentId);

        if (cached != null && (now - cached.CachedAt) <= StaleThreshold)
        {
            return ToDto(cached.Record, "cache", now);
        }

        var row = await _repo.GetLatestAsync(lineId, equipmentId, ct).ConfigureAwait(false);
        if (row != null)
        {
            return EntityToDto(row, "tsdb", now);
        }

        // 캐시는 있는데 stale, TSDB 에도 없음 → 그래도 캐시 값을 돌려준다 (source=cache, stale 표시는 ageSeconds 로)
        if (cached != null)
        {
            return ToDto(cached.Record, "cache", now);
        }

        return null;
    }

    /// <summary>전체 슬롯의 latest. 캐시에서 채워지지 않은 슬롯은 TSDB 로 보강.</summary>
    public async Task<EquipmentLatestEnvelopeDto> GetAllLatestAsync(CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var threshold = StaleThreshold;

        // 1) 캐시 snapshot
        var cacheSnapshot = _cache.Snapshot();
        var cacheByKey = cacheSnapshot.ToDictionary(kv => kv.Key, kv => kv.Value);

        // 2) 캐시가 비었거나 전반적으로 stale 한 슬롯들이 있을 수 있으므로
        //    TSDB 의 전체 slot 목록을 한 번 가져와 union 한다.
        var tsdbRows = await _repo.GetAllLatestAsync(ct).ConfigureAwait(false);
        var tsdbByKey = new Dictionary<LatestSlotKey, EquipmentSnapshotEntity>();
        foreach (var row in tsdbRows)
        {
            if (!_lut.TryGetLineId(row.LineId, out var lid)) continue;
            if (!_lut.TryGetEquipmentId(row.EquipmentId, out var eid)) continue;
            tsdbByKey[new LatestSlotKey(lid, eid)] = row;
        }

        // 3) union 키 셋
        var keys = new HashSet<LatestSlotKey>(cacheByKey.Keys);
        foreach (var k in tsdbByKey.Keys) keys.Add(k);

        var items = new List<EquipmentLatestDto>(keys.Count);
        int cacheSourceCount = 0;
        int tsdbSourceCount = 0;

        foreach (var key in keys.OrderBy(k => k.LineId).ThenBy(k => k.EquipmentId))
        {
            cacheByKey.TryGetValue(key, out var cached);
            tsdbByKey.TryGetValue(key, out var row);

            if (cached != null && (now - cached.CachedAt) <= threshold)
            {
                items.Add(ToDto(cached.Record, "cache", now));
                cacheSourceCount++;
            }
            else if (row != null)
            {
                items.Add(EntityToDto(row, "tsdb", now));
                tsdbSourceCount++;
            }
            else if (cached != null)
            {
                // stale 캐시지만 TSDB 에도 없음 → 그대로 cache (오래된)
                items.Add(ToDto(cached.Record, "cache", now));
                cacheSourceCount++;
            }
        }

        string overall =
            items.Count == 0 ? "empty"
            : (cacheSourceCount > 0 && tsdbSourceCount > 0) ? "mixed"
            : cacheSourceCount > 0 ? "cache"
            : "tsdb";

        return new EquipmentLatestEnvelopeDto(
            Count: items.Count,
            OverallSource: overall,
            GeneratedAt: now,
            Items: items);
    }

    // ----------------- 변환 -----------------

    private static EquipmentLatestDto ToDto(EquipmentRecord r, string source, DateTimeOffset now)
    {
        var ts = DateTimeOffset.FromUnixTimeMilliseconds(r.TsEpochMs);
        var age = (now - ts).TotalSeconds;
        return new EquipmentLatestDto(
            LineId: r.LineId,
            EquipmentId: r.EquipmentId,
            TsEpochMs: r.TsEpochMs,
            Ts: ts,
            Heartbeat: r.Heartbeat,
            QualityCode: r.QualityCode,
            Power: r.Power != 0,
            StatusCode: r.StatusCode,
            Progress: r.Progress,
            CycleTime: r.CycleTime,
            PartCount: r.PartCount,
            Data1Setpoint: r.Data1Setpoint, Data1Sensor: r.Data1Sensor,
            Data2Setpoint: r.Data2Setpoint, Data2Sensor: r.Data2Sensor,
            Data3Setpoint: r.Data3Setpoint, Data3Sensor: r.Data3Sensor,
            ExternalData1Sensor: r.ExternalData1Sensor,
            ExternalData2Sensor: r.ExternalData2Sensor,
            ExternalData3Sensor: r.ExternalData3Sensor,
            ExternalData4Sensor: r.ExternalData4Sensor,
            CmdId: r.CmdId,
            CmdAccepted: r.CmdAccepted,
            CmdStatus: r.CmdStatus,
            Source: source,
            AgeSeconds: Math.Max(0, age));
    }

    private EquipmentLatestDto EntityToDto(EquipmentSnapshotEntity e, string source, DateTimeOffset now)
    {
        if (!_lut.TryGetLineId(e.LineId, out var lid)) lid = 0;
        if (!_lut.TryGetEquipmentId(e.EquipmentId, out var eid)) eid = 0;
        var tsMs = e.Ts.ToUnixTimeMilliseconds();
        var age = (now - e.Ts).TotalSeconds;

        return new EquipmentLatestDto(
            LineId: lid,
            EquipmentId: eid,
            TsEpochMs: tsMs,
            Ts: e.Ts,
            Heartbeat: (int)(e.Heartbeat ?? 0L),
            QualityCode: e.QualityCode ?? (short)0,
            Power: e.Power ?? false,
            StatusCode: e.StatusCode ?? (short)0,
            Progress: (float)(e.Progress ?? 0d),
            CycleTime: (float)(e.CycleTime ?? 0d),
            PartCount: e.PartCount ?? 0L,
            Data1Setpoint: (float)(e.Data1Setpoint ?? 0d),
            Data1Sensor:   (float)(e.Data1Sensor ?? 0d),
            Data2Setpoint: (float)(e.Data2Setpoint ?? 0d),
            Data2Sensor:   (float)(e.Data2Sensor ?? 0d),
            Data3Setpoint: (float)(e.Data3Setpoint ?? 0d),
            Data3Sensor:   (float)(e.Data3Sensor ?? 0d),
            ExternalData1Sensor: (float)(e.ExternalData1Sensor ?? 0d),
            ExternalData2Sensor: (float)(e.ExternalData2Sensor ?? 0d),
            ExternalData3Sensor: (float)(e.ExternalData3Sensor ?? 0d),
            ExternalData4Sensor: (float)(e.ExternalData4Sensor ?? 0d),
            CmdId: (int)(e.CmdId ?? 0L),
            CmdAccepted: (int)(e.CmdAccepted ?? (short)0),
            CmdStatus: (int)(e.CmdStatus ?? (short)0),
            Source: source,
            AgeSeconds: Math.Max(0, age));
    }
}
