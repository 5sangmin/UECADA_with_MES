// src/Features/ConnectionStatus/ConnectionStatusController.cs
//
// 정책 (Q5=B): 통합 + 개별 엔드포인트 모두 제공.
//   GET /api/status              → 4종 통합 + overall
//   GET /api/status/{target}     → tsdb | commanddb | udp | opcua

using BeApi.Shared.Models;
using Microsoft.AspNetCore.Mvc;

namespace BeApi.Features.ConnectionStatus;

[ApiController]
[Route("api/status")]
public sealed class ConnectionStatusController : ControllerBase
{
    private readonly ConnectionStatusStore _store;

    public ConnectionStatusController(ConnectionStatusStore store)
    {
        _store = store;
    }

    /// <summary>4종 체커의 최신 스냅샷 + overall 상태.</summary>
    [HttpGet]
    public ActionResult<ApiResponse<ConnectionStatusOverallDto>> GetAll()
    {
        var targets = _store.GetAllDto();
        var overall = _store.GetOverall().ToString().ToUpperInvariant();
        return Ok(ApiResponse<ConnectionStatusOverallDto>.Ok(
            new ConnectionStatusOverallDto(overall, targets)));
    }

    /// <summary>특정 target 의 스냅샷.</summary>
    [HttpGet("{target}")]
    public ActionResult<ApiResponse<ConnectionStatusDto>> GetOne(string target)
    {
        var normalized = (target ?? string.Empty).Trim().ToLowerInvariant();
        var dto = _store.GetDto(normalized);
        if (dto is null)
        {
            return NotFound(ApiResponse<ConnectionStatusDto>.Fail(
                "TARGET_NOT_FOUND",
                $"'{target}' 은(는) 등록된 체크 대상이 아닙니다. 사용 가능: {string.Join(", ", _store.Targets)}"));
        }
        return Ok(ApiResponse<ConnectionStatusDto>.Ok(dto));
    }
}
