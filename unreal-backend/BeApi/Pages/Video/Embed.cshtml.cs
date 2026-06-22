// Pages/Video/Embed.cshtml.cs
//
// /video/embed/{lineId}/{equipmentId}
//   - Unreal Engine Web Browser 플러그인용 풀스크린 영상 뷰어.
//   - _Layout 을 사용하지 않고 영상만 100vw × 100vh 로 표시.
//   - controls 숨김, autoplay + loop + muted 로 무한 재생.

using BeApi.Features.Video;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace BeApi.Pages.Video;

public class EmbedModel : PageModel
{
    private readonly VideoService _videoService;

    public EmbedModel(VideoService videoService)
    {
        _videoService = videoService;
    }

    [BindProperty(SupportsGet = true)] public int LineId { get; set; }
    [BindProperty(SupportsGet = true)] public int EquipmentId { get; set; }

    public bool HasVideo { get; private set; }
    public string VideoUrl => $"/api/video/{LineId}/{EquipmentId}";
    public string ThumbnailUrl => $"/api/video/{LineId}/{EquipmentId}/thumbnail.jpg";

    public async Task OnGetAsync(CancellationToken ct)
    {
        var resolution = await _videoService.ResolveVideoAsync(LineId, EquipmentId, ct).ConfigureAwait(false);
        HasVideo = resolution.FilePath != null;
    }
}
