// src/Shared/Diagnostics/StartupSelfCheck.cs
//
// PR8(Step14): 기동 시 자가진단 (Startup Self-Check).
//
// 정책:
//   - DB 스키마 검증은 이미 VerifyDatabaseSchemaAsync 가 fail-fast 로 수행하므로 중복하지 않는다.
//   - 본 self-check 는 그 외의 운영 전제 조건을 점검:
//       1) VIDEO_ROOT 디렉터리 존재 + 알려진 타입 폴더(CAST/CNC/WASH/ASSY/TEST) 중 최소 1개 존재
//          → fail-fast (실패 시 InvalidOperationException)
//       2) OPC UA endpoint URL 형식 (opc.tcp:// 시작)
//          → 경고만
//       3) UDP 포트(Relay.ListenPort, Replay.OutputPort) 점유 여부 사전 확인
//          → 경고만 (실제 바인딩은 HostedService 가 시도, 실패 시 거기서 로그됨)
//       4) Multicast 인터페이스 IP 가 실제 NIC 에 존재하는지 (Windows 멀티 NIC 환경 보조)
//          → 경고만
//
// fail-fast 선정 기준:
//   - "기동 후에는 복구 불가능하고, 사용자가 즉시 알아야 하는 항목" 만 fail-fast.
//   - VIDEO_ROOT 가 잘못되면 모든 영상 페이지가 404 가 되어 디버깅이 어렵고, 운영자가
//     기동 직후에 인지하지 못하면 한참 뒤에야 발견 → fail-fast.
//   - DB 는 기존 VerifyDatabaseSchemaAsync 가 fail-fast.

using BeApi.Features.Video;
using BeApi.Shared.Settings;
using Microsoft.Extensions.Options;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace BeApi.Shared.Diagnostics;

public static class StartupSelfCheck
{
    /// <summary>
    /// 기동 시 자가진단 실행. fail-fast 항목 실패 시 InvalidOperationException.
    /// </summary>
    public static void Run(IServiceProvider services, ILogger logger)
    {
        logger.LogInformation("=== Startup self-check 시작 ===");

        // (1) VIDEO_ROOT 검증 — fail-fast
        CheckVideoRoot(services, logger);

        // (2) OPC UA endpoint 형식 — 경고
        CheckOpcUaEndpoint(services, logger);

        // (3) UDP 포트 점유 사전 확인 — 경고
        CheckUdpPorts(services, logger);

        // (4) Multicast 인터페이스 IP — 경고
        CheckMulticastInterface(services, logger);

        logger.LogInformation("=== Startup self-check 완료 ===");
    }

    private static void CheckVideoRoot(IServiceProvider services, ILogger logger)
    {
        var settings = services.GetRequiredService<VideoSettings>();
        var root = settings.GetAbsoluteRoot();

        if (!Directory.Exists(root))
        {
            throw new InvalidOperationException(
                $"VIDEO_ROOT 디렉터리가 존재하지 않습니다: {root}. " +
                "환경변수 VIDEO_ROOT 또는 appsettings.Video:RootPath 설정을 확인하세요. " +
                "예: VIDEO_ROOT=D:\\videos 또는 ./videos");
        }

        // 알려진 타입 폴더 중 최소 하나는 있어야 비디오 페이지가 의미 있음.
        var knownTypes = new[] { "CAST", "CNC", "WASH", "ASSY", "TEST" };
        var foundTypes = knownTypes.Where(t => Directory.Exists(Path.Combine(root, t))).ToArray();
        if (foundTypes.Length == 0)
        {
            throw new InvalidOperationException(
                $"VIDEO_ROOT={root} 안에 알려진 타입 폴더(CAST/CNC/WASH/ASSY/TEST) 중 어느 것도 존재하지 않습니다. " +
                "최소 한 개 이상의 타입 폴더와 status_default.webm 파일이 필요합니다.");
        }

        logger.LogInformation(
            "VIDEO_ROOT OK: {Root} (타입 폴더 발견: {Types})",
            root, string.Join(", ", foundTypes));

        // 각 타입 폴더에 default 파일이 있는지 확인 (없으면 경고)
        foreach (var type in foundTypes)
        {
            var defaultVideo = Path.Combine(root, type, $"{settings.DefaultBaseName}.{settings.VideoExtension}");
            if (!File.Exists(defaultVideo))
            {
                logger.LogWarning(
                    "VIDEO_ROOT/{Type} 에 기본 영상이 없습니다: {Path}. " +
                    "status 매칭 실패 시 fallback 으로 사용되는 파일입니다.",
                    type, defaultVideo);
            }
        }
    }

