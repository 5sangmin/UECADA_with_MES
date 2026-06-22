// src/Features/ConnectionStatus/Checkers/UdpConnectionChecker.cs
//
// UDP-Live 수신 상태 체크.
// 정책 (Q4=A): 3-state 판정. UdpSettings.StaleThresholdSeconds (기본 10초) 재사용.
//   - TotalPackets == 0                       → DOWN ("never received")
//   - now - LastReceivedAt > StaleThreshold   → DEGRADED ("stale")
//   - 그 외                                    → OK

using BeApi.Features.UdpRelay.Live;
using BeApi.Shared.Settings;
using Microsoft.Extensions.Options;

namespace BeApi.Features.ConnectionStatus.Checkers;

public sealed class UdpConnectionChecker : IConnectionChecker
{
    private readonly IUdpLiveObserver _observer;
    private readonly UdpSettings _udp;

    public string Target => "udp";

    public UdpConnectionChecker(IUdpLiveObserver observer, IOptions<UdpSettings> udpOptions)
    {
        _observer = observer;
        _udp = udpOptions.Value;
    }

    public Task<CheckResult> CheckAsync(CancellationToken ct)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();

        var total = _observer.TotalPackets;
        var lastAt = _observer.LastReceivedAt;
        var now = DateTimeOffset.UtcNow;
        var staleAfter = TimeSpan.FromSeconds(Math.Max(1, _udp.StaleThresholdSeconds));

        ConnectionState state;
        string? error;
        TimeSpan? sinceLast = lastAt is null ? null : now - lastAt.Value;

        if (total == 0 || lastAt is null)
        {
            state = ConnectionState.Down;
            error = "패킷 미수신 (total-das 송신 없음 또는 BeApi 리스너 미시작)";
        }
        else if (sinceLast > staleAfter)
        {
            state = ConnectionState.Degraded;
            error = $"마지막 수신 후 {sinceLast.Value.TotalSeconds:F1}s (임계값 {staleAfter.TotalSeconds:F0}s 초과)";
        }
        else
        {
            state = ConnectionState.Ok;
            error = null;
        }

        var detail = new Dictionary<string, object?>
        {
            ["totalPackets"] = total,
            ["lastReceivedAt"] = lastAt,
            ["secondsSinceLast"] = sinceLast?.TotalSeconds,
            ["staleThresholdSeconds"] = staleAfter.TotalSeconds,
            ["lastPacketTsEpochMs"] = _observer.LastPacketTsEpochMs,
            ["lastEquipmentCount"] = _observer.LastEquipmentCount,
            ["listenPort"] = _udp.Relay.ListenPort,
            ["multicastGroup"] = _udp.MulticastGroup,
            ["multicastOutputPort"] = _udp.Relay.OutputPort,
        };

        sw.Stop();
        return Task.FromResult(new CheckResult(state, error, sw.Elapsed.TotalMilliseconds, detail));
    }
}
