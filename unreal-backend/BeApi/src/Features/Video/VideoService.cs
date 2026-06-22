// src/Features/Video/VideoService.cs
//
// (line, equipment) 의 현재 status_code 를 LatestService 에서 조회 후
// equipmentId → typeCode 로 변환한 뒤 VideoFileResolver 로 실제 파일 경로를 돌려준다.
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
        string? TypeCode,
        int StatusCode,
        string? FilePath,
        string Source); // "status" | "default" | "missing" | "unknown_type"

    public async Task<Resolution> ResolveVideoAsync(int lineId, int equipmentId, CancellationToken ct)
        => await ResolveAsync(lineId, equipmentId, isThumbnail: false, ct).ConfigureAwait(false);

    public async Task<Resolution> ResolveThumbnailAsync(int lineId, int equipmentId, CancellationToken ct)
        => await ResolveAsync(lineId, equipmentId, isThumbnail: true, ct).ConfigureAwait(false);

    private async Task<Resolution> ResolveAsync(int lineId, int equipmentId, bool isThumbnail, CancellationToken ct)
    {
        var typeCode = EquipmentTypeResolver.GetTypeCode(equipmentId);
        if (typeCode == null)
        {
            _logger.LogWarning(
                "Video resolve: equipmentId={e} 가 알려진 타입(1xx/2xx/3xx/4xx/5xx) 이 아닙니다. line={l}",
                equipmentId, lineId);
            return new Resolution(lineId, equipmentId, null, 0, null, "unknown_type");
        }

        var latest = await _latestService.GetLatestAsync((short)lineId, (short)equipmentId, ct).ConfigureAwait(false);
        var statusCode = latest?.StatusCode ?? 0;

        var path = isThumbnail
            ? _resolver.ResolveThumbnailPath(typeCode, statusCode)
            : _resolver.ResolveVideoPath(typeCode, statusCode);

        string source;
        if (path == null)
        {
            source = "missing";
        }
        else
        {
            // 파일명이 status_{code}.{ext} 인지 status_default 인지 구분
            var name = Path.GetFileNameWithoutExtension(path);
            source = name.StartsWith("status_default", StringComparison.Ordinal)
                ? "default"
                : "status";
        }

        _logger.LogDebug(
            "Video resolve: line={l} equip={e} type={t} status={s} kind={k} → {src} ({path})",
            lineId, equipmentId, typeCode, statusCode, isThumbnail ? "thumb" : "video",
            source, path ?? "(none)");

        return new Resolution(lineId, equipmentId, typeCode, statusCode, path, source);
    }
}
