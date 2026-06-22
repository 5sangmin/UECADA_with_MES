using BeApi.Api.Middleware;
using BeApi.Shared.Extensions;
using BeApi.Shared.Diagnostics;
using Serilog;
using BeApi.Infrastructure.Persistence.Extensions;
using BeApi.Features.UdpRelay;
using BeApi.Features.ConnectionStatus;
using BeApi.Features.Latest;
using BeApi.Features.Commands;
using BeApi.Features.Video;

// PR8(Step14): .env 파일 로딩 (있을 경우에만).
//   - WebApplication.CreateBuilder 가 환경변수를 Configuration 으로 흡수하기 전에
//     반드시 호출되어야 한다 → Log.Logger 설정보다도 먼저.
//   - 파일이 없으면 silent skip.
var envFilePath = EnvFileLoader.LoadIfPresent();

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    Log.Information("be-api 시작 중...");

    if (envFilePath is not null)
    {
        Log.Information(".env 파일 로드 완료: {EnvFilePath}", envFilePath);
    }
    else
    {
        Log.Information(".env 파일을 찾지 못함 — OS 환경변수 / appsettings 만 사용.");
    }

    var builder = WebApplication.CreateBuilder(args);

    builder.Host.UseSerilog((context, services, configuration) =>
        configuration
            .ReadFrom.Configuration(context.Configuration)
            .ReadFrom.Services(services)
            .Enrich.FromLogContext()
            .WriteTo.Console()
    );

    builder.Services.AddControllers();
    // PR6(Step12): 운영용 Razor Pages (대시보드/명령 폼/리스트/상세)
    builder.Services.AddRazorPages();
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

    // PR4(Step 7~8): Latest Data API (UDP 메모리 캐시 + TSDB fallback)
    // ⚠️ 순서 주의: AddUdpRelayAndReplay 이 LatestCache 를 IUdpLiveObserver 로 참조하므로
    //          AddLatestApi 를 먼저 호출해 LatestCache 을 DI 에 등록해둔다.
    builder.Services.AddLatestApi();

    // PR2(Step15): UDP Live Relay + Replay
    builder.Services.AddUdpRelayAndReplay(builder.Configuration);

    // PR3(Step 4~6): Connection Status (opcua/udp/tsdb/commanddb 주기 체크, 메모리만)
    builder.Services.AddConnectionStatus();

    // PR5(Step 9~11): Command API (DTO/validation/idempotency, POST/GET)
    builder.Services.AddCommandsApi();

    // PR7(Step13): Video Streaming API + 운영 화면 (Range 지원)
    builder.Services.AddVideoApi(builder.Configuration);

    var app = builder.Build();

    // Step 2에서 추가: 전역 예외 처리 (파이프라인 가장 앞)
    app.UseMiddleware<GlobalExceptionMiddleware>();

    if (app.Environment.IsDevelopment())
    {
        app.UseSwagger();
        app.UseSwaggerUI();
    }

    app.UseSerilogRequestLogging();

    // PR6(Step12): wwwroot 정적 파일(site.css) + Razor Pages 라우팅
    app.UseStaticFiles();

    app.MapHealthChecks("/health");
    app.MapControllers();
    app.MapRazorPages();

    // PR1(Step3): EF Core 마이그레이션을 수행하지 않으므로
    // 시작 직전에 기존 DB 스키마(필수 테이블/뷰) 존재 여부를 검증한다.
    // 누락 시 InvalidOperationException 으로 fail-fast.
    await app.Services.VerifyDatabaseSchemaAsync(app.Logger);

    // PR8(Step14): 운영 전제 조건 자가진단.
    //   - VIDEO_ROOT 누락/오설정 시 fail-fast (영상 페이지가 죽음 없이 404 가 되는 함정 방지)
    //   - OPC UA / UDP 포트 / Multicast NIC 는 경고만 (실제 바인딩은 HostedService 가 시도)
    StartupSelfCheck.Run(app.Services, app.Logger);

    // PR2(Step15): EquipmentLut 을 즉시 인스턴스화.
    //   - LUT 가 비어있으면 short.TryParse 기반 항등 변환
    //   - LUT 에 값이 있으면 whitelist 동작
    using (var scope = app.Services.CreateScope())
    {
        var lut = scope.ServiceProvider.GetRequiredService<BeApi.Features.UdpRelay.Lut.EquipmentLut>();
        app.Logger.LogInformation(
            "EquipmentLut 초기화 완료. lineWhitelist={lw}({lc}), equipmentWhitelist={ew}({ec})",
            lut.HasLineWhitelist, lut.LineCount,
            lut.HasEquipmentWhitelist, lut.EquipmentCount);
    }

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