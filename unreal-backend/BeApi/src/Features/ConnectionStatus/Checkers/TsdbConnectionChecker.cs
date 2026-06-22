// src/Features/ConnectionStatus/Checkers/TsdbConnectionChecker.cs
//
// TSDB(TimescaleDB/Postgres) 연결성 체크.
// - DB.CanConnectAsync (raw connection open) 으로 판정.
// - 정책 (Q2=C): 결과는 메모리에만 저장. EF Core 가 스키마를 만들지 않는다.

using BeApi.Infrastructure.Persistence;
using BeApi.Shared.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace BeApi.Features.ConnectionStatus.Checkers;

public sealed class TsdbConnectionChecker : IConnectionChecker
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ConnectionCheckSettings _settings;

    public string Target => "tsdb";

    public TsdbConnectionChecker(
        IServiceScopeFactory scopeFactory,
        IOptions<ConnectionCheckSettings> settings)
    {
        _scopeFactory = scopeFactory;
        _settings = settings.Value;
    }

    public async Task<CheckResult> CheckAsync(CancellationToken ct)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TsdbDbContext>();

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(_settings.TimeoutMs);

        try
        {
            var ok = await db.Database.CanConnectAsync(cts.Token).ConfigureAwait(false);
            sw.Stop();
            return ok
                ? new CheckResult(ConnectionState.Ok, null, sw.Elapsed.TotalMilliseconds)
                : new CheckResult(ConnectionState.Down, "CanConnect=false", sw.Elapsed.TotalMilliseconds);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            sw.Stop();
            return new CheckResult(ConnectionState.Down, $"timeout({_settings.TimeoutMs}ms)", sw.Elapsed.TotalMilliseconds);
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new CheckResult(ConnectionState.Down, ex.GetBaseException().Message, sw.Elapsed.TotalMilliseconds);
        }
    }
}
