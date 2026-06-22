using System.Text.Json;

namespace BeApi.Infrastructure.Persistence.Entities;

public class CommandResponseEventEntity
{
    public long Id { get; set; }
    public int CommandId { get; set; }
    public int LineId { get; set; }
    public int EquipmentId { get; set; }
    public bool? Accepted { get; set; }
    public short? CmdStatus { get; set; }
    public string Status { get; set; } = string.Empty;
    public JsonDocument? ResponseJson { get; set; }
    public long? SourceTsEpochMs { get; set; }
    public DateTimeOffset ObservedAt { get; set; }
    public string? SnapshotKey { get; set; }
    public string? SourceNodeId { get; set; }
    public string? LastError { get; set; }
}