// src/Features/UdpRelay/Replay/ReplayManager.cs
//
// 전역 단일 replay 세션 lifecycle 관리.
//
// 정책:
//   - 동시에 최대 한 세션만 실행. 진행 중에 start 요청 시 409 Conflict.
//   - stop 요청 시 즉시 cancel.
//   - 본 매니저는 시작/중지/상태 노출만 책임지고, 실제 송신 loop 는 ReplayWorker 가 수행.

using BeApi.Shared.Settings;
using Microsoft.Extensions.Options;

namespace BeApi.Features.UdpRelay.Replay;

public sealed class ReplayManager : IAsyncDisposable
{
    private readonly UdpSettings _udp;
    private readonly ReplaySnapshotReader _reader;
    private readonly ReplayWorkerFactory _workerFactory;
    private readonly ILogger<ReplayManager> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private CancellationTokenSource? _cts;
    private Task? _workerTask;
    private ReplaySessionState? _currentState;

    public ReplayManager(
        IOptions<UdpSettings> udpOptions,
        ReplaySnapshotReader reader,
        ReplayWorkerFactory workerFactory,
        ILogger<ReplayManager> logger)
    {
        _udp = udpOptions.Value;
        _reader = reader;
        _workerFactory = workerFactory;
        _logger = logger;
    }

    public ReplaySessionState? GetState()
    {
        // snapshot 복사 없이 그대로 반환 (단순 read).
        return _currentState;
    }

    public async Task<ReplayStartOutcome> StartAsync(ReplayStartRequest req, CancellationToken ct)
    {
        // 입력 validate
        if (req.From >= req.To)
            return new ReplayStartOutcome(ReplayStartResult.InvalidRange, null, "from 은 to 보다 이전이어야 합니다.");

        var speed = req.Speed ?? _udp.Replay.DefaultSpeed;
        if (speed < _udp.Replay.MinSpeed || speed > _udp.Replay.MaxSpeed)
            return new ReplayStartOutcome(
                ReplayStartResult.InvalidSpeed,
                null,
                $"speed 는 [{_udp.Replay.MinSpeed}, {_udp.Replay.MaxSpeed}] 범위여야 합니다.");

        var loop = req.Loop ?? false;

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_currentState != null && _workerTask is { IsCompleted: false })
            {
                return new ReplayStartOutcome(
                    ReplayStartResult.Conflict,
                    _currentState,
                    "이미 진행 중인 replay 세션이 있습니다. /api/replay/stop 후 다시 시작하세요.");
            }

            // 0건 fail-fast
            var hasAny = await _reader.HasAnyAsync(req.From, req.To, ct).ConfigureAwait(false);
            if (!hasAny)
            {
                return new ReplayStartOutcome(
                    ReplayStartResult.NoData,
                    null,
                    "해당 구간(equipment_snapshot) 에 데이터가 없습니다.");
            }

            // 이전 세션 청소
            await DisposeWorkerLocked().ConfigureAwait(false);

            var state = new ReplaySessionState
            {
                From = req.From,
                To = req.To,
                Speed = speed,
                Loop = loop,
                StartedAt = DateTimeOffset.UtcNow,
                CurrentTs = null,
                SentPackets = 0,
                SentRecords = 0,
                LoopCount = 0,
            };

            _cts = new CancellationTokenSource();
            _currentState = state;

            var worker = _workerFactory.Create(state, _cts.Token);
            _workerTask = Task.Run(async () =>
            {
                try
                {
                    await worker.RunAsync().ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    _logger.LogInformation("Replay 세션 취소됨.");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Replay 세션 비정상 종료.");
                }
                finally
                {
                    _logger.LogInformation(
                        "Replay 세션 종료. sentPackets={p}, sentRecords={r}, loopCount={l}",
                        state.SentPackets, state.SentRecords, state.LoopCount);
                    _currentState = null;
                }
            }, CancellationToken.None);

            _logger.LogInformation(
                "Replay 시작. from={from}, to={to}, speed={speed}, loop={loop}",
                req.From, req.To, speed, loop);

            return new ReplayStartOutcome(ReplayStartResult.Started, state, null);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<bool> StopAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_currentState == null || _workerTask == null || _workerTask.IsCompleted)
            {
                return false;
            }
            await DisposeWorkerLocked().ConfigureAwait(false);
            return true;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task DisposeWorkerLocked()
    {
        if (_cts != null)
        {
            try { _cts.Cancel(); } catch { /* ignore */ }
        }
        if (_workerTask != null)
        {
            try { await _workerTask.ConfigureAwait(false); } catch { /* 이미 위에서 로그됨 */ }
        }
        _cts?.Dispose();
        _cts = null;
        _workerTask = null;
        _currentState = null;
    }

    public async ValueTask DisposeAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try { await DisposeWorkerLocked().ConfigureAwait(false); }
        finally { _gate.Release(); _gate.Dispose(); }
    }
}
