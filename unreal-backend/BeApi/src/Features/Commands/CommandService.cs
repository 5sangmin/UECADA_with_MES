// src/Features/Commands/CommandService.cs
//
// Application layer.
//   - CreateAsync: DTO → CommandRequestEntity. commandId 자동 생성.
//                  idempotencyKey 가 있으면 동일 키 row 우선 검사 → 있으면 그 row 반환 (멱등).
//   - GetByIdAsync: 단건 + history + latest 묶음.
//   - GetPagedAsync, GetLatestPerEquipmentAsync.
//
// 트랜잭션은 단일 INSERT 라 별도 트랜잭션 처리 안 함.
// JsonDocument <-> JsonElement 는 PostgresQL jsonb 와 mapping.

using System.Text.Json;
using BeApi.Infrastructure.Persistence.Entities;

namespace BeApi.Features.Commands;

public sealed class CommandService
{
    private readonly ICommandRepository _repo;
    private readonly ILogger<CommandService> _logger;

    public CommandService(ICommandRepository repo, ILogger<CommandService> logger)
    {
        _repo = repo;
        _logger = logger;
    }

    public enum CreateOutcome { Created, ReusedIdempotent, DuplicateCommandId }

    public sealed record CreateResult(CreateOutcome Outcome, CommandResponseDto Dto);

    /// <summary>
    /// POST /api/commands 본문 처리.
    ///  1) headerIdempotencyKey or req.IdempotencyKey 가 있으면 동일 키 검색 → 있으면 그대로 반환 (ReusedIdempotent).
    ///  2) req.CommandId 가 명시되었는데 이미 존재 → DuplicateCommandId.
    ///  3) 아니면 새 commandId 생성하고 INSERT.
    /// </summary>
    public async Task<CreateResult> CreateAsync(
        CreateCommandRequest req,
        string? headerIdempotencyKey,
        CancellationToken ct)
    {
        var idemKey = !string.IsNullOrWhiteSpace(headerIdempotencyKey)
            ? headerIdempotencyKey
            : (string.IsNullOrWhiteSpace(req.IdempotencyKey) ? null : req.IdempotencyKey);

        // 1) idempotency 검사
        if (idemKey != null)
        {
            var existing = await _repo.GetByIdempotencyKeyAsync(idemKey, ct).ConfigureAwait(false);
            if (existing != null)
            {
                _logger.LogInformation("Command 멱등 재사용: idempotencyKey={key}, commandId={cid}", idemKey, existing.CommandId);
                return new CreateResult(CreateOutcome.ReusedIdempotent, ToDto(existing));
            }
        }

        // 2) commandId 처리
        var commandId = string.IsNullOrWhiteSpace(req.CommandId)
            ? GenerateCommandId()
            : req.CommandId!.Trim();

        if (!string.IsNullOrWhiteSpace(req.CommandId))
        {
            var clash = await _repo.GetByCommandIdAsync(commandId, ct).ConfigureAwait(false);
            if (clash != null)
            {
                _logger.LogWarning("Command 중복 commandId 거부: {cid}", commandId);
                return new CreateResult(CreateOutcome.DuplicateCommandId, ToDto(clash));
            }
        }

        // 3) JsonDocument 변환 (jsonb 매핑)
        var commandValueDoc = req.CommandValue.HasValue
            ? JsonDocument.Parse(req.CommandValue.Value.GetRawText())
            : null;

        JsonDocument requestJsonDoc;
        if (req.RequestJson.HasValue)
        {
            requestJsonDoc = JsonDocument.Parse(req.RequestJson.Value.GetRawText());
        }
        else
        {
            // 본 DTO 그대로 직렬화해서 저장
            var raw = JsonSerializer.Serialize(req);
            requestJsonDoc = JsonDocument.Parse(raw);
        }

        var entity = new CommandRequestEntity
        {
            CommandId = commandId,
            SourceType = string.IsNullOrWhiteSpace(req.SourceType) ? "API" : req.SourceType!,
            LineId = req.LineId,
            EquipmentId = req.EquipmentId,
            CommandType = req.CommandType,
            CommandValue = commandValueDoc,
            RequestJson = requestJsonDoc,
            Status = "REQUESTED",
            Priority = req.Priority ?? 100,
            RetryCount = 0,
            MaxRetry = req.MaxRetry ?? 0,
            CreatedBy = req.CreatedBy,
            CreatedAt = DateTimeOffset.UtcNow,
            IdempotencyKey = idemKey,
        };

        var saved = await _repo.CreateAsync(entity, ct).ConfigureAwait(false);
        _logger.LogInformation(
            "Command 신규 생성: commandId={cid}, line={l}, equip={e}, type={t}",
            saved.CommandId, saved.LineId, saved.EquipmentId, saved.CommandType);

        return new CreateResult(CreateOutcome.Created, ToDto(saved));
    }

