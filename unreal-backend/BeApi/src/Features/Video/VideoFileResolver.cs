// src/Features/Video/VideoFileResolver.cs
//
// (typeCode, status_code) → 실제 파일 경로 매핑.
//
// 우선순위:
//   1) {root}/{typeCode}/{prefix}{code}.{ext}      (예: CNC/status_1.webm)
//   2) {root}/{typeCode}/{defaultBaseName}.{ext}   (예: CNC/status_default.webm)
//   3) null (호출자가 404 처리)
//
// 보안: 절대 경로 결정 후 RootPath 의 하위에 있는지 재검증 (path traversal 방지).
//       typeCode 는 EquipmentTypeResolver 가 반환한 화이트리스트(CAST/CNC/WASH/ASSY/TEST) 중 하나이므로
//       사용자 입력이 경로에 직접 들어가지 않지만 방어적으로 한 번 더 검사한다.

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

    public string? ResolveVideoPath(string typeCode, int statusCode)
        => Resolve(typeCode, statusCode, _settings.VideoExtension);

    public string? ResolveThumbnailPath(string typeCode, int statusCode)
        => Resolve(typeCode, statusCode, _settings.ThumbnailExtension);

    /// <summary>status 명시 없이 바로 타입별 default 파일을 찾는다 (power-off 등에서 사용).</summary>
    public string? ResolveDefaultVideoPath(string typeCode)
        => ResolveDefault(typeCode, _settings.VideoExtension);

    /// <summary>status 명시 없이 바로 타입별 default 썸네일을 찾는다.</summary>
    public string? ResolveDefaultThumbnailPath(string typeCode)
        => ResolveDefault(typeCode, _settings.ThumbnailExtension);

    private string? ResolveDefault(string typeCode, string extension)
    {
        if (string.IsNullOrWhiteSpace(typeCode))
        {
            return null;
        }

        var root = _settings.GetAbsoluteRoot();
        var typeDir = Path.Combine(root, typeCode);
        var fallback = Path.Combine(typeDir, $"{_settings.DefaultBaseName}.{extension}");
        if (IsWithinRoot(fallback, root) && File.Exists(fallback))
        {
            return fallback;
        }
        _logger.LogWarning(
            "Video resolver: {Type}/{DefaultName}.{Ext} 없음 (power-off fallback)",
            typeCode, _settings.DefaultBaseName, extension);
        return null;
    }

    private string? Resolve(string typeCode, int statusCode, string extension)
    {
        if (string.IsNullOrWhiteSpace(typeCode))
        {
            return null;
        }

        var root = _settings.GetAbsoluteRoot();
        var typeDir = Path.Combine(root, typeCode);

        // 1) status 별 파일
        var primary = Path.Combine(typeDir, $"{_settings.StatusFilePrefix}{statusCode}.{extension}");
        if (IsWithinRoot(primary, root) && File.Exists(primary))
        {
            return primary;
        }

        // 2) 타입별 default fallback
        var fallback = Path.Combine(typeDir, $"{_settings.DefaultBaseName}.{extension}");
        if (IsWithinRoot(fallback, root) && File.Exists(fallback))
        {
            _logger.LogDebug(
                "Video resolver: {Type}/status_{Code}.{Ext} 없음 → {DefaultName} fallback 사용",
                typeCode, statusCode, extension, _settings.DefaultBaseName);
            return fallback;
        }

        // 3) 없음
        _logger.LogWarning(
            "Video resolver: type={type} status={code} ext={ext} 매칭 파일 없음. root={root}",
            typeCode, statusCode, extension, root);
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
