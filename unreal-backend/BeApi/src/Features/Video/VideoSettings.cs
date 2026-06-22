// src/Features/Video/VideoSettings.cs
//
// PR7 (Step13) Video Streaming 설정.
//
// 환경변수 우선:
//   VIDEO_ROOT  → 절대/상대 경로. 비어 있으면 './videos' (현재 작업 디렉터리 기준).
//
// 디렉터리 컨벤션:
//   {VIDEO_ROOT}/{TYPE}/status_{code}.webm
//   {VIDEO_ROOT}/{TYPE}/status_{code}.jpg        (썸네일, 사전 생성)
//   {VIDEO_ROOT}/{TYPE}/status_default.webm      (타입별 fallback)
//   {VIDEO_ROOT}/{TYPE}/status_default.jpg
//
// TYPE 은 equipmentId 의 백의 자리로 결정 (CAST/CNC/WASH/ASSY/TEST — EquipmentTypeResolver 참조).
// 라인(line) 은 영상 파일 선택에 영향을 주지 않는다 — line 1/2/3 의 같은 타입은 모두 같은 영상.
//
// status_code 값: 0=IDLE, 1=RUNNING, 2=COMPLETED, 3=WARNING, 4=ERROR

namespace BeApi.Features.Video;

public sealed class VideoSettings
{
    /// <summary>비디오 루트 디렉터리 (절대 경로 권장, 상대 경로면 현재 작업 디렉터리 기준).</summary>
    public string RootPath { get; init; } = "./videos";

    /// <summary>비디오 컨테이너 확장자 (기본 webm). UE5 CEF 호환성 우선.</summary>
    public string VideoExtension { get; init; } = "webm";

    /// <summary>썸네일 확장자 (기본 jpg, 사전 생성).</summary>
    public string ThumbnailExtension { get; init; } = "jpg";

    /// <summary>status 별 파일이 없을 때 사용할 기본 파일명 (확장자 제외). 각 타입 폴더 안에 위치.</summary>
    public string DefaultBaseName { get; init; } = "status_default";

    /// <summary>status 별 파일 prefix (예: 'status_1.webm').</summary>
    public string StatusFilePrefix { get; init; } = "status_";

    /// <summary>환경변수 + appsettings 를 합성해 인스턴스 생성.</summary>
    public static VideoSettings Build(IConfiguration configuration)
    {
        var section = configuration.GetSection("Video");
        var fromConfig = section.Get<VideoSettings>() ?? new VideoSettings();

        // 환경변수 우선
        var envRoot = Environment.GetEnvironmentVariable("VIDEO_ROOT");
        if (!string.IsNullOrWhiteSpace(envRoot))
        {
            return new VideoSettings
            {
                RootPath = envRoot,
                VideoExtension = fromConfig.VideoExtension,
                ThumbnailExtension = fromConfig.ThumbnailExtension,
                DefaultBaseName = fromConfig.DefaultBaseName,
                StatusFilePrefix = fromConfig.StatusFilePrefix,
            };
        }
        return fromConfig;
    }

    /// <summary>RootPath 를 절대 경로로 정규화.</summary>
    public string GetAbsoluteRoot()
    {
        return Path.IsPathRooted(RootPath)
            ? Path.GetFullPath(RootPath)
            : Path.GetFullPath(RootPath, Directory.GetCurrentDirectory());
    }
}
