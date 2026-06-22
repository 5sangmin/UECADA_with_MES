using BeApi.Api.Middleware;
using BeApi.Shared.Extensions;
using Serilog;
using BeApi.Infrastructure.Persistence.Extensions;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    Log.Information("be-api 시작 중...");

    var builder = WebApplication.CreateBuilder(args);

    builder.Host.UseSerilog((context, services, configuration) =>
        configuration
            .ReadFrom.Configuration(context.Configuration)
            .ReadFrom.Services(services)
            .Enrich.FromLogContext()
            .WriteTo.Console()
    );

    builder.Services.AddControllers();
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen(c =>
    {
        c.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
        {
            Title = "be-api",
            Version = "v1"
        });
    });
    builder.Services.AddHealthChecks();

    // Step 2에서 추가: 공통 설정 등록
    builder.Services.AddSharedSettings(builder.Configuration);
    builder.Services.AddPersistence(builder.Configuration);

    var app = builder.Build();

    // Step 2에서 추가: 전역 예외 처리 (파이프라인 가장 앞)
    app.UseMiddleware<GlobalExceptionMiddleware>();

    if (app.Environment.IsDevelopment())
    {
        app.UseSwagger();
        app.UseSwaggerUI();
    }

    app.UseSerilogRequestLogging();
    app.MapHealthChecks("/health");
    app.MapControllers();

    // PR1(Step3): EF Core 마이그레이션을 수행하지 않으므로
    // 시작 직전에 기존 DB 스키마(필수 테이블/뷰) 존재 여부를 검증한다.
    // 누락 시 InvalidOperationException 으로 fail-fast.
    await app.Services.VerifyDatabaseSchemaAsync(app.Logger);

    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "be-api 시작 실패");
}
finally
{
    Log.CloseAndFlush();
}