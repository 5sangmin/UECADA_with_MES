// src/Features/UdpRelay/Replay/ReplayModels.cs

namespace BeApi.Features.UdpRelay.Replay;

public sealed record ReplayStartRequest(
    DateTimeOffset From,
    DateTimeOffset To,
    double? Speed,
    bool? Loop);

public sealed class ReplaySessionState
{
    public DateTimeOffset From { get; init; }
    public DateTimeOffset To { get; init; }
    public double Speed { get; init; }
    public bool Loop { get; init; }
    public DateTimeOffset StartedAt { get; init; }
    public DateTimeOffset? CurrentTs { get; set; }
    public long SentPackets { get; set; }
    public long SentRecords { get; set; }
    public int LoopCount { get; set; }
}

public enum ReplayStartResult
{
    Started,
    Conflict,      // 이미 진행 중
    NoData,        // 구간에 데이터 없음
    InvalidRange,  // from >= to 등
    InvalidSpeed
}

public sealed record ReplayStartOutcome(
    ReplayStartResult Result,
    ReplaySessionState? State,
    string? Message);
