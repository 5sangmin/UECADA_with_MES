using BeApi.Infrastructure.Persistence.Entities;

namespace BeApi.Features.Commands;

public interface ICommandRepository
{
    Task<CommandRequestEntity?> GetByCommandIdAsync(string commandId, CancellationToken ct = default);
    Task<CommandRequestEntity?> GetByIdempotencyKeyAsync(string idempotencyKey, CancellationToken ct = default);
    Task<IReadOnlyList<CommandRequestEntity>> GetPagedAsync(int page, int pageSize, CancellationToken ct = default);
    Task<CommandRequestEntity> CreateAsync(CommandRequestEntity entity, CancellationToken ct = default);

    Task<IReadOnlyList<CommandHistoryViewEntity>> GetHistoryAsync(string commandId, CancellationToken ct = default);
    Task<CommandLatestResponseEntity?> GetLatestResponseAsync(int lineId, int equipmentId, CancellationToken ct = default);
}