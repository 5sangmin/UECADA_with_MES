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
    /// <remarks>
    /// 시간 파싱 정책:
    ///  - tz offset 이 명시되면 그대로 사용 (예: "2026-06-22T07:09:20Z", "2026-06-22T16:09:20+09:00")
    ///  - tz offset 이 없으면 KST(+09:00) 로 간주 (예: "2026-06-22T16:09:20", "2026-06-22 16:09:20")
    /// </remarks>
    [HttpPost("start")]
    public async Task<IActionResult> Start([FromBody] ReplayStartRequestDto body, CancellationToken ct)
    {
        if (!ReplayTimeParser.TryParse(body?.From, out var from, out var fromReason))
            return BadRequest(new { error = $"from 파싱 실패: {fromReason}" });
        if (!ReplayTimeParser.TryParse(body?.To, out var to, out var toReason))
            return BadRequest(new { error = $"to 파싱 실패: {toReason}" });

        var req = new ReplayStartRequest(
            From: from,
            To: to,
            Speed: body!.Speed,
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

    /// <summary>
    /// One-shot 링크 용 간이 GET 진입점. 내부에서 POST /api/replay/start 과 동일한
    /// ReplayManager.StartAsync 를 호출하여 replay 세션을 시작한다.
    /// </summary>
    /// <remarks>
    /// 시간 지정 우선순위:
    ///   1) last_minutes 가 있으면 서버 현재 시각(UTC)을 기준으로
    ///      from = now - last_minutes, to = now 로 계산.
    ///   2) 없으면 from / to 를 그대로 사용 (ReplayTimeParser 규칙 적용 — tz 없으면 KST).
    /// 파싱 실패나 둘 다 없으면 400.
    ///
    /// 예시:
    ///   GET /api/replay/start-link?last_minutes=10&speed=1&loop=true
    ///   GET /api/replay/start-link?last_minutes=60&speed=10
    ///   GET /api/replay/start-link?from=2026-06-22T15:00:00&to=2026-06-22T15:10:00&speed=2
    ///
    /// 응답은 POST /api/replay/start 과 동일 포맷 (Ok / Conflict / BadRequest).
    /// </remarks>
    [HttpGet("start-link")]
    public async Task<IActionResult> StartLink(
        [FromQuery(Name = "last_minutes")] double? lastMinutes,
        [FromQuery] string? from,
        [FromQuery] string? to,
        [FromQuery] double? speed,
        [FromQuery] bool? loop,
        CancellationToken ct)
    {
        DateTimeOffset fromDto;
        DateTimeOffset toDto;

        if (lastMinutes.HasValue)
        {
            // 상대 시간 모드 — 서버 현재 기준으로 계산.
            if (lastMinutes.Value <= 0)
                return BadRequest(new { error = "last_minutes 는 0보다 커야 합니다." });

            var now = DateTimeOffset.UtcNow;
            fromDto = now - TimeSpan.FromMinutes(lastMinutes.Value);
            toDto = now;
        }
        else
        {
            // 절대 시각 모드 — POST start 와 동일한 파싱 규칙.
            if (string.IsNullOrWhiteSpace(from) || string.IsNullOrWhiteSpace(to))
                return BadRequest(new
                {
                    error = "last_minutes 또는 (from, to) 조합 중 하나는 필수입니다.",
                    usage = new[]
                    {
                        "GET /api/replay/start-link?last_minutes=10&speed=1&loop=true",
                        "GET /api/replay/start-link?from=2026-06-22T15:00:00&to=2026-06-22T15:10:00&speed=2",
                    },
                });

            if (!ReplayTimeParser.TryParse(from, out fromDto, out var fromReason))
                return BadRequest(new { error = $"from 파싱 실패: {fromReason}" });
            if (!ReplayTimeParser.TryParse(to, out toDto, out var toReason))
                return BadRequest(new { error = $"to 파싱 실패: {toReason}" });
        }

        var req = new ReplayStartRequest(
            From: fromDto,
            To: toDto,
            Speed: speed,
            Loop: loop);

        var outcome = await _manager.StartAsync(req, ct);

        // POST /start 와 동일한 매핑 (임의 GET 으로도 단일 엔드포인트로 추적 가능).
        return outcome.Result switch
        {
            ReplayStartResult.Started      => Ok(ToView(outcome.State!)),
            ReplayStartResult.Conflict     => Conflict(new { error = outcome.Message, current = outcome.State == null ? null : ToView(outcome.State) }),
            ReplayStartResult.NoData       => BadRequest(new { error = outcome.Message }),
            ReplayStartResult.InvalidRange => BadRequest(new { error = outcome.Message }),
            ReplayStartResult.InvalidSpeed => BadRequest(new { error = outcome.Message }),
            _ => StatusCode(500, new { error = "알 수 없는 결과" }),
        };
    }

    /// <summary>
    /// One-shot 링크 용 GET stop. POST /api/replay/stop 과 동일한 동작.
    /// </summary>
    /// <remarks>예시: GET /api/replay/stop-link</remarks>
    [HttpGet("stop-link")]
    public async Task<IActionResult> StopLink(CancellationToken ct)
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
        from = s.From,                 // 원본 offset 유지
        fromKst = s.From.ToOffset(ReplayTimeParser.KstOffset),
        to = s.To,
        toKst = s.To.ToOffset(ReplayTimeParser.KstOffset),
        speed = s.Speed,
        loop = s.Loop,
        startedAt = s.StartedAt,
        currentTs = s.CurrentTs,
        currentTsKst = s.CurrentTs?.ToOffset(ReplayTimeParser.KstOffset),
        sentPackets = s.SentPackets,
        sentRecords = s.SentRecords,
        loopCount = s.LoopCount,
    };
}

public sealed class ReplayStartRequestDto
{
    /// <summary>
    /// 시작 시각. tz offset 미기재 시 KST(+09:00) 로 간주.
    /// 예: "2026-06-22T16:09:20" (KST) / "2026-06-22T07:09:20Z" (UTC) / "2026-06-22T16:09:20+09:00".
    /// </summary>
    public string? From { get; set; }

    /// <summary>
    /// 종료 시각. tz offset 미기재 시 KST(+09:00) 로 간주.
    /// </summary>
    public string? To { get; set; }

    public double? Speed { get; set; }
    public bool? Loop { get; set; }
}
