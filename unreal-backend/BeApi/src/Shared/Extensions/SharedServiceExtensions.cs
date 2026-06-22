// src/Shared/Extensions/SharedServiceExtensions.cs
using BeApi.Shared.Settings;

namespace BeApi.Shared.Extensions;

public static class SharedServiceExtensions
{
    public static IServiceCollection AddSharedSettings(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<OpcUaSettings>(
            configuration.GetSection(OpcUaSettings.SectionName));

        services.Configure<UdpSettings>(
            configuration.GetSection(UdpSettings.SectionName));

        services.Configure<DatabaseSettings>(
            configuration.GetSection(DatabaseSettings.SectionName));

        services.Configure<ConnectionCheckSettings>(
            configuration.GetSection(ConnectionCheckSettings.SectionName));

        return services;  // 체이닝 가능하도록 services 반환
    }
}