// src/Features/ConnectionStatus/ConnectionStatusHostedService.cs
//
// 정책 (Q3=A): ConnectionCheckSettings.IntervalSeconds(기본 30s) 주기로 모든 체커를 병렬 실행.
// 각 체커의 timeout 은 ConnectionCheckSettings.TimeoutMs (기본 5s). Task.WhenAll 로 동시 수행.

using BeApi.Shared.Settings;
using Microsoft.Extensions.Options;

namespace BeApi.Features.ConnectionStatus;

public sealed class ConnectionStatusHostedService : BackgroundService
{
    private readonly IReadOnlyList<IConnectionChecker> _checkers;
    private readonly ConnectionStatusStore _store;
    private readonly ConnectionCheckSettings _settings;
    private readonly ILogger<ConnectionStatusHostedService> _logger;

    public ConnectionStatusHostedService(
        IEnumerable<IConnectionChecker> checkers,
        ConnectionStatusStore store,
        IOptions<ConnectionCheckSettings> settings,
        ILogger<ConnectionStatusHostedService> logger)
    {
        _checkers = checkers.ToArray();
        _store = store;
        _settings = settings.Value;
        _logger = logger;

        // 시작 시 모든 target 사전 등록 → /api/status 가 즉시 UNKNOWN 으로라도 응답 가능
        foreach (var c in _checkers)
        {
            _store.Register(c.Target);
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(Math.Max(1, _settings.IntervalSeconds));
        _logger.LogInformation(
            "ConnectionStatus 폴링 시작. interval={i}s, timeout={t}ms, targets=[{tgts}]",
            interval.TotalSeconds, _settings.TimeoutMs, string.Join(", ", _checkers.Select(c => c.Target)));

        // 시작 즉시 1회
        await RunOnceAsync(stoppingToken).ConfigureAwait(false);

        using var timer = new PeriodicTimer(interval);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            {
                await RunOnceAsync(stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // shutdown
        }

        _logger.LogInformation("ConnectionStatus 폴링 종료.");
    }

    private async Task RunOnceAsync(CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var tasks = _checkers.Select(c => RunOneAsync(c, now, ct)).ToArray();
        await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    private async Task RunOneAsync(IConnectionChecker checker, DateTimeOffset now, CancellationToken ct)
    {
        try
        {
            var result = await checker.CheckAsync(ct).ConfigureAwait(false);
            _store.Update(checker.Target, result, now);

            if (result.State == ConnectionState.Down)
            {
                _logger.LogWarning(
                    "Status[{target}] DOWN. error={err}, latency={ms:F0}ms",
                    checker.Target, result.Error, result.LatencyMs);
            }
            else if (result.State == ConnectionState.Degraded)
            {
                _logger.LogWarning(
                    "Status[{target}] DEGRADED. error={err}, latency={ms:F0}ms",
                    checker.Target, result.Error, result.LatencyMs);
            }
            else
            {
                _logger.LogDebug(
                    "Status[{target}] {state}. latency={ms:F0}ms",
                    checker.Target, result.State, result.LatencyMs);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Status[{target}] 체크 자체 예외", checker.Target);
            _store.Update(checker.Target,
                new CheckResult(ConnectionState.Down, ex.GetBaseException().Message, 0), now);
        }
    }
}
