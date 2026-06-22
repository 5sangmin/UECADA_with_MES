// src/Features/ConnectionStatus/ConnectionStatusServiceExtensions.cs

using BeApi.Features.ConnectionStatus.Checkers;

namespace BeApi.Features.ConnectionStatus;

public static class ConnectionStatusServiceExtensions
{
    public static IServiceCollection AddConnectionStatus(this IServiceCollection services)
    {
        // 메모리 저장소
        services.AddSingleton<ConnectionStatusStore>();

        // 4종 체커 (DI 등록 순서 = polling 결과 정렬과 무관, Store 가 알파벳 정렬)
        services.AddSingleton<IConnectionChecker, OpcUaConnectionChecker>();
        services.AddSingleton<IConnectionChecker, UdpConnectionChecker>();
        services.AddSingleton<IConnectionChecker, TsdbConnectionChecker>();
        services.AddSingleton<IConnectionChecker, CommandDbConnectionChecker>();

        // HostedService - 주기 polling
        services.AddHostedService<ConnectionStatusHostedService>();

        return services;
    }
}
