// src/Features/Commands/CommandServiceExtensions.cs

namespace BeApi.Features.Commands;

public static class CommandServiceExtensions
{
    public static IServiceCollection AddCommandsApi(this IServiceCollection services)
    {
        // CommandService 는 ICommandRepository(Scoped, DbContext 의존) 를 받기 때문에 Scoped.
        services.AddScoped<CommandService>();
        return services;
    }
}
