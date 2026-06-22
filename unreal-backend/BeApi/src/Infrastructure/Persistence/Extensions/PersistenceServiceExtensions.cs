// src/Infrastructure/Persistence/Extensions/PersistenceServiceExtensions.cs
//
// PR1(Step3) 변경 요약:
// - CommandDbContext + TsdbDbContext 두 컨텍스트를 모두 DI 등록한다.
// - 마이그레이션을 절대 수행하지 않는다 (Migrations 폴더 없음, Database.Migrate 호출 없음).
// - 기존 DB(이미 init.sql로 생성됨)에 read/insert만 한다.

using BeApi.Features.Commands;
using Microsoft.EntityFrameworkCore;

namespace BeApi.Infrastructure.Persistence.Extensions;

public static class PersistenceServiceExtensions
{
    public static IServiceCollection AddPersistence(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var commandDbConn = configuration["Database:CommandDbConnectionString"];
        if (string.IsNullOrWhiteSpace(commandDbConn))
        {
            throw new InvalidOperationException(
                "Database:CommandDbConnectionString이 설정되지 않았습니다. " +
                "appsettings.{env}.json 또는 환경변수 Database__CommandDbConnectionString을 확인하세요.");
        }

        var tsdbConn = configuration["Database:TsdbConnectionString"];
        if (string.IsNullOrWhiteSpace(tsdbConn))
        {
            throw new InvalidOperationException(
                "Database:TsdbConnectionString이 설정되지 않았습니다. " +
                "appsettings.{env}.json 또는 환경변수 Database__TsdbConnectionString을 확인하세요.");
        }

        services.AddDbContext<CommandDbContext>(options =>
            options.UseNpgsql(commandDbConn));

        services.AddDbContext<TsdbDbContext>(options =>
            options.UseNpgsql(tsdbConn));

        services.AddScoped<ICommandRepository, CommandRepository>();

        return services;
    }
}
