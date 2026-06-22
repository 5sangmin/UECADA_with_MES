// src/Features/Latest/LatestServiceExtensions.cs

namespace BeApi.Features.Latest;

public static class LatestServiceExtensions
{
    public static IServiceCollection AddLatestApi(this IServiceCollection services)
    {
        services.AddSingleton<LatestCache>();        // IUdpLiveObserver payload hook 소비자
        services.AddSingleton<LatestRepository>();
        services.AddSingleton<LatestService>();
        return services;
    }
}