    public async Task<CommandDetailDto?> GetByCommandIdAsync(string commandId, CancellationToken ct)
    {
        var req = await _repo.GetByCommandIdAsync(commandId, ct).ConfigureAwait(false);
        if (req == null) return null;

        var historyRows = await _repo.GetHistoryAsync(commandId, ct).ConfigureAwait(false);
        var latest = await _repo.GetLatestResponseAsync(req.LineId, req.EquipmentId, ct).ConfigureAwait(false);

        var history = historyRows.Select(ToHistoryDto).ToList();
        var latestDto = latest != null ? ToLatestDto(latest) : null;

        return new CommandDetailDto(ToDto(req), history, latestDto);
    }

    public async Task<CommandPageDto> GetPagedAsync(int page, int pageSize, CancellationToken ct)
    {
        // 가벼운 범위 보정
        page = page < 1 ? 1 : page;
        pageSize = pageSize is < 1 or > 200 ? 50 : pageSize;

        var rows = await _repo.GetPagedAsync(page, pageSize, ct).ConfigureAwait(false);
        var total = await _repo.CountAsync(ct).ConfigureAwait(false);

        var items = rows.Select(ToDto).ToList();
        return new CommandPageDto(page, pageSize, total, items);
    }

    public async Task<CommandLatestResponseDto?> GetLatestPerEquipmentAsync(int lineId, int equipmentId, CancellationToken ct)
    {
        var row = await _repo.GetLatestResponseAsync(lineId, equipmentId, ct).ConfigureAwait(false);
        return row == null ? null : ToLatestDto(row);
    }

    // ----------------- helpers -----------------

    private static string GenerateCommandId()
    {
        // 8 byte timestamp + 4 byte random → URL-safe-ish.
        var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var rand = Random.Shared.Next(0, int.MaxValue);
        return $"cmd-{nowMs:x}-{rand:x8}";
    }

    private static CommandResponseDto ToDto(CommandRequestEntity e)
    {
        return new CommandResponseDto(
            Id: e.Id,
            CommandId: e.CommandId,
            SourceType: e.SourceType,
            LineId: e.LineId,
            EquipmentId: e.EquipmentId,
            CommandType: e.CommandType,
            CommandValue: e.CommandValue is null ? null : Clone(e.CommandValue),
            RequestJson: Clone(e.RequestJson),
            Status: e.Status,
            Priority: e.Priority,
            RetryCount: e.RetryCount,
            MaxRetry: e.MaxRetry,
            CreatedBy: e.CreatedBy,
            CreatedAt: e.CreatedAt,
            StartedAt: e.StartedAt,
            FinishedAt: e.FinishedAt,
            PickedAt: e.PickedAt,
            WorkerId: e.WorkerId,
            LastError: e.LastError,
            IdempotencyKey: e.IdempotencyKey);
    }

    private static CommandHistoryItemDto ToHistoryDto(CommandHistoryViewEntity e)
    {
        return new CommandHistoryItemDto(
            CommandId: e.CommandId,
            LineId: e.LineId,
            EquipmentId: e.EquipmentId,
            CommandType: e.CommandType,
            RequestStatus: e.RequestStatus,
            ResponseEventId: e.ResponseEventId,
            OldStatus: e.OldStatus,
            NewStatus: e.NewStatus,
            Accepted: e.Accepted,
            CmdStatus: e.CmdStatus,
            SourceTsEpochMs: e.SourceTsEpochMs,
            ChangedAt: e.ChangedAt,
            SourceNodeId: e.SourceNodeId,
            ResponseLastError: e.ResponseLastError,
            ProcessIndex: e.ProcessIndex);
    }

    private static CommandLatestResponseDto ToLatestDto(CommandLatestResponseEntity e)
    {
        return new CommandLatestResponseDto(
            LineId: e.LineId,
            EquipmentId: e.EquipmentId,
            CommandId: e.CommandId,
            CommandType: e.CommandType,
            Status: e.Status,
            Accepted: e.Accepted,
            CmdStatus: e.CmdStatus,
            ResponseJson: e.ResponseJson is null ? null : Clone(e.ResponseJson),
            SourceTsEpochMs: e.SourceTsEpochMs,
            FirstObservedAt: e.FirstObservedAt,
            UpdatedAt: e.UpdatedAt,
            SnapshotKey: e.SnapshotKey,
            SourceNodeId: e.SourceNodeId,
            LastError: e.LastError);
    }

    /// <summary>
    /// JsonDocument 를 JsonElement (값 복사) 로 변환.
    /// JsonDocument 가 dispose 되면 JsonElement 가 죽기 때문에, RawText 를 통해 한번 더 파싱한 doc 의 element 를 그대로 사용.
    /// 응답 직렬화 시점에는 doc 의 lifecycle 이 유지되므로 안전 (EF 가 트래킹 안 함, 즉시 응답 직렬화).
    /// </summary>
    private static JsonElement Clone(JsonDocument doc)
    {
        // RootElement 자체를 그대로 노출하면 DbContext 가 닫힌 후 NRE 가능.
        // 안전하게 RawText 로 다시 파싱하고 그 doc 을 정적으로 들고 있는 wrapper 가 필요하지만,
        // ASP.NET 응답 파이프라인에서는 컨트롤러가 끝나기 전 직렬화가 끝나므로 RootElement 만 노출.
        // → 운영 안정성이 더 중요하면 JsonNode/Dictionary 변환 도입을 고려.
        return doc.RootElement.Clone();
    }
}
