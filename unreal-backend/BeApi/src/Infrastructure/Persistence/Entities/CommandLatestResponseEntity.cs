using System.Text.Json;

namespace BeApi.Infrastructure.Persistence.Entities;

public class CommandLatestResponseEntity
{
    public int LineId { get; set; }
    public int EquipmentId { get; set; }
    public string CommandId { get; set; } = string.Empty;
    public string? CommandType { get; set; }
    public string Status { get; set; } = string.Empty;
    public bool? Accepted { get; set; }
    public short? CmdStatus { get; set; }
    public JsonDocument? ResponseJson { get; set; }
    public long? SourceTsEpochMs { get; set; }
    public DateTimeOffset FirstObservedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public string? SnapshotKey { get; set; }
    public string? SourceNodeId { get; set; }
    public string? LastError { get; set; }
}