    private static void CheckOpcUaEndpoint(IServiceProvider services, ILogger logger)
    {
        var opc = services.GetRequiredService<IOptions<OpcUaSettings>>().Value;
        if (string.IsNullOrWhiteSpace(opc.EndpointUrl))
        {
            logger.LogWarning("OPC UA EndpointUrl 이 비어 있습니다. 환경변수 OPCUA__ENDPOINTURL 또는 appsettings 확인.");
            return;
        }
        if (!opc.EndpointUrl.StartsWith("opc.tcp://", StringComparison.OrdinalIgnoreCase))
        {
            logger.LogWarning(
                "OPC UA EndpointUrl 이 'opc.tcp://' 로 시작하지 않습니다: {Url}",
                opc.EndpointUrl);
        }
        else
        {
            logger.LogInformation("OPC UA endpoint OK: {Url}", opc.EndpointUrl);
        }
    }

    private static void CheckUdpPorts(IServiceProvider services, ILogger logger)
    {
        var udp = services.GetRequiredService<IOptions<UdpSettings>>().Value;
        var ports = new[]
        {
            (Name: "Relay.ListenPort", Port: udp.Relay.ListenPort),
            (Name: "Relay.OutputPort", Port: udp.Relay.OutputPort),
            (Name: "Replay.OutputPort", Port: udp.Replay.OutputPort),
        };

        foreach (var (name, port) in ports)
        {
            if (IsUdpPortInUse(port))
            {
                logger.LogWarning(
                    "UDP 포트 {Port} ({Name}) 이 이미 다른 프로세스에 의해 사용 중일 수 있습니다. " +
                    "기동 후 실제 바인딩 시 실패하면 해당 프로세스를 종료하거나 포트를 변경하세요.",
                    port, name);
            }
            else
            {
                logger.LogInformation("UDP {Name}={Port} 바인딩 가능", name, port);
            }
        }
    }

    private static bool IsUdpPortInUse(int port)
    {
        try
        {
            // IPGlobalProperties 로 현재 OS 의 UDP 점유 목록을 확인 (Windows/Linux 모두 동작).
            var props = IPGlobalProperties.GetIPGlobalProperties();
            var udpListeners = props.GetActiveUdpListeners();
            return udpListeners.Any(ep => ep.Port == port);
        }
        catch
        {
            // 권한 부족 등으로 조회 실패 시 false 로 간주 (false negative 허용)
            return false;
        }
    }

    private static void CheckMulticastInterface(IServiceProvider services, ILogger logger)
    {
        var udp = services.GetRequiredService<IOptions<UdpSettings>>().Value;
        var iface = udp.Relay.MulticastInterface;
        if (string.IsNullOrWhiteSpace(iface))
        {
            logger.LogInformation("UDP MulticastInterface 가 비어 있어 OS 기본 라우팅을 사용합니다.");
            return;
        }
        if (!IPAddress.TryParse(iface, out var ifaceIp))
        {
            logger.LogWarning("UDP MulticastInterface 가 IP 형식이 아닙니다: {Value}", iface);
            return;
        }

        // 시스템의 모든 NIC IP 와 비교
        try
        {
            var nicIps = NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up)
                .SelectMany(n => n.GetIPProperties().UnicastAddresses)
                .Select(ua => ua.Address)
                .Where(a => a.AddressFamily == AddressFamily.InterNetwork)
                .ToList();

            if (nicIps.Any(ip => ip.Equals(ifaceIp)))
            {
                logger.LogInformation("UDP MulticastInterface={Iface} 가 활성 NIC 와 일치합니다.", iface);
            }
            else
            {
                logger.LogWarning(
                    "UDP MulticastInterface={Iface} 에 해당하는 활성 NIC 를 찾을 수 없습니다. " +
                    "Windows 다중 NIC 환경에서 잘못된 인터페이스로 송신되면 멀티캐스트가 보이지 않을 수 있습니다. " +
                    "활성 IP 목록: [{Active}]",
                    iface, string.Join(", ", nicIps.Select(ip => ip.ToString())));
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "NIC 정보를 조회하지 못했습니다. MulticastInterface 검증을 건너뜁니다.");
        }
    }
}
