// src/Features/ConnectionStatus/ConnectionStatusStore.cs
//
// 4종 target 의 최신 ConnectionStatusSnapshot 을 메모리에 유지.
// - DB 적재 없음 (Q2=C 결정).
// - thread-safe (ConcurrentDictionary + per-snapshot lock).

using System.Collections.Concurrent;

namespace BeApi.Features.ConnectionStatus;

public sealed class ConnectionStatusStore
{
    private readonly ConcurrentDictionary<string, ConnectionStatusSnapshot> _snapshots = new();

    /// <summary>HostedService 가 시작 시 호출하여 target 목록을 초기화.</summary>
    public void Register(string target)
    {
        _snapshots.TryAdd(target, new ConnectionStatusSnapshot { Target = target });
    }

    public IReadOnlyCollection<string> Targets => _snapshots.Keys.ToArray();

    /// <summary>체크 1회 결과로 스냅샷 갱신.</summary>
    public void Update(string target, CheckResult result, DateTimeOffset now)
    {
        var snap = _snapshots.GetOrAdd(target, t => new ConnectionStatusSnapshot { Target = t });
        lock (snap)
        {
            snap.State = result.State;
            snap.CheckedAt = now;
            snap.LatencyMs = result.LatencyMs;
            snap.LastError = result.Error;
            snap.Detail = result.Detail;
            if (result.State == ConnectionState.Ok)
            {
                snap.LastSuccessAt = now;
            }
            else if (result.State == ConnectionState.Down || result.State == ConnectionState.Degraded)
            {
                snap.LastFailureAt = now;
            }
        }
    }

    /// <summary>특정 target 의 스냅샷을 DTO 로 복제.</summary>
    public ConnectionStatusDto? GetDto(string target)
    {
        if (!_snapshots.TryGetValue(target, out var snap)) return null;
        lock (snap)
        {
            return ToDto(snap);
        }
    }

    /// <summary>전체 target 을 DTO 로 복제.</summary>
    public IReadOnlyList<ConnectionStatusDto> GetAllDto()
    {
        var list = new List<ConnectionStatusDto>(_snapshots.Count);
        foreach (var snap in _snapshots.Values)
        {
            lock (snap) { list.Add(ToDto(snap)); }
        }
        // target 이름 알파벳 정렬 (일관성)
        list.Sort((a, b) => string.CompareOrdinal(a.Target, b.Target));
        return list;
    }

    /// <summary>전체 종합 상태 산정. 가장 안 좋은 쪽으로 (DOWN > DEGRADED > UNKNOWN > OK).</summary>
    public ConnectionState GetOverall()
    {
        var worst = ConnectionState.Ok;
        var anyUnknown = false;
        var anyChecked = false;
        foreach (var snap in _snapshots.Values)
        {
            lock (snap)
            {
                if (snap.State == ConnectionState.Unknown)
                {
                    anyUnknown = true;
                    continue;
                }
                anyChecked = true;
                if (snap.State == ConnectionState.Down) return ConnectionState.Down;
                if (snap.State == ConnectionState.Degraded && worst != ConnectionState.Down)
                {
                    worst = ConnectionState.Degraded;
                }
            }
        }
        if (!anyChecked && anyUnknown) return ConnectionState.Unknown;
        return worst;
    }

    private static ConnectionStatusDto ToDto(ConnectionStatusSnapshot snap) =>
        new(
            Target: snap.Target,
            State: snap.State.ToString().ToUpperInvariant(),
            CheckedAt: snap.CheckedAt,
            LastSuccessAt: snap.LastSuccessAt,
            LastFailureAt: snap.LastFailureAt,
            LastError: snap.LastError,
            LatencyMs: snap.LatencyMs,
            Detail: snap.Detail);
}
