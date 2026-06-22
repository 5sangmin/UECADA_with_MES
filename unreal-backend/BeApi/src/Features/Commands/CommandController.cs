// src/Features/Commands/CommandController.cs
//
// 엔드포인트:
//   POST   /api/commands
//   GET    /api/commands?page=&pageSize=
//   GET    /api/commands/{commandId:int}                  → request + history + latest
//   GET    /api/commands/latest/{lineId:int}/{equipmentId:int}
//
// Idempotency-Key 헤더 또는 body.idempotency_key.

using BeApi.Shared.Models;
using Microsoft.AspNetCore.Mvc;

namespace BeApi.Features.Commands;

[ApiController]
[Route("api/commands")]
public sealed class CommandController : ControllerBase
{
    private const string IdempotencyHeaderName = "Idempotency-Key";

    private readonly CommandService _service;
    private readonly ILogger<CommandController> _logger;

    public CommandController(CommandService service, ILogger<CommandController> logger)
    {
        _service = service;
        _logger = logger;
    }

    [HttpPost]
    public async Task<IActionResult> Create(
        [FromBody] CreateCommandRequest request,
        CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            var msg = string.Join("; ",
                ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage));
            return BadRequest(ApiResponse<CommandResponseDto>.Fail("VALIDATION_FAILED", msg));
        }

        Request.Headers.TryGetValue(IdempotencyHeaderName, out var headerValues);
        var idemHeader = headerValues.Count > 0 ? headerValues[0] : null;

        var result = await _service.CreateAsync(request, idemHeader, ct).ConfigureAwait(false);

        return result.Outcome switch
        {
            CommandService.CreateOutcome.Created =>
                CreatedAtAction(
                    nameof(GetByCommandId),
                    new { commandId = result.Dto!.CommandId },
                    ApiResponse<CommandResponseDto>.Ok(result.Dto)),

            CommandService.CreateOutcome.ReusedIdempotent =>
                Ok(ApiResponse<CommandResponseDto>.Ok(result.Dto!)),

            CommandService.CreateOutcome.DuplicateCommandId =>
                Conflict(ApiResponse<CommandResponseDto>.Fail(
                    result.ErrorCode!, result.ErrorMessage!)),

            CommandService.CreateOutcome.InvalidEquipmentCode =>
                BadRequest(ApiResponse<CommandResponseDto>.Fail(
                    result.ErrorCode!, result.ErrorMessage!)),

            CommandService.CreateOutcome.InvalidCommandType =>
                BadRequest(ApiResponse<CommandResponseDto>.Fail(
                    result.ErrorCode!, result.ErrorMessage!)),

            _ => StatusCode(StatusCodes.Status500InternalServerError,
                ApiResponse<CommandResponseDto>.Fail("UNKNOWN_OUTCOME", "정의되지 않은 결과"))
        };
    }

    [HttpGet]
    public async Task<ActionResult<ApiResponse<CommandPageDto>>> GetPaged(
        [FromQuery] int page = 1,
        [FromQuery(Name = "page_size")] int pageSize = 50,
        CancellationToken ct = default)
    {
        var dto = await _service.GetPagedAsync(page, pageSize, ct).ConfigureAwait(false);
        return Ok(ApiResponse<CommandPageDto>.Ok(dto));
    }

    [HttpGet("{commandId:int}")]
    public async Task<ActionResult<ApiResponse<CommandDetailDto>>> GetByCommandId(
        int commandId, CancellationToken ct)
    {
        var dto = await _service.GetByCommandIdAsync(commandId, ct).ConfigureAwait(false);
        if (dto == null)
        {
            return NotFound(ApiResponse<CommandDetailDto>.Fail(
                "COMMAND_NOT_FOUND", $"command_id={commandId} 을 찾을 수 없습니다."));
        }
        return Ok(ApiResponse<CommandDetailDto>.Ok(dto));
    }

    [HttpGet("latest/{lineId:int}/{equipmentId:int}")]
    public async Task<ActionResult<ApiResponse<CommandLatestResponseDto>>> GetLatest(
        int lineId, int equipmentId, CancellationToken ct)
    {
        var dto = await _service.GetLatestPerEquipmentAsync(lineId, equipmentId, ct).ConfigureAwait(false);
        if (dto == null)
        {
            return NotFound(ApiResponse<CommandLatestResponseDto>.Fail(
                "LATEST_RESPONSE_NOT_FOUND",
                $"(line={lineId}, equip={equipmentId}) 의 latest response 가 없습니다."));
        }
        return Ok(ApiResponse<CommandLatestResponseDto>.Ok(dto));
    }
}
