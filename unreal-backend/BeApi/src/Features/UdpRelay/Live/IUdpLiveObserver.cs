// src/Features/UdpRelay/Live/IUdpLiveObserver.cs
//
// Relay 진행 상황을 관측하는 가벼운 hook.
// PR3(Connection Status), PR4(Latest API memory cache) 에서 동일 인터페이스로 확장된다.

namespace BeApi.Features.UdpRelay.Live;

public interface IUdpLiveObserver
{
    /// <summary>Relay 성공 시 호출.</summary>
    void NotifyLivePacket(long packetTsEpochMs, int equipmentCount);

    /// <summary>가장 최근에 live 패킷을 수신한 시각. 한 번도 못 받았다면 null.</summary>
    DateTimeOffset? LastReceivedAt { get; }

    /// <summary>누적 live 패킷 수신 수.</summary>
    long TotalPackets { get; }

    /// <summary>마지막 패킷의 packet_ts (epoch ms). 미수신이면 null.</summary>
    long? LastPacketTsEpochMs { get; }

    /// <summary>마지막 패킷에 들어있던 장비 수.</summary>
    int LastEquipmentCount { get; }
}

/// <summary>기본 noop 구현 (테스트/특수 환경용). 일반 동작은 UdpLiveStatusObserver 사용.</summary>
public sealed class NullUdpLiveObserver : IUdpLiveObserver
{
    public void NotifyLivePacket(long packetTsEpochMs, int equipmentCount) { }
    public DateTimeOffset? LastReceivedAt => null;
    public long TotalPackets => 0;
    public long? LastPacketTsEpochMs => null;
    public int LastEquipmentCount => 0;
}

/// <summary>
/// 실제 관측자. PR3 ConnectionStatus 의 UDP 체커가 이 값을 읽어 상태를 판정한다.
/// thread-safe (Interlocked + Volatile).
/// </summary>
public sealed class UdpLiveStatusObserver : IUdpLiveObserver
{
    private long _totalPackets;
    private long _lastReceivedTicksUtc;   // 0 이면 미수신
    private long _lastPacketTsEpochMs;
    private int _lastEquipmentCount;

    public void NotifyLivePacket(long packetTsEpochMs, int equipmentCount)
    {
        Interlocked.Increment(ref _totalPackets);
        Interlocked.Exchange(ref _lastReceivedTicksUtc, DateTime.UtcNow.Ticks);
        Interlocked.Exchange(ref _lastPacketTsEpochMs, packetTsEpochMs);
        Volatile.Write(ref _lastEquipmentCount, equipmentCount);
    }

    public DateTimeOffset? LastReceivedAt
    {
        get
        {
            var ticks = Interlocked.Read(ref _lastReceivedTicksUtc);
            return ticks == 0 ? null : new DateTimeOffset(ticks, TimeSpan.Zero);
        }
    }

    public long TotalPackets => Interlocked.Read(ref _totalPackets);

    public long? LastPacketTsEpochMs
    {
        get
        {
            var v = Interlocked.Read(ref _lastPacketTsEpochMs);
            return v == 0 ? null : v;
        }
    }

    public int LastEquipmentCount => Volatile.Read(ref _lastEquipmentCount);
}
