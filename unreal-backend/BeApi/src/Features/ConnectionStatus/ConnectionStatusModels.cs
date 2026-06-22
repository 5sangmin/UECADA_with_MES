// src/Features/ConnectionStatus/ConnectionStatusModels.cs
//
// PR3 (Step 4~6) Connection Status 모델.

namespace BeApi.Features.ConnectionStatus;

/// <summary>OK / DEGRADED / DOWN / UNKNOWN.</summary>
public enum ConnectionState
{
    Unknown = 0,
    Ok = 1,
    Degraded = 2,
    Down = 3,
}

/// <summary>체크 한 번의 결과.</summary>
public readonly record struct CheckResult(
    ConnectionState State,
    string? Error,
    double LatencyMs,
    IReadOnlyDictionary<string, object?>? Detail = null);

/// <summary>각 체커가 구현하는 인터페이스.</summary>
public interface IConnectionChecker
{
    /// <summary>"opcua" / "udp" / "tsdb" / "commanddb".</summary>
    string Target { get; }

    Task<CheckResult> CheckAsync(CancellationToken ct);
}

/// <summary>메모리 저장소에 들어가는 누적 스냅샷.</summary>
public sealed class ConnectionStatusSnapshot
{
    public required string Target { get; init; }
    public ConnectionState State { get; set; } = ConnectionState.Unknown;
    public DateTimeOffset? CheckedAt { get; set; }
    public DateTimeOffset? LastSuccessAt { get; set; }
    public DateTimeOffset? LastFailureAt { get; set; }
    public string? LastError { get; set; }
    public double? LatencyMs { get; set; }
    public IReadOnlyDictionary<string, object?>? Detail { get; set; }
}

/// <summary>API 응답 DTO.</summary>
public sealed record ConnectionStatusDto(
    string Target,
    string State,
    DateTimeOffset? CheckedAt,
    DateTimeOffset? LastSuccessAt,
    DateTimeOffset? LastFailureAt,
    string? LastError,
    double? LatencyMs,
    IReadOnlyDictionary<string, object?>? Detail);

/// <summary>전체 통합 응답.</summary>
public sealed record ConnectionStatusOverallDto(
    string Overall,                          // OK/DEGRADED/DOWN/UNKNOWN
    IReadOnlyList<ConnectionStatusDto> Targets);
