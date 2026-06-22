// src/Features/Commands/CommandModels.cs
//
// PR5 (Step 9~11) Command API DTO — snake_case 외부 계약.
//
// 결정 (사용자 Q&A):
//   - command_id: UDP wire 의 int32 (DB 컬럼도 integer).
//   - equipment_id: 외부에서 "CAST-01" 같은 코드 문자열로 들어옴 → 내부 int 로 LUT 변환,
//                   request_json 원본은 코드 문자열 그대로 보존.
//   - line_id: 코드에서 추정 불가 → 외부 입력 필수 (int).
//   - command_type: 카탈로그 기반 검증 (CommandTypeCatalog).
//   - value: primitive (bool/number/string) 또는 임의의 JSON. command_value(jsonb) 에 그대로 저장.

using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BeApi.Features.Commands;

/// <summary>POST /api/commands 요청 본문 (snake_case).</summary>
public sealed class CreateCommandRequest
{
    /// <summary>UDP wire 의 int32 cmd_id 와 동일. 외부에서 명시할 때만 사용 (선택).</summary>
    [JsonPropertyName("command_id")]
    public int? CommandId { get; set; }

    /// <summary>요청 소스 (예: "MES", "OPERATOR"). 누락 시 "API".</summary>
    [JsonPropertyName("source_type")]
    public string? SourceType { get; set; }

    /// <summary>line_id (필수). 코드에서 추정 불가하므로 외부 입력.</summary>
    [JsonPropertyName("line_id")]
    [Required]
    public int LineId { get; set; }

    /// <summary>설비 코드 문자열. 예: "CAST-01", "CNC-02".</summary>
    [JsonPropertyName("equipment_id")]
    [Required, MinLength(1)]
    public string EquipmentId { get; set; } = string.Empty;

    /// <summary>예: "power", "injection_pressure_sp" 등. 설비 prefix 와 조합 검증.</summary>
    [JsonPropertyName("command_type")]
    [Required, MinLength(1), MaxLength(64)]
    public string CommandType { get; set; } = string.Empty;

    /// <summary>명령 값. primitive(bool/number/string) 또는 JSON.</summary>
    [JsonPropertyName("value")]
    public JsonElement? Value { get; set; }

    [JsonPropertyName("priority")]
    public int? Priority { get; set; }

    [JsonPropertyName("max_retry")]
    public int? MaxRetry { get; set; }

    [JsonPropertyName("created_by")]
    public string? CreatedBy { get; set; }

    /// <summary>body 에서 idempotency 키를 줄 때 사용 (헤더로도 받음).</summary>
    [JsonPropertyName("idempotency_key")]
    public string? IdempotencyKey { get; set; }
}

/// <summary>command_request row 한 건 응답 (snake_case).</summary>
public sealed record CommandResponseDto(
    [property: JsonPropertyName("id")] long Id,
    [property: JsonPropertyName("command_id")] int CommandId,
    [property: JsonPropertyName("source_type")] string SourceType,
    [property: JsonPropertyName("line_id")] int LineId,
    [property: JsonPropertyName("equipment_id")] int EquipmentId,
    [property: JsonPropertyName("equipment_code")] string? EquipmentCode,
    [property: JsonPropertyName("command_type")] string CommandType,
    [property: JsonPropertyName("command_value")] JsonElement? CommandValue,
    [property: JsonPropertyName("request_json")] JsonElement RequestJson,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("priority")] int Priority,
    [property: JsonPropertyName("retry_count")] int RetryCount,
    [property: JsonPropertyName("max_retry")] int MaxRetry,
    [property: JsonPropertyName("created_by")] string? CreatedBy,
    [property: JsonPropertyName("created_at")] DateTimeOffset CreatedAt,
    [property: JsonPropertyName("started_at")] DateTimeOffset? StartedAt,
    [property: JsonPropertyName("finished_at")] DateTimeOffset? FinishedAt,
    [property: JsonPropertyName("picked_at")] DateTimeOffset? PickedAt,
    [property: JsonPropertyName("worker_id")] string? WorkerId,
    [property: JsonPropertyName("last_error")] string? LastError,
    [property: JsonPropertyName("idempotency_key")] string? IdempotencyKey);

/// <summary>command_history 뷰 row (snake_case).</summary>
public sealed record CommandHistoryItemDto(
    [property: JsonPropertyName("command_id")] int CommandId,
    [property: JsonPropertyName("line_id")] int LineId,
    [property: JsonPropertyName("equipment_id")] int EquipmentId,
    [property: JsonPropertyName("command_type")] string CommandType,
    [property: JsonPropertyName("request_status")] string RequestStatus,
    [property: JsonPropertyName("response_event_id")] long? ResponseEventId,
    [property: JsonPropertyName("old_status")] string? OldStatus,
    [property: JsonPropertyName("new_status")] string? NewStatus,
    [property: JsonPropertyName("accepted")] bool? Accepted,
    [property: JsonPropertyName("cmd_status")] short? CmdStatus,
    [property: JsonPropertyName("source_tsepochms")] long? SourceTsEpochMs,
    [property: JsonPropertyName("changed_at")] DateTimeOffset? ChangedAt,
    [property: JsonPropertyName("source_node_id")] string? SourceNodeId,
    [property: JsonPropertyName("response_last_error")] string? ResponseLastError,
    [property: JsonPropertyName("process_index")] long? ProcessIndex);

/// <summary>command_latest_response row (snake_case).</summary>
public sealed record CommandLatestResponseDto(
    [property: JsonPropertyName("line_id")] int LineId,
    [property: JsonPropertyName("equipment_id")] int EquipmentId,
    [property: JsonPropertyName("command_id")] int CommandId,
    [property: JsonPropertyName("command_type")] string? CommandType,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("accepted")] bool? Accepted,
    [property: JsonPropertyName("cmd_status")] short? CmdStatus,
    [property: JsonPropertyName("response_json")] JsonElement? ResponseJson,
    [property: JsonPropertyName("source_tsepochms")] long? SourceTsEpochMs,
    [property: JsonPropertyName("first_observed_at")] DateTimeOffset FirstObservedAt,
    [property: JsonPropertyName("updated_at")] DateTimeOffset UpdatedAt,
    [property: JsonPropertyName("snapshot_key")] string? SnapshotKey,
    [property: JsonPropertyName("source_node_id")] string? SourceNodeId,
    [property: JsonPropertyName("last_error")] string? LastError);

/// <summary>GET /api/commands/{commandId} 의 묶음 응답.</summary>
public sealed record CommandDetailDto(
    [property: JsonPropertyName("request")] CommandResponseDto Request,
    [property: JsonPropertyName("history")] IReadOnlyList<CommandHistoryItemDto> History,
    [property: JsonPropertyName("latest")] CommandLatestResponseDto? Latest);

/// <summary>페이지 응답 봉투.</summary>
public sealed record CommandPageDto(
    [property: JsonPropertyName("page")] int Page,
    [property: JsonPropertyName("page_size")] int PageSize,
    [property: JsonPropertyName("count")] int Count,
    [property: JsonPropertyName("items")] IReadOnlyList<CommandResponseDto> Items);
