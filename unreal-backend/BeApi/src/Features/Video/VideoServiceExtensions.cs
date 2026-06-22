// src/Features/Video/VideoServiceExtensions.cs
//
// DI 등록 헬퍼.

namespace BeApi.Features.Video;

public static class VideoServiceExtensions
{
    public static IServiceCollection AddVideoApi(this IServiceCollection services, IConfiguration configuration)
    {
        var settings = VideoSettings.Build(configuration);
        services.AddSingleton(settings);
        services.AddSingleton<VideoFileResolver>();
        services.AddSingleton<VideoService>();
        return services;
    }
}
