// src/Features/Commands/CommandService.cs
//
// Application layer.
//   - CreateAsync:
//       1) EquipmentCodeResolver 로 "CAST-01" → (prefix, equipmentId).
//       2) CommandTypeCatalog 로 prefix × command_type 검증.
//       3) idempotency_key 가 있으면 동일 키 row 우선 검사 → 재사용 (멱등).
//       4) command_id: 외부 명시값 우선, 없으면 command_id_seq.nextval.
//       5) request_json 에는 원본(코드 문자열 포함) 그대로 저장.
//       6) command_value(jsonb) 에는 req.value 그대로 (primitive 또는 JSON).
//   - GetByIdAsync, GetPagedAsync, GetLatestPerEquipmentAsync.

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

    public enum CreateOutcome
    {
        Created,
        ReusedIdempotent,
        DuplicateCommandId,
        InvalidEquipmentCode,
        InvalidCommandType,
    }

    public sealed record CreateResult(
        CreateOutcome Outcome,
        CommandResponseDto? Dto,
        string? ErrorCode,
        string? ErrorMessage);

    public async Task<CreateResult> CreateAsync(
        CreateCommandRequest req,
        string? headerIdempotencyKey,
        CancellationToken ct)
    {
        // 1) 설비 코드 해석
        if (!EquipmentCodeResolver.TryResolve(req.EquipmentId, out var resolved, out var resolveError))
        {
            return new CreateResult(
                CreateOutcome.InvalidEquipmentCode,
                null,
                "INVALID_EQUIPMENT_CODE",
                resolveError);
        }
        var equipmentIdInt = resolved!.EquipmentId;
        var prefix = resolved.Prefix;

        // 2) command_type 검증
        if (!CommandTypeCatalog.IsValid(prefix, req.CommandType))
        {
            var allowed = string.Join(", ", CommandTypeCatalog.AllowedFor(prefix));
            return new CreateResult(
                CreateOutcome.InvalidCommandType,
                null,
                "INVALID_COMMAND_TYPE",
                $"command_type='{req.CommandType}' 은 prefix='{prefix}' 에 허용되지 않습니다. 허용: [{allowed}]");
        }

        // 3) idempotency 검사
        var idemKey = !string.IsNullOrWhiteSpace(headerIdempotencyKey)
            ? headerIdempotencyKey
            : (string.IsNullOrWhiteSpace(req.IdempotencyKey) ? null : req.IdempotencyKey);

        if (idemKey != null)
        {
            var existing = await _repo.GetByIdempotencyKeyAsync(idemKey, ct).ConfigureAwait(false);
            if (existing != null)
            {
                _logger.LogInformation(
                    "Command 멱등 재사용: idempotency_key={key}, command_id={cid}",
                    idemKey, existing.CommandId);
                return new CreateResult(
                    CreateOutcome.ReusedIdempotent,
                    ToDto(existing, req.EquipmentId),
                    null, null);
            }
        }

        // 4) command_id 결정
        int commandId;
        if (req.CommandId.HasValue)
        {
            commandId = req.CommandId.Value;
            var clash = await _repo.GetByCommandIdAsync(commandId, ct).ConfigureAwait(false);
            if (clash != null)
            {
                _logger.LogWarning("Command 중복 command_id 거부: {cid}", commandId);
                return new CreateResult(
                    CreateOutcome.DuplicateCommandId,
                    ToDto(clash, req.EquipmentId),
                    "DUPLICATE_COMMAND_ID",
                    $"command_id={commandId} 이 이미 존재합니다.");
            }
        }
        else
        {
            commandId = await _repo.NextCommandIdAsync(ct).ConfigureAwait(false);
        }

        // 5) command_value 변환 — primitive 도 그대로 jsonb 에 저장 (jsonb 는 primitive 허용).
        var commandValueDoc = req.Value.HasValue
            ? JsonDocument.Parse(req.Value.Value.GetRawText())
            : null;

        // 6) request_json 은 외부 입력 원본 그대로 보존 (CAST-01 같은 코드 문자열 포함).
        //    외부에서 별도 request_json 을 주지 않으면 본 DTO 를 직렬화.
        var rawDtoJson = JsonSerializer.Serialize(new
        {
            command_id = req.CommandId,
            source_type = req.SourceType,
            line_id = req.LineId,
            equipment_id = req.EquipmentId, // 원본 (코드 문자열)
            command_type = req.CommandType,
            value = req.Value,
            priority = req.Priority,
            max_retry = req.MaxRetry,
            created_by = req.CreatedBy,
            idempotency_key = req.IdempotencyKey,
        });
        var requestJsonDoc = JsonDocument.Parse(rawDtoJson);

        var entity = new CommandRequestEntity
        {
            CommandId = commandId,
            SourceType = string.IsNullOrWhiteSpace(req.SourceType) ? "API" : req.SourceType!,
            LineId = req.LineId,
            EquipmentId = equipmentIdInt,
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
            "Command 신규 생성: command_id={cid}, line={l}, equip={ec}({eid}), type={t}",
            saved.CommandId, saved.LineId, req.EquipmentId, saved.EquipmentId, saved.CommandType);

        return new CreateResult(
            CreateOutcome.Created,
            ToDto(saved, req.EquipmentId),
            null, null);
    }

    public async Task<CommandDetailDto?> GetByCommandIdAsync(int commandId, CancellationToken ct)
    {
        var req = await _repo.GetByCommandIdAsync(commandId, ct).ConfigureAwait(false);
        if (req == null) return null;

        var historyRows = await _repo.GetHistoryAsync(commandId, ct).ConfigureAwait(false);
        var latest = await _repo.GetLatestResponseAsync(req.LineId, req.EquipmentId, ct).ConfigureAwait(false);

        var history = historyRows.Select(ToHistoryDto).ToList();
        var latestDto = latest != null ? ToLatestDto(latest) : null;

        return new CommandDetailDto(ToDto(req, equipmentCodeOrigin: null), history, latestDto);
    }

    public async Task<CommandPageDto> GetPagedAsync(int page, int pageSize, CancellationToken ct)
    {
        page = page < 1 ? 1 : page;
        pageSize = pageSize is < 1 or > 200 ? 50 : pageSize;

        var rows = await _repo.GetPagedAsync(page, pageSize, ct).ConfigureAwait(false);
        var total = await _repo.CountAsync(ct).ConfigureAwait(false);

        var items = rows.Select(r => ToDto(r, equipmentCodeOrigin: null)).ToList();
        return new CommandPageDto(page, pageSize, total, items);
    }

    public async Task<CommandLatestResponseDto?> GetLatestPerEquipmentAsync(int lineId, int equipmentId, CancellationToken ct)
    {
        var row = await _repo.GetLatestResponseAsync(lineId, equipmentId, ct).ConfigureAwait(false);
        return row == null ? null : ToLatestDto(row);
    }

    // ----------------- helpers -----------------

    /// <summary>
    /// EquipmentEntity.EquipmentId (int) → 추정 equipment_code 문자열.
    /// 외부에서 받은 원본 code 가 있으면 그걸 우선 사용. 없으면 base 역산.
    /// </summary>
    private static CommandResponseDto ToDto(CommandRequestEntity e, string? equipmentCodeOrigin)
    {
        var code = equipmentCodeOrigin ?? TryReverseCode(e.EquipmentId);
        return new CommandResponseDto(
            Id: e.Id,
            CommandId: e.CommandId,
            SourceType: e.SourceType,
            LineId: e.LineId,
            EquipmentId: e.EquipmentId,
            EquipmentCode: code,
            CommandType: e.CommandType,
            CommandValue: e.CommandValue is null ? null : e.CommandValue.RootElement.Clone(),
            RequestJson: e.RequestJson.RootElement.Clone(),
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

    private static string? TryReverseCode(int equipmentId)
    {
        // 100~599 범위에서 prefix 역산.
        if (equipmentId is < 100 or > 599) return null;
        var prefixBase = equipmentId / 100 * 100;
        var prefix = prefixBase switch
        {
            100 => "CAST",
            200 => "CNC",
            300 => "WASH",
            400 => "ASSY",
            500 => "TEST",
            _ => null,
        };
        if (prefix == null) return null;
        var yy = equipmentId - prefixBase;
        return $"{prefix}-{yy:D2}";
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
            ResponseJson: e.ResponseJson is null ? null : e.ResponseJson.RootElement.Clone(),
            SourceTsEpochMs: e.SourceTsEpochMs,
            FirstObservedAt: e.FirstObservedAt,
            UpdatedAt: e.UpdatedAt,
            SnapshotKey: e.SnapshotKey,
            SourceNodeId: e.SourceNodeId,
            LastError: e.LastError);
    }
}
