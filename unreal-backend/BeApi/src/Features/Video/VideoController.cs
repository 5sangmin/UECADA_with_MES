// src/Features/Video/VideoController.cs
//
// GET /api/video/{lineId}/{equipmentId}              → webm 스트림 (Range 지원)
// GET /api/video/{lineId}/{equipmentId}/thumbnail.jpg → 첫 프레임 jpg
//
// PhysicalFile + enableRangeProcessing:true 로 Range 헤더 처리:
//   - 클라이언트가 Range 없으면 200 OK + Content-Length
//   - Range 있으면 206 Partial Content + Content-Range + Accept-Ranges
// HTML5 <video> 태그 (Unreal CEF 포함) 의 seek/buffer 가 정상 동작.

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
                error = "VIDEO_NOT_FOUND",
                message = $"line={lineId}, equipment={equipmentId}, status={res.StatusCode} 에 매칭되는 영상이 없습니다.",
            });
        }

        var contentType = ResolveContentType(res.FilePath, "video/webm");
        // 응답 헤더에 매칭된 status_code / source 를 디버그용으로 노출 (CORS preflight 영향 없음)
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
                error = "THUMBNAIL_NOT_FOUND",
                message = $"line={lineId}, equipment={equipmentId}, status={res.StatusCode} 에 매칭되는 썸네일이 없습니다.",
            });
        }

        var contentType = ResolveContentType(res.FilePath, "image/jpeg");
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
