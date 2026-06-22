// src/Infrastructure/Persistence/Extensions/DatabaseStartupHealthCheck.cs
//
// PR1(Step3) 신규:
// 본 backend는 EF Core 마이그레이션을 수행하지 않는다 (기존 DB 보존 정책).
// 대신 startup 시점에 필수 테이블/뷰가 존재하는지 가벼운 쿼리로 검증해
// 잘못된 DB에 붙거나 스키마가 누락된 환경에서 fail-fast 한다.
//
// 검증 항목:
//   CommandDB(PostgreSQL):
//     - public.command_request
//     - public.command_response_event
//     - public.command_latest_response
//     - public.command_history (view)
//   TSDB(TimescaleDB):
//     - public.equipment_snapshot

using BeApi.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BeApi.Infrastructure.Persistence.Extensions;

public static class DatabaseStartupHealthCheck
{
    /// <summary>
    /// 필수 테이블/뷰의 존재 여부를 점검한다. 한 항목이라도 실패하면
    /// <see cref="InvalidOperationException"/>을 던져 backend 기동을 거부한다.
    /// </summary>
    public static async Task VerifyDatabaseSchemaAsync(this IServiceProvider services, ILogger logger)
    {
        using var scope = services.CreateScope();

        await CheckCommandDbAsync(scope.ServiceProvider, logger);
        await CheckTsdbAsync(scope.ServiceProvider, logger);
    }

    private static async Task CheckCommandDbAsync(IServiceProvider sp, ILogger logger)
    {
        var db = sp.GetRequiredService<CommandDbContext>();

        // 1) 연결 가능 여부
        try
        {
            if (!await db.Database.CanConnectAsync())
            {
                throw new InvalidOperationException("CommandDB 연결 실패: CanConnectAsync()가 false를 반환했습니다.");
            }
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            throw new InvalidOperationException(
                "CommandDB 연결 중 예외 발생. ConnectionString과 PostgreSQL 기동 상태를 확인하세요.", ex);
        }

        // 2) 필수 테이블/뷰 존재 검증 (LIMIT 0로 데이터는 가져오지 않는다)
        string[] requiredObjects =
        {
            "public.command_request",
            "public.command_response_event",
            "public.command_latest_response",
            "public.command_history"
        };

        foreach (var obj in requiredObjects)
        {
            await ExecuteScalarOrThrowAsync(
                db,
                sql: $"SELECT 1 FROM {obj} LIMIT 0",
                description: $"CommandDB.{obj}",
                logger: logger);
        }

        logger.LogInformation("CommandDB 스키마 검증 완료 (테이블 {Count}개).", requiredObjects.Length);
    }

    private static async Task CheckTsdbAsync(IServiceProvider sp, ILogger logger)
    {
        var db = sp.GetRequiredService<TsdbDbContext>();

        try
        {
            if (!await db.Database.CanConnectAsync())
            {
                throw new InvalidOperationException("TSDB 연결 실패: CanConnectAsync()가 false를 반환했습니다.");
            }
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            throw new InvalidOperationException(
                "TSDB 연결 중 예외 발생. ConnectionString과 TimescaleDB 기동 상태를 확인하세요.", ex);
        }

        await ExecuteScalarOrThrowAsync(
            db,
            sql: "SELECT 1 FROM public.equipment_snapshot LIMIT 0",
            description: "TSDB.public.equipment_snapshot",
            logger: logger);

        logger.LogInformation("TSDB 스키마 검증 완료.");
    }

    private static async Task ExecuteScalarOrThrowAsync(
        DbContext db,
        string sql,
        string description,
        ILogger logger)
    {
        try
        {
            var conn = db.Database.GetDbConnection();
            if (conn.State != System.Data.ConnectionState.Open)
            {
                await conn.OpenAsync();
            }

            using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            cmd.CommandTimeout = 5;
            await cmd.ExecuteScalarAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "필수 DB 객체 검증 실패: {Description}", description);
            throw new InvalidOperationException(
                $"필수 DB 객체 '{description}' 가 존재하지 않거나 접근할 수 없습니다. " +
                $"database/*/init/*.sql 이 정상 적용되었는지 확인하세요.", ex);
        }
    }
}
