// src/Features/UdpRelay/UdpRelayServiceExtensions.cs

using BeApi.Features.UdpRelay.Live;
using BeApi.Features.UdpRelay.Lut;
using BeApi.Features.UdpRelay.Replay;

namespace BeApi.Features.UdpRelay;

public static class UdpRelayServiceExtensions
{
    public static IServiceCollection AddUdpRelayAndReplay(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // LUT
        services.Configure<EquipmentLutSettings>(
            configuration.GetSection(EquipmentLutSettings.SectionName));
        services.AddSingleton<EquipmentLut>();

        // Live relay
        services.AddSingleton<IUdpLiveObserver, NullUdpLiveObserver>();
        services.AddHostedService<UdpLiveRelayService>();

        // Replay
        services.AddSingleton<ReplaySnapshotReader>();
        services.AddSingleton<ReplayWorkerFactory>();
        services.AddSingleton<ReplayManager>();

        return services;
    }
}
