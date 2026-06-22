// src/Features/Latest/LatestController.cs
//
// 결정 (Q1=A):
//   GET /api/latest                           → 전체 슬롯 최신
//   GET /api/latest/{lineId}/{equipmentId}    → 단일 슬롯 최신

using BeApi.Shared.Models;
using Microsoft.AspNetCore.Mvc;

namespace BeApi.Features.Latest;

[ApiController]
[Route("api/latest")]
public sealed class LatestController : ControllerBase
{
    private readonly LatestService _service;

    public LatestController(LatestService service)
    {
        _service = service;
    }

    [HttpGet]
    public async Task<ActionResult<ApiResponse<EquipmentLatestEnvelopeDto>>> GetAll(CancellationToken ct)
    {
        var env = await _service.GetAllLatestAsync(ct).ConfigureAwait(false);
        return Ok(ApiResponse<EquipmentLatestEnvelopeDto>.Ok(env));
    }

    [HttpGet("{lineId:int}/{equipmentId:int}")]
    public async Task<ActionResult<ApiResponse<EquipmentLatestDto>>> GetOne(
        int lineId,
        int equipmentId,
        CancellationToken ct)
    {
        if (lineId < short.MinValue || lineId > short.MaxValue ||
            equipmentId < short.MinValue || equipmentId > short.MaxValue)
        {
            return BadRequest(ApiResponse<EquipmentLatestDto>.Fail(
                "INVALID_ID_RANGE",
                $"lineId/equipmentId 가 short 범위를 벗어남 (lineId={lineId}, equipmentId={equipmentId})"));
        }

        var dto = await _service.GetLatestAsync((short)lineId, (short)equipmentId, ct).ConfigureAwait(false);
        if (dto is null)
        {
            return NotFound(ApiResponse<EquipmentLatestDto>.Fail(
                "EQUIPMENT_NOT_FOUND",
                $"(lineId={lineId}, equipmentId={equipmentId}) 의 최신 데이터를 캐시/TSDB 모두에서 찾을 수 없습니다."));
        }
        return Ok(ApiResponse<EquipmentLatestDto>.Ok(dto));
    }
}
