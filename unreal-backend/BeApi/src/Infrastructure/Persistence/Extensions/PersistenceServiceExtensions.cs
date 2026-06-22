using BeApi.Features.Commands;
using Microsoft.EntityFrameworkCore;

namespace BeApi.Infrastructure.Persistence.Extensions;

public static class PersistenceServiceExtensions
{
    public static IServiceCollection AddPersistence(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connStr = configuration["Database:CommandDbConnectionString"]
            ?? throw new InvalidOperationException("CommandDbConnectionString이 설정되지 않았습니다.");

        services.AddDbContext<CommandDbContext>(options =>
            options.UseNpgsql(connStr));

        services.AddScoped<ICommandRepository, CommandRepository>();

        return services;
    }
}