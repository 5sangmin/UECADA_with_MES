// src/Features/UdpRelay/UdpRelayServiceExtensions.cs

using BeApi.Features.Latest;
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

        // Live relay observer 구성
        //  - UdpLiveStatusObserver: PR3 ConnectionStatus 의 UDP 체커 입력
        //  - LatestCache:           PR4 Latest API 의 메모리 캐시 (payload decode)
        //  - CompositeUdpLiveObserver: 위 둘에 fan-out 후 UdpLiveRelayService 에 단일 주입
        //
        // 전제: AddLatestApi() 가 본 메서드보다 먼저 호출되어 LatestCache 가 DI 에 등록되어 있어야 한다.
        services.AddSingleton<UdpLiveStatusObserver>();

        services.AddSingleton<CompositeUdpLiveObserver>(sp =>
        {
            var status = sp.GetRequiredService<UdpLiveStatusObserver>();
            var latest = sp.GetRequiredService<LatestCache>();
            var inner = new IUdpLiveObserver[] { status, latest };
            return new CompositeUdpLiveObserver(inner, status);
        });

        // 단일 IUdpLiveObserver 는 항상 Composite 를 반환.
        // (UdpLiveRelayService, UdpConnectionChecker 모두 이걸 받는다.
        //  Composite 의 status 위임 덕분에 UdpConnectionChecker 도 정확한 수치를 본다.)
        services.AddSingleton<IUdpLiveObserver>(sp => sp.GetRequiredService<CompositeUdpLiveObserver>());

        services.AddHostedService<UdpLiveRelayService>();

        // Replay
        services.AddSingleton<ReplaySnapshotReader>();
        services.AddSingleton<ReplayWorkerFactory>();
        services.AddSingleton<ReplayManager>();

        return services;
    }
}
