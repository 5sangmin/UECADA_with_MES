using System.Text.Json;

namespace BeApi.Infrastructure.Persistence.Entities;

public class CommandRequestEntity
{
    public long Id { get; set; }
    public string CommandId { get; set; } = string.Empty;
    public string SourceType { get; set; } = string.Empty;
    public int LineId { get; set; }
    public int EquipmentId { get; set; }
    public string CommandType { get; set; } = string.Empty;
    public JsonDocument? CommandValue { get; set; }
    public JsonDocument RequestJson { get; set; } = default!;
    public string Status { get; set; } = "REQUESTED";
    public int Priority { get; set; } = 100;
    public int RetryCount { get; set; }
    public int MaxRetry { get; set; }
    public string? CreatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }
    public DateTimeOffset? PickedAt { get; set; }
    public string? WorkerId { get; set; }
    public string? LastError { get; set; }
    public string? IdempotencyKey { get; set; }
}