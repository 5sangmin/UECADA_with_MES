using BeApi.Infrastructure.Persistence;
using BeApi.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace BeApi.Features.Commands;

public class CommandRepository : ICommandRepository
{
    private readonly CommandDbContext _db;

    public CommandRepository(CommandDbContext db)
    {
        _db = db;
    }

    public async Task<CommandRequestEntity?> GetByCommandIdAsync(int commandId, CancellationToken ct = default)
        => await _db.CommandRequests
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.CommandId == commandId, ct);

    public async Task<CommandRequestEntity?> GetByIdempotencyKeyAsync(string idempotencyKey, CancellationToken ct = default)
        => await _db.CommandRequests
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.IdempotencyKey == idempotencyKey, ct);

    public async Task<IReadOnlyList<CommandRequestEntity>> GetPagedAsync(int page, int pageSize, CancellationToken ct = default)
        => await _db.CommandRequests
            .AsNoTracking()
            .OrderByDescending(x => x.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

    public async Task<int> CountAsync(CancellationToken ct = default)
        => await _db.CommandRequests.AsNoTracking().CountAsync(ct);

    public async Task<CommandRequestEntity> CreateAsync(CommandRequestEntity entity, CancellationToken ct = default)
    {
        _db.CommandRequests.Add(entity);
        await _db.SaveChangesAsync(ct);
        return entity;
    }

    public async Task<int> NextCommandIdAsync(CancellationToken ct = default)
    {
        // FromSqlRaw·Scalar 알림 수준으로는 EF 가 제한적. ADO.NET 으로 직접 조회.
        var conn = _db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open)
            await conn.OpenAsync(ct).ConfigureAwait(false);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT nextval('public.command_id_seq')::int";
        var raw = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return Convert.ToInt32(raw);
    }

    public async Task<IReadOnlyList<CommandHistoryViewEntity>> GetHistoryAsync(int commandId, CancellationToken ct = default)
        => await _db.CommandHistories
            .AsNoTracking()
            .Where(x => x.CommandId == commandId)
            .OrderBy(x => x.ChangedAt)
            .ThenBy(x => x.ProcessIndex)
            .ToListAsync(ct);

    public async Task<CommandLatestResponseEntity?> GetLatestResponseAsync(int lineId, int equipmentId, CancellationToken ct = default)
        => await _db.CommandLatestResponses
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.LineId == lineId && x.EquipmentId == equipmentId, ct);
}