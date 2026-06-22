// src/Features/Video/VideoFileResolver.cs
//
// status_code → 실제 파일 경로 매핑.
//
// 우선순위:
//   1) {root}/{prefix}{code}.{ext}       (예: status_1.webm)
//   2) {root}/{defaultBaseName}.{ext}    (예: status_default.webm)
//   3) null (호출자가 404 처리)
//
// 보안: 절대 경로 결정 후 RootPath 의 하위에 있는지 재검증 (path traversal 방지).
//       파일명은 status_code(int) 만 사용하므로 사용자 입력이 경로에 직접 들어가지 않지만
//       방어적으로 한 번 더 검사한다.

namespace BeApi.Features.Video;

public sealed class VideoFileResolver
{
    private readonly VideoSettings _settings;
    private readonly ILogger<VideoFileResolver> _logger;

    public VideoFileResolver(VideoSettings settings, ILogger<VideoFileResolver> logger)
    {
        _settings = settings;
        _logger = logger;
    }

    public string? ResolveVideoPath(int statusCode)
        => Resolve(statusCode, _settings.VideoExtension);

    public string? ResolveThumbnailPath(int statusCode)
        => Resolve(statusCode, _settings.ThumbnailExtension);

    private string? Resolve(int statusCode, string extension)
    {
        var root = _settings.GetAbsoluteRoot();

        // 1) status 별 파일
        var primary = Path.Combine(root, $"{_settings.StatusFilePrefix}{statusCode}.{extension}");
        if (IsWithinRoot(primary, root) && File.Exists(primary))
        {
            return primary;
        }

        // 2) default fallback
        var fallback = Path.Combine(root, $"{_settings.DefaultBaseName}.{extension}");
        if (IsWithinRoot(fallback, root) && File.Exists(fallback))
        {
            _logger.LogDebug(
                "Video resolver: status_{code}.{ext} 없음 → default 사용",
                statusCode, extension);
            return fallback;
        }

        // 3) 없음
        _logger.LogWarning(
            "Video resolver: status={code} ext={ext} 매칭 파일 없음. root={root}",
            statusCode, extension, root);
        return null;
    }

    private static bool IsWithinRoot(string candidate, string root)
    {
        var fullCandidate = Path.GetFullPath(candidate);
        var fullRoot = Path.GetFullPath(root);
        return fullCandidate.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            || string.Equals(fullCandidate, fullRoot, StringComparison.Ordinal);
    }
}
