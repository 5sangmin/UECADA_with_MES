// src/Features/UdpRelay/Replay/ReplaySnapshotReader.cs
//
// TSDB(equipment_snapshot) 로부터 replay 데이터를 읽어 wire EquipmentRecord 로 변환.
//
// 정책:
//   - ts 오름차순으로 streaming. 메모리에 전체 로드하지 않는다.
//   - 그루핑(같은 ts → 한 패킷) 은 ReplayWorker 측에서 수행한다.
//
// TSDB 의 line_id/equipment_id 는 text. EquipmentLut 으로 정수 ID 로 변환한다.

using BeApi.Features.UdpRelay.Lut;
using BeApi.Features.UdpRelay.Wire;
using BeApi.Infrastructure.Persistence;
using BeApi.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace BeApi.Features.UdpRelay.Replay;

public sealed class ReplaySnapshotReader
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly EquipmentLut _lut;
    private readonly ILogger<ReplaySnapshotReader> _logger;

    public ReplaySnapshotReader(
        IServiceScopeFactory scopeFactory,
        EquipmentLut lut,
        ILogger<ReplaySnapshotReader> logger)
    {
        _scopeFactory = scopeFactory;
        _lut = lut;
        _logger = logger;
    }

    /// <summary>
    /// equipment_snapshot 에서 전체 (line_id, equipment_id) distinct 목록을 반환.
    /// Replay 시 장비 슬롯 구성용.
    /// </summary>
    public async Task<IReadOnlyList<EquipmentSlotKey>> GetDistinctEquipmentsAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TsdbDbContext>();
        var rows = await db.EquipmentSnapshots
            .AsNoTracking()
            .Select(e => new { e.LineId, e.EquipmentId })
            .Distinct()
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var list = new List<EquipmentSlotKey>(rows.Count);
        foreach (var r in rows)
        {
            if (!_lut.TryGetLineId(r.LineId, out var lineIdInt))
            {
                _logger.LogWarning("Distinct: 알 수 없는 line_id '{lineId}' skip", r.LineId);
                continue;
            }
            if (!_lut.TryGetEquipmentId(r.EquipmentId, out var equipmentIdInt))
            {
                _logger.LogWarning("Distinct: 알 수 없는 equipment_id '{eqId}' skip", r.EquipmentId);
                continue;
            }
            list.Add(new EquipmentSlotKey(lineIdInt, equipmentIdInt));
        }
        // 정렬 (lineId, equipmentId)
        list.Sort((a, b) =>
        {
            var c = a.LineId.CompareTo(b.LineId);
            return c != 0 ? c : a.EquipmentId.CompareTo(b.EquipmentId);
        });
        return list;
    }

    /// <summary>구간 내 row 가 존재하는지 빠르게 확인. 0건 fail-fast 용도.</summary>
    /// <remarks>
    /// Npgsql 은 timestamptz 컴럼에 DateTimeOffset 을 쓸 때 offset=0(UTC) 만 허용한다.
    /// 따라서 쿼리 직전 UTC 로 변환한다 (절대 시각은 동일).
    /// </remarks>
    public async Task<bool> HasAnyAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        var fromUtc = from.ToUniversalTime();
        var toUtc = to.ToUniversalTime();

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TsdbDbContext>();
        return await db.EquipmentSnapshots
            .AsNoTracking()
            .Where(e => e.Ts >= fromUtc && e.Ts <= toUtc)
            .AnyAsync(ct)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// ts 오름차순으로 EquipmentRecord 를 stream. 변환 실패(LUT 미스) row 는 skip + 경고 로그.
    /// </summary>
    public async IAsyncEnumerable<TsRecord> StreamAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        var fromUtc = from.ToUniversalTime();
        var toUtc = to.ToUniversalTime();

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TsdbDbContext>();

        var query = db.EquipmentSnapshots
            .AsNoTracking()
            .Where(e => e.Ts >= fromUtc && e.Ts <= toUtc)
            .OrderBy(e => e.Ts)
            .ThenBy(e => e.LineId)
            .ThenBy(e => e.EquipmentId);

        await foreach (var e in query.AsAsyncEnumerable().WithCancellation(ct).ConfigureAwait(false))
        {
            if (!_lut.TryGetLineId(e.LineId, out var lineIdInt))
            {
                _logger.LogWarning("Replay: 알 수 없는 line_id '{lineId}' skip", e.LineId);
                continue;
            }
            if (!_lut.TryGetEquipmentId(e.EquipmentId, out var equipmentIdInt))
            {
                _logger.LogWarning("Replay: 알 수 없는 equipment_id '{eqId}' skip", e.EquipmentId);
                continue;
            }

            yield return new TsRecord(e.Ts, MapToWireRecord(e, lineIdInt, equipmentIdInt));
        }
    }

    private static EquipmentRecord MapToWireRecord(
        EquipmentSnapshotEntity e,
        short lineIdInt,
        short equipmentIdInt)
    {
        var tsMs = e.Ts.ToUnixTimeMilliseconds();

        return new EquipmentRecord(
            LineId: lineIdInt,
            EquipmentId: equipmentIdInt,
            TsEpochMs: tsMs,
            Heartbeat: (int)(e.Heartbeat ?? 0L),
            QualityCode: e.QualityCode ?? (short)0,
            Power: (byte)((e.Power ?? false) ? 1 : 0),
            Reserved: 0,
            StatusCode: e.StatusCode ?? (short)0,
            Progress: (float)(e.Progress ?? 0d),
            CycleTime: (float)(e.CycleTime ?? 0d),
            PartCount: (int)(e.PartCount ?? 0L),
            Data1Setpoint: (float)(e.Data1Setpoint ?? 0d),
            Data1Sensor: (float)(e.Data1Sensor ?? 0d),
            Data2Setpoint: (float)(e.Data2Setpoint ?? 0d),
            Data2Sensor: (float)(e.Data2Sensor ?? 0d),
            Data3Setpoint: (float)(e.Data3Setpoint ?? 0d),
            Data3Sensor: (float)(e.Data3Sensor ?? 0d),
            ExternalData1Sensor: (float)(e.ExternalData1Sensor ?? 0d),
            ExternalData2Sensor: (float)(e.ExternalData2Sensor ?? 0d),
            ExternalData3Sensor: (float)(e.ExternalData3Sensor ?? 0d),
            ExternalData4Sensor: (float)(e.ExternalData4Sensor ?? 0d),
            CmdId: (int)(e.CmdId ?? 0L),
            CmdAccepted: (int)(e.CmdAccepted ?? (short)0),
            CmdStatus: (int)(e.CmdStatus ?? (short)0));
    }
}

public readonly record struct TsRecord(DateTimeOffset Ts, EquipmentRecord Record);

/// <summary>27 장비 슬롯을 식별하는 키 (line_id, equipment_id).</summary>
public readonly record struct EquipmentSlotKey(short LineId, short EquipmentId);
