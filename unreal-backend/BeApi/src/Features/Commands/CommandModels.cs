// src/Features/Commands/CommandModels.cs
//
// PR5 (Step 9~11) Command API DTO.
//
// 결정 (사용자 Q&A):
//   Q1=A: 최소 필드만 받고 commandId/requestJson 는 서버 자동 생성.
//   Q2=A: idempotencyKey 는 선택. 제공되면 동일 키 row 가 있는지 검사.
//   Q4=A: priority/maxRetry/createdBy 등은 nullable, 누락 시 DB default 사용.

using System.ComponentModel.DataAnnotations;
using System.Text.Json;

namespace BeApi.Features.Commands;

/// <summary>POST /api/commands 요청 본문.</summary>
public sealed class CreateCommandRequest
{
    /// <summary>외부에서 commandId 를 강제하고 싶을 때 (선택).</summary>
    public string? CommandId { get; set; }

    /// <summary>요청 소스 분류 (예: "MES", "OPERATOR"). 없으면 "API" 로 저장.</summary>
    public string? SourceType { get; set; }

    [Required]
    public int LineId { get; set; }

    [Required]
    public int EquipmentId { get; set; }

    [Required, MinLength(1), MaxLength(64)]
    public string CommandType { get; set; } = string.Empty;

    /// <summary>jsonb command_value. 명령 파라미터 (free-form JSON).</summary>
    public JsonElement? CommandValue { get; set; }

    /// <summary>전체 요청 원본 JSON (선택). 누락 시 서버가 본 DTO 그대로 직렬화해서 저장.</summary>
    public JsonElement? RequestJson { get; set; }

    public int? Priority { get; set; }
    public int? MaxRetry { get; set; }
    public string? CreatedBy { get; set; }

    /// <summary>body 에서 idempotency 키를 줄 때 사용 (헤더로도 받음).</summary>
    public string? IdempotencyKey { get; set; }
}

/// <summary>command_request row 한 건 응답.</summary>
public sealed record CommandResponseDto(
    long Id,
    string CommandId,
    string SourceType,
    int LineId,
    int EquipmentId,
    string CommandType,
    JsonElement? CommandValue,
    JsonElement RequestJson,
    string Status,
    int Priority,
    int RetryCount,
    int MaxRetry,
    string? CreatedBy,
    DateTimeOffset CreatedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? FinishedAt,
    DateTimeOffset? PickedAt,
    string? WorkerId,
    string? LastError,
    string? IdempotencyKey);

/// <summary>command_history 뷰 row.</summary>
public sealed record CommandHistoryItemDto(
    string CommandId,
    int LineId,
    int EquipmentId,
    string CommandType,
    string RequestStatus,
    long? ResponseEventId,
    string? OldStatus,
    string? NewStatus,
    bool? Accepted,
    short? CmdStatus,
    long? SourceTsEpochMs,
    DateTimeOffset? ChangedAt,
    string? SourceNodeId,
    string? ResponseLastError,
    long? ProcessIndex);

/// <summary>command_latest_response row.</summary>
public sealed record CommandLatestResponseDto(
    int LineId,
    int EquipmentId,
    string CommandId,
    string? CommandType,
    string Status,
    bool? Accepted,
    short? CmdStatus,
    JsonElement? ResponseJson,
    long? SourceTsEpochMs,
    DateTimeOffset FirstObservedAt,
    DateTimeOffset UpdatedAt,
    string? SnapshotKey,
    string? SourceNodeId,
    string? LastError);

/// <summary>GET /api/commands/{commandId} 의 묶음 응답.</summary>
public sealed record CommandDetailDto(
    CommandResponseDto Request,
    IReadOnlyList<CommandHistoryItemDto> History,
    CommandLatestResponseDto? Latest);

/// <summary>페이지 응답 봉투.</summary>
public sealed record CommandPageDto(
    int Page,
    int PageSize,
    int Count,
    IReadOnlyList<CommandResponseDto> Items);
