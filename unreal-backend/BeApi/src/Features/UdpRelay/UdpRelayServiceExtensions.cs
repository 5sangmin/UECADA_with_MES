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
        // PR3: NullUdpLiveObserver → UdpLiveStatusObserver 교체.
        // Connection Status 체커(UdpConnectionChecker) 가 이 관측자의 수치를 읽는다.
        services.AddSingleton<UdpLiveStatusObserver>();
        services.AddSingleton<IUdpLiveObserver>(sp => sp.GetRequiredService<UdpLiveStatusObserver>());
        services.AddHostedService<UdpLiveRelayService>();

        // Replay
        services.AddSingleton<ReplaySnapshotReader>();
        services.AddSingleton<ReplayWorkerFactory>();
        services.AddSingleton<ReplayManager>();

        return services;
    }
}
