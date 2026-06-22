using System.Text.Json;

namespace BeApi.Infrastructure.Persistence.Entities;

public class CommandHistoryViewEntity
{
    public string CommandId { get; set; } = string.Empty;
    public string SourceType { get; set; } = string.Empty;
    public int LineId { get; set; }
    public int EquipmentId { get; set; }
    public string CommandType { get; set; } = string.Empty;
    public JsonDocument? CommandValue { get; set; }
    public JsonDocument RequestJson { get; set; } = default!;
    public string RequestStatus { get; set; } = string.Empty;
    public int Priority { get; set; }
    public int RetryCount { get; set; }
    public int MaxRetry { get; set; }
    public string? CreatedBy { get; set; }
    public DateTimeOffset RequestCreatedAt { get; set; }
    public DateTimeOffset? RequestStartedAt { get; set; }
    public DateTimeOffset? RequestFinishedAt { get; set; }
    public DateTimeOffset? PickedAt { get; set; }
    public string? RequestWorkerId { get; set; }
    public string? RequestLastError { get; set; }
    public string? IdempotencyKey { get; set; }

    public long? ResponseEventId { get; set; }
    public string? OldStatus { get; set; }
    public string? NewStatus { get; set; }
    public bool? Accepted { get; set; }
    public short? CmdStatus { get; set; }
    public JsonDocument? ResponseJson { get; set; }
    public long? SourceTsEpochMs { get; set; }
    public DateTimeOffset? ChangedAt { get; set; }
    public string? SourceNodeId { get; set; }
    public string? ResponseLastError { get; set; }
    public long? ProcessIndex { get; set; }
}