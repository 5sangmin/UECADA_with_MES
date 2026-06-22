// src/Features/UdpRelay/Live/IUdpLiveObserver.cs
//
// Relay 진행 상황을 관측하는 가벼운 hook.
// PR3(Connection Status), PR4(Latest API memory cache) 에서 동일 인터페이스로 확장될 예정.

namespace BeApi.Features.UdpRelay.Live;

public interface IUdpLiveObserver
{
    void NotifyLivePacket(long packetTsEpochMs, int equipmentCount);
}

/// <summary>
/// 기본 noop 구현. PR3/PR4 에서 실제 관측자로 교체된다.
/// </summary>
public sealed class NullUdpLiveObserver : IUdpLiveObserver
{
    public void NotifyLivePacket(long packetTsEpochMs, int equipmentCount) { }
}
