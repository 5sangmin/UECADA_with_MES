// src/Features/Video/VideoService.cs
//
// (line, equipment) 의 현재 status_code 를 LatestService 에서 조회 후
// VideoFileResolver 로 실제 파일 경로를 돌려준다.
//
// LatestService.GetLatestAsync 는 캐시 우선 + TSDB fallback 이므로
// 비디오 라우팅도 동일한 데이터 소스를 사용한다 (대시보드와 일관성).

using BeApi.Features.Latest;

namespace BeApi.Features.Video;

public sealed class VideoService
{
    private readonly LatestService _latestService;
    private readonly VideoFileResolver _resolver;
    private readonly ILogger<VideoService> _logger;

    public VideoService(
        LatestService latestService,
        VideoFileResolver resolver,
        ILogger<VideoService> logger)
    {
        _latestService = latestService;
        _resolver = resolver;
        _logger = logger;
    }

    public sealed record Resolution(
        int LineId,
        int EquipmentId,
        int StatusCode,
        string? FilePath,
        string Source); // "status" | "default" | "missing"

    public async Task<Resolution> ResolveVideoAsync(int lineId, int equipmentId, CancellationToken ct)
        => await ResolveAsync(lineId, equipmentId, isThumbnail: false, ct).ConfigureAwait(false);

    public async Task<Resolution> ResolveThumbnailAsync(int lineId, int equipmentId, CancellationToken ct)
        => await ResolveAsync(lineId, equipmentId, isThumbnail: true, ct).ConfigureAwait(false);

    private async Task<Resolution> ResolveAsync(int lineId, int equipmentId, bool isThumbnail, CancellationToken ct)
    {
        var latest = await _latestService.GetLatestAsync((short)lineId, (short)equipmentId, ct).ConfigureAwait(false);
        var statusCode = latest?.StatusCode ?? 0;

        var path = isThumbnail
            ? _resolver.ResolveThumbnailPath(statusCode)
            : _resolver.ResolveVideoPath(statusCode);

        string source;
        if (path == null)
        {
            source = "missing";
        }
        else
        {
            // 파일명이 status_{code}.{ext} 인지 default 인지 구분
            var name = Path.GetFileNameWithoutExtension(path);
            source = name.StartsWith("status_default", StringComparison.Ordinal)
                ? "default"
                : "status";
        }

        _logger.LogDebug(
            "Video resolve: line={l} equip={e} status={s} kind={k} → {src} ({path})",
            lineId, equipmentId, statusCode, isThumbnail ? "thumb" : "video",
            source, path ?? "(none)");

        return new Resolution(lineId, equipmentId, statusCode, path, source);
    }
}
