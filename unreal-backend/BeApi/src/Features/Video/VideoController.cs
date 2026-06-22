// src/Features/Video/VideoController.cs
//
// GET /api/video/{lineId}/{equipmentId}                → webm 스트림 (Range 지원)
// GET /api/video/{lineId}/{equipmentId}/thumbnail.jpg  → 첫 프레임 jpg
//
// PhysicalFile + enableRangeProcessing:true 로 Range 헤더 처리:
//   - 클라이언트가 Range 없으면 200 OK + Content-Length
//   - Range 있으면 206 Partial Content + Content-Range + Accept-Ranges
// HTML5 <video> 태그 (Unreal CEF 포함) 의 seek/buffer 가 정상 동작.
//
// 영상은 equipment 타입(CAST/CNC/WASH/ASSY/TEST) + status_code 로 결정.
// line 은 영상 파일 선택에 영향 없음 — line 1/2/3 의 같은 타입은 같은 파일 공유.

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.StaticFiles;

namespace BeApi.Features.Video;

[ApiController]
[Route("api/video")]
public sealed class VideoController : ControllerBase
{
    private readonly VideoService _service;
    private readonly VideoSettings _settings;
    private static readonly FileExtensionContentTypeProvider _mimeProvider = new();

    public VideoController(VideoService service, VideoSettings settings)
    {
        _service = service;
        _settings = settings;
    }

    [HttpGet("{lineId:int}/{equipmentId:int}")]
    public async Task<IActionResult> GetVideo(int lineId, int equipmentId, CancellationToken ct)
    {
        var res = await _service.ResolveVideoAsync(lineId, equipmentId, ct).ConfigureAwait(false);
        if (res.FilePath == null)
        {
            return NotFound(new
            {
                error = res.Source == "unknown_type" ? "UNKNOWN_EQUIPMENT_TYPE" : "VIDEO_NOT_FOUND",
                message = res.Source == "unknown_type"
                    ? $"equipmentId={equipmentId} 가 알려진 타입(1xx/2xx/3xx/4xx/5xx) 이 아닙니다."
                    : $"line={lineId}, equipment={equipmentId}(type={res.TypeCode}), status={res.StatusCode} 에 매칭되는 영상이 없습니다.",
            });
        }

        var contentType = ResolveContentType(res.FilePath, "video/webm");
        // 응답 헤더에 매칭된 type / status_code / source 를 디버그용으로 노출 (CORS preflight 영향 없음)
        Response.Headers["X-Equipment-Type"] = res.TypeCode ?? string.Empty;
        Response.Headers["X-Status-Code"] = res.StatusCode.ToString();
        Response.Headers["X-Video-Source"] = res.Source;
        return PhysicalFile(res.FilePath, contentType, enableRangeProcessing: true);
    }

    [HttpGet("{lineId:int}/{equipmentId:int}/thumbnail.jpg")]
    public async Task<IActionResult> GetThumbnail(int lineId, int equipmentId, CancellationToken ct)
    {
        var res = await _service.ResolveThumbnailAsync(lineId, equipmentId, ct).ConfigureAwait(false);
        if (res.FilePath == null)
        {
            return NotFound(new
            {
                error = res.Source == "unknown_type" ? "UNKNOWN_EQUIPMENT_TYPE" : "THUMBNAIL_NOT_FOUND",
                message = res.Source == "unknown_type"
                    ? $"equipmentId={equipmentId} 가 알려진 타입(1xx/2xx/3xx/4xx/5xx) 이 아닙니다."
                    : $"line={lineId}, equipment={equipmentId}(type={res.TypeCode}), status={res.StatusCode} 에 매칭되는 썸네일이 없습니다.",
            });
        }

        var contentType = ResolveContentType(res.FilePath, "image/jpeg");
        Response.Headers["X-Equipment-Type"] = res.TypeCode ?? string.Empty;
        Response.Headers["X-Status-Code"] = res.StatusCode.ToString();
        Response.Headers["X-Video-Source"] = res.Source;
        // 썸네일은 단순 이미지 — Range 불필요.
        return PhysicalFile(res.FilePath, contentType);
    }

    private static string ResolveContentType(string path, string fallback)
    {
        return _mimeProvider.TryGetContentType(path, out var ct) ? ct : fallback;
    }
}
