// src/Features/Latest/LatestRepository.cs
//
// TSDB(equipment_snapshot) 의 (line_id, equipment_id) 별 최신 row 조회.
// 단일 슬롯 조회와 전체 조회 모두 지원.

using BeApi.Features.UdpRelay.Lut;
using BeApi.Infrastructure.Persistence;
using BeApi.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace BeApi.Features.Latest;

public sealed class LatestRepository
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly EquipmentLut _lut;
    private readonly ILogger<LatestRepository> _logger;

    public LatestRepository(
        IServiceScopeFactory scopeFactory,
        EquipmentLut lut,
        ILogger<LatestRepository> logger)
    {
        _scopeFactory = scopeFactory;
        _lut = lut;
        _logger = logger;
    }

    /// <summary>단일 (lineId, equipmentId) 의 가장 최근 row.</summary>
    public async Task<EquipmentSnapshotEntity?> GetLatestAsync(short lineId, short equipmentId, CancellationToken ct)
    {
        var lineKey = _lut.GetLineKey(lineId);
        var equipKey = _lut.GetEquipmentKey(equipmentId);

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TsdbDbContext>();

        // TSDB 의 line_id/equipment_id 가 text 라서 매칭이 LUT 의 text 키와 일치해야 한다.
        return await db.EquipmentSnapshots
            .AsNoTracking()
            .Where(e => e.LineId == lineKey && e.EquipmentId == equipKey)
            .OrderByDescending(e => e.Ts)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// 전체 슬롯의 최신 row.
    /// 정책: TSDB 에 존재하는 모든 (line_id, equipment_id) 별 가장 최근 ts row 한 줄씩.
    /// EF 의 GroupBy + max ts 조합은 LINQ-to-SQL 한계로 깔끔하지 않아,
    /// 1) DISTINCT (line, eq) 를 먼저 모으고
    /// 2) 각 슬롯별로 FirstOrDefault (병렬) 로 가져온다.
    /// 슬롯 수가 27 수준이라 비용 작음.
    /// </summary>
    public async Task<IReadOnlyList<EquipmentSnapshotEntity>> GetAllLatestAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TsdbDbContext>();

        var slotKeys = await db.EquipmentSnapshots
            .AsNoTracking()
            .Select(e => new { e.LineId, e.EquipmentId })
            .Distinct()
            .ToListAsync(ct)
            .ConfigureAwait(false);

        if (slotKeys.Count == 0) return Array.Empty<EquipmentSnapshotEntity>();

        // 슬롯별로 순차 조회 (트랜잭션 없이 N small queries). N≈27 이라 부담 적음.
        var results = new List<EquipmentSnapshotEntity>(slotKeys.Count);
        foreach (var k in slotKeys)
        {
            var row = await db.EquipmentSnapshots
                .AsNoTracking()
                .Where(e => e.LineId == k.LineId && e.EquipmentId == k.EquipmentId)
                .OrderByDescending(e => e.Ts)
                .FirstOrDefaultAsync(ct)
                .ConfigureAwait(false);
            if (row != null) results.Add(row);
        }

        return results;
    }
}
