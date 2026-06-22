using BeApi.Infrastructure.Persistence.Entities;

namespace BeApi.Features.Commands;

public interface ICommandRepository
{
    Task<CommandRequestEntity?> GetByCommandIdAsync(int commandId, CancellationToken ct = default);
    Task<CommandRequestEntity?> GetByIdempotencyKeyAsync(string idempotencyKey, CancellationToken ct = default);
    Task<IReadOnlyList<CommandRequestEntity>> GetPagedAsync(int page, int pageSize, CancellationToken ct = default);
    Task<int> CountAsync(CancellationToken ct = default);
    Task<CommandRequestEntity> CreateAsync(CommandRequestEntity entity, CancellationToken ct = default);

    /// <summary>command_id_seq.nextval — int32 단조 증가 발급.</summary>
    Task<int> NextCommandIdAsync(CancellationToken ct = default);

    Task<IReadOnlyList<CommandHistoryViewEntity>> GetHistoryAsync(int commandId, CancellationToken ct = default);
    Task<CommandLatestResponseEntity?> GetLatestResponseAsync(int lineId, int equipmentId, CancellationToken ct = default);
}