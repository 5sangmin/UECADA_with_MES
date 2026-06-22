// src/Features/UdpRelay/Live/IUdpLiveObserver.cs
//
// Relay 진행 상황을 관측하는 가벼운 hook.
// PR3 (Connection Status), PR4 (Latest API memory cache) 가 이 인터페이스를 통해 신호를 받는다.
//
// 설계:
//   - NotifyLivePacket(ts, count): 가벼운 메타데이터만. status checker 용.
//   - NotifyLivePayload(span):     전체 payload (header + records). latest cache 용.
// observer 구현체가 둘 중 무엇을 활용할지는 자유.

namespace BeApi.Features.UdpRelay.Live;

public interface IUdpLiveObserver
{
    /// <summary>Relay 성공 시 호출 (가벼운 메타데이터).</summary>
    void NotifyLivePacket(long packetTsEpochMs, int equipmentCount);

    /// <summary>
    /// Relay 성공 시 호출 (전체 payload).
    /// 호출자가 payload buffer 의 lifetime 을 보장하지 않으므로,
    /// 필요하면 구현체에서 즉시 복사하거나 그 자리에서 decode 해야 한다.
    /// </summary>
    void NotifyLivePayload(ReadOnlySpan<byte> payload);

    DateTimeOffset? LastReceivedAt { get; }
    long TotalPackets { get; }
    long? LastPacketTsEpochMs { get; }
    int LastEquipmentCount { get; }
}

/// <summary>기본 noop 구현 (테스트용). 일반 동작은 UdpLiveStatusObserver 사용.</summary>
public sealed class NullUdpLiveObserver : IUdpLiveObserver
{
    public void NotifyLivePacket(long packetTsEpochMs, int equipmentCount) { }
    public void NotifyLivePayload(ReadOnlySpan<byte> payload) { }
    public DateTimeOffset? LastReceivedAt => null;
    public long TotalPackets => 0;
    public long? LastPacketTsEpochMs => null;
    public int LastEquipmentCount => 0;
}

/// <summary>
/// 상태 관측자. PR3 ConnectionStatus 의 UDP 체커가 이 값을 읽어 상태를 판정한다.
/// thread-safe (Interlocked + Volatile).
/// payload hook 은 무시한다 (status 판정에 불필요).
/// </summary>
public sealed class UdpLiveStatusObserver : IUdpLiveObserver
{
    private long _totalPackets;
    private long _lastReceivedTicksUtc;
    private long _lastPacketTsEpochMs;
    private int _lastEquipmentCount;

    public void NotifyLivePacket(long packetTsEpochMs, int equipmentCount)
    {
        Interlocked.Increment(ref _totalPackets);
        Interlocked.Exchange(ref _lastReceivedTicksUtc, DateTime.UtcNow.Ticks);
        Interlocked.Exchange(ref _lastPacketTsEpochMs, packetTsEpochMs);
        Volatile.Write(ref _lastEquipmentCount, equipmentCount);
    }

    public void NotifyLivePayload(ReadOnlySpan<byte> payload) { /* not used for status */ }

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
        get { var v = Interlocked.Read(ref _lastPacketTsEpochMs); return v == 0 ? null : v; }
    }
    public int LastEquipmentCount => Volatile.Read(ref _lastEquipmentCount);
}

/// <summary>
/// 여러 observer 를 동시에 호출하는 fan-out wrapper.
/// DI 에 IUdpLiveObserver 로 단일 인스턴스 등록 후, 내부에서 등록된 모든 observer 에 전파.
/// </summary>
public sealed class CompositeUdpLiveObserver : IUdpLiveObserver
{
    private readonly IReadOnlyList<IUdpLiveObserver> _inner;
    private readonly UdpLiveStatusObserver _status;

    public CompositeUdpLiveObserver(IEnumerable<IUdpLiveObserver> inner, UdpLiveStatusObserver status)
    {
        _inner = inner.ToArray();
        _status = status;
    }

    public void NotifyLivePacket(long packetTsEpochMs, int equipmentCount)
    {
        foreach (var o in _inner) o.NotifyLivePacket(packetTsEpochMs, equipmentCount);
    }

    public void NotifyLivePayload(ReadOnlySpan<byte> payload)
    {
        foreach (var o in _inner) o.NotifyLivePayload(payload);
    }

    // status 신호는 단일 진실 (UdpLiveStatusObserver) 에서 가져옴
    public DateTimeOffset? LastReceivedAt => _status.LastReceivedAt;
    public long TotalPackets => _status.TotalPackets;
    public long? LastPacketTsEpochMs => _status.LastPacketTsEpochMs;
    public int LastEquipmentCount => _status.LastEquipmentCount;
}
