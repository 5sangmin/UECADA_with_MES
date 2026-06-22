// src/Features/ConnectionStatus/Checkers/OpcUaConnectionChecker.cs
//
// 정책 (OPC UA 체크 방식=A): EndpointUrl (opc.tcp://host:port[/path]) 을 파싱하여
// TcpClient 로 host:port 에 단순 접속만 시도한다. OPC UA 핸드셰이크는 수행하지 않는다.
// 라이브러리 의존이 0이고, 가장 가벼운 'reachability' 신호.

using System.Net.Sockets;
using BeApi.Shared.Settings;
using Microsoft.Extensions.Options;

namespace BeApi.Features.ConnectionStatus.Checkers;

public sealed class OpcUaConnectionChecker : IConnectionChecker
{
    private readonly OpcUaSettings _opc;
    private readonly ConnectionCheckSettings _settings;

    public string Target => "opcua";

    public OpcUaConnectionChecker(
        IOptions<OpcUaSettings> opcOptions,
        IOptions<ConnectionCheckSettings> settings)
    {
        _opc = opcOptions.Value;
        _settings = settings.Value;
    }

    public async Task<CheckResult> CheckAsync(CancellationToken ct)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var endpoint = _opc.EndpointUrl;

        if (string.IsNullOrWhiteSpace(endpoint))
        {
            sw.Stop();
            return new CheckResult(
                ConnectionState.Unknown,
                "OpcUa.EndpointUrl 미설정",
                sw.Elapsed.TotalMilliseconds,
                new Dictionary<string, object?> { ["endpoint"] = endpoint });
        }

        if (!TryParseOpcTcp(endpoint, out var host, out var port))
        {
            sw.Stop();
            return new CheckResult(
                ConnectionState.Down,
                $"EndpointUrl 파싱 실패: '{endpoint}' (예: opc.tcp://host:4840)",
                sw.Elapsed.TotalMilliseconds,
                new Dictionary<string, object?> { ["endpoint"] = endpoint });
        }

        using var client = new TcpClient { NoDelay = true };
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(_settings.TimeoutMs);

        try
        {
            await client.ConnectAsync(host, port, cts.Token).ConfigureAwait(false);
            sw.Stop();
            return new CheckResult(
                ConnectionState.Ok,
                null,
                sw.Elapsed.TotalMilliseconds,
                new Dictionary<string, object?> { ["endpoint"] = endpoint, ["host"] = host, ["port"] = port });
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            sw.Stop();
            return new CheckResult(
                ConnectionState.Down,
                $"timeout({_settings.TimeoutMs}ms) connecting to {host}:{port}",
                sw.Elapsed.TotalMilliseconds,
                new Dictionary<string, object?> { ["endpoint"] = endpoint, ["host"] = host, ["port"] = port });
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new CheckResult(
                ConnectionState.Down,
                ex.GetBaseException().Message,
                sw.Elapsed.TotalMilliseconds,
                new Dictionary<string, object?> { ["endpoint"] = endpoint, ["host"] = host, ["port"] = port });
        }
    }

    /// <summary>opc.tcp://host:port[/path] → (host, port).</summary>
    internal static bool TryParseOpcTcp(string url, out string host, out int port)
    {
        host = string.Empty;
        port = 0;
        if (string.IsNullOrWhiteSpace(url)) return false;

        const string scheme = "opc.tcp://";
        var idx = url.IndexOf(scheme, StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return false;

        var rest = url[(idx + scheme.Length)..];
        // path 제거
        var slash = rest.IndexOf('/');
        if (slash >= 0) rest = rest[..slash];

        var colon = rest.LastIndexOf(':');
        if (colon < 0)
        {
            host = rest;
            port = 4840; // OPC UA 기본 포트
            return !string.IsNullOrWhiteSpace(host);
        }

        host = rest[..colon];
        var portStr = rest[(colon + 1)..];
        if (!int.TryParse(portStr, out port)) return false;
        return !string.IsNullOrWhiteSpace(host) && port > 0 && port <= 65535;
    }
}
