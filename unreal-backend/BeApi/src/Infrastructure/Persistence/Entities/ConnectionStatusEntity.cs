// src/Infrastructure/Persistence/Entities/ConnectionStatusEntity.cs
namespace BeApi.Infrastructure.Persistence.Entities;

public class ConnectionStatusEntity
{
    public int Id { get; set; }
    public string Target { get; set; } = string.Empty;  // "opcua", "udp", "tsdb", "commanddb"
    public string Status { get; set; } = string.Empty;   // "OK", "DEGRADED", "DOWN"
    public DateTimeOffset CheckedAt { get; set; }
    public DateTimeOffset? LastSuccessAt { get; set; }
    public DateTimeOffset? LastFailureAt { get; set; }
    public string? LastError { get; set; }
    public double? LatencyMs { get; set; }
}