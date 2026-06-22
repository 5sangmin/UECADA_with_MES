// src/Shared/Settings/UdpSettings.cs
namespace BeApi.Shared.Settings;

/// <summary>
/// UDP 관련 통합 설정.
/// - total-das 가 50010 으로 unicast 송신 → BeApi 가 relay 하여 239.100.0.1:50020 으로 multicast
/// - replay 는 TSDB 를 읽어 239.100.0.1:50021 으로 multicast
/// - 두 채널 모두 동일한 NIC(MulticastInterface) 로 송신
/// </summary>
public class UdpSettings
{
    public const string SectionName = "Udp";

    /// <summary>멀티캐스트 그룹 IP (라이브/리플레이 공통).</summary>
    public string MulticastGroup { get; set; } = "239.100.0.1";

    /// <summary>(BeApi 가 수신만 할 때 사용하던 레거시 필드. 현재는 사용되지 않음.)</summary>
    public int ListenPort { get; set; } = 50020;

    /// <summary>BeApi 가 binding 하는 IP. 보통 0.0.0.0.</summary>
    public string BindAddress { get; set; } = "0.0.0.0";

    /// <summary>UDP 수신/관측 stale 판정 임계값(초).</summary>
    public int StaleThresholdSeconds { get; set; } = 10;

    /// <summary>Live relay 설정 (50010 → 50020).</summary>
    public UdpRelaySettings Relay { get; set; } = new();

    /// <summary>Replay 설정 (TSDB → 50021).</summary>
    public UdpReplaySettings Replay { get; set; } = new();
}

public class UdpRelaySettings
{
    /// <summary>BeApi 가 total-das 의 unicast 를 수신할 포트.</summary>
    public int ListenPort { get; set; } = 50010;

    /// <summary>Relay 출력 multicast 포트.</summary>
    public int OutputPort { get; set; } = 50020;

    /// <summary>
    /// Multicast 송신 NIC 의 IP. Windows 에 NIC 가 여러 개일 때 명시적으로 고정.
    /// 빈 문자열이면 OS 기본 라우팅.
    /// </summary>
    public string MulticastInterface { get; set; } = "192.168.5.10";

    /// <summary>IP TTL. LAN 한정 권장값 1.</summary>
    public int MulticastTtl { get; set; } = 1;
}

public class UdpReplaySettings
{
    /// <summary>Replay 출력 multicast 포트.</summary>
    public int OutputPort { get; set; } = 50021;

    /// <summary>기본 speed (1.0 = 원본 속도).</summary>
    public double DefaultSpeed { get; set; } = 1.0;

    /// <summary>speed 허용 범위 - 하한.</summary>
    public double MinSpeed { get; set; } = 0.1;

    /// <summary>speed 허용 범위 - 상한.</summary>
    public double MaxSpeed { get; set; } = 100.0;

    /// <summary>같은 ts 그룹을 한 패킷으로 묶을 때 TSDB 페이지 사이즈(메모리 보호).</summary>
    public int FetchBatchSize { get; set; } = 5000;
}
