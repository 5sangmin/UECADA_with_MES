// Pages/Video/Detail.cshtml.cs
//
// /video/{lineId}/{equipmentId}
//   - <video> 태그로 /api/video/{lineId}/{equipmentId} 재생.
//   - 현재 status_code 와 source 도 표시 (페이지 로드 시점 기준).

using BeApi.Features.Video;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace BeApi.Pages.Video;

public class DetailModel : PageModel
{
    private readonly VideoService _videoService;

    public DetailModel(VideoService videoService)
    {
        _videoService = videoService;
    }

    [BindProperty(SupportsGet = true)] public int LineId { get; set; }
    [BindProperty(SupportsGet = true)] public int EquipmentId { get; set; }

    public int StatusCode { get; private set; }
    public string ResolvedSource { get; private set; } = "missing";
    public bool HasVideo { get; private set; }
    public string VideoUrl => $"/api/video/{LineId}/{EquipmentId}";
    public string ThumbnailUrl => $"/api/video/{LineId}/{EquipmentId}/thumbnail.jpg";

    public async Task OnGetAsync(CancellationToken ct)
    {
        var resolution = await _videoService.ResolveVideoAsync(LineId, EquipmentId, ct).ConfigureAwait(false);
        StatusCode = resolution.StatusCode;
        ResolvedSource = resolution.Source;
        HasVideo = resolution.FilePath != null;
    }
}
