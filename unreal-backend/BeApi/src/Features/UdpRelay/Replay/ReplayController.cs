// src/Features/UdpRelay/Replay/ReplayController.cs

using Microsoft.AspNetCore.Mvc;

namespace BeApi.Features.UdpRelay.Replay;

[ApiController]
[Route("api/replay")]
public sealed class ReplayController : ControllerBase
{
    private readonly ReplayManager _manager;

    public ReplayController(ReplayManager manager) { _manager = manager; }

    /// <summary>전역 단일 replay 세션을 시작한다.</summary>
    [HttpPost("start")]
    public async Task<IActionResult> Start([FromBody] ReplayStartRequestDto body, CancellationToken ct)
    {
        var req = new ReplayStartRequest(
            From: body.From,
            To: body.To,
            Speed: body.Speed,
            Loop: body.Loop);

        var outcome = await _manager.StartAsync(req, ct);

        return outcome.Result switch
        {
            ReplayStartResult.Started     => Ok(ToView(outcome.State!)),
            ReplayStartResult.Conflict    => Conflict(new { error = outcome.Message, current = outcome.State == null ? null : ToView(outcome.State) }),
            ReplayStartResult.NoData      => BadRequest(new { error = outcome.Message }),
            ReplayStartResult.InvalidRange=> BadRequest(new { error = outcome.Message }),
            ReplayStartResult.InvalidSpeed=> BadRequest(new { error = outcome.Message }),
            _ => StatusCode(500, new { error = "알 수 없는 결과" }),
        };
    }

    [HttpPost("stop")]
    public async Task<IActionResult> Stop(CancellationToken ct)
    {
        var stopped = await _manager.StopAsync(ct);
        if (!stopped) return NotFound(new { error = "진행 중인 replay 세션이 없습니다." });
        return Ok(new { stopped = true });
    }

    [HttpGet("status")]
    public IActionResult Status()
    {
        var state = _manager.GetState();
        if (state == null) return Ok(new { status = "idle" });
        return Ok(new
        {
            status = "running",
            session = ToView(state),
        });
    }

    private static object ToView(ReplaySessionState s) => new
    {
        from = s.From,
        to = s.To,
        speed = s.Speed,
        loop = s.Loop,
        startedAt = s.StartedAt,
        currentTs = s.CurrentTs,
        sentPackets = s.SentPackets,
        sentRecords = s.SentRecords,
        loopCount = s.LoopCount,
    };
}

public sealed class ReplayStartRequestDto
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public double? Speed { get; set; }
    public bool? Loop { get; set; }
}
