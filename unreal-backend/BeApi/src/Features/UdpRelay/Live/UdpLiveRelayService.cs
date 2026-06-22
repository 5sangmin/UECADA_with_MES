// src/Features/UdpRelay/Live/UdpLiveRelayService.cs
//
// total-das 의 unicast UDP (0.0.0.0:50010) 를 그대로 239.100.0.1:50020 multicast 로 relay.
//
// 정책:
//   - payload 무변환. 들어온 그대로 송신.
//   - magic('TDAS') 가 맞지 않는 패킷은 로그 후 drop (외부 노이즈 차단).
//   - 송신 multicast NIC 는 appsettings 의 Udp:Relay:MulticastInterface 로 명시.
//   - 본 서비스는 HostedService 로 backend 라이프사이클과 동일하게 동작.

using System.Net;
using System.Net.Sockets;
using BeApi.Features.UdpRelay.Wire;
using BeApi.Shared.Settings;
using Microsoft.Extensions.Options;

namespace BeApi.Features.UdpRelay.Live;

public sealed class UdpLiveRelayService : BackgroundService
{
    private readonly UdpSettings _udp;
    private readonly ILogger<UdpLiveRelayService> _logger;
    private readonly IUdpLiveObserver _observer;

    public UdpLiveRelayService(
        IOptions<UdpSettings> udpOptions,
        IUdpLiveObserver observer,
        ILogger<UdpLiveRelayService> logger)
    {
        _udp = udpOptions.Value;
        _observer = observer;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var relay = _udp.Relay;
        var listenEndpoint = new IPEndPoint(IPAddress.Parse(_udp.BindAddress), relay.ListenPort);
        var multicastGroup = IPAddress.Parse(_udp.MulticastGroup);
        var multicastEndpoint = new IPEndPoint(multicastGroup, relay.OutputPort);

        // 1) 수신 소켓 (0.0.0.0:50010 unicast)
        using var receiver = new UdpClient(AddressFamily.InterNetwork);
        receiver.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        receiver.Client.Bind(listenEndpoint);

        // 2) 송신 소켓 (multicast)
        using var sender = new UdpClient(AddressFamily.InterNetwork);
        ConfigureMulticastSender(sender, relay);

        _logger.LogInformation(
            "UDP live relay 시작: {listen} → {mcast} (NIC={nic}, TTL={ttl})",
            listenEndpoint, multicastEndpoint, relay.MulticastInterface, relay.MulticastTtl);

        long packetCount = 0;
        long droppedCount = 0;

        while (!stoppingToken.IsCancellationRequested)
        {
            UdpReceiveResult result;
            try
            {
                result = await receiver.ReceiveAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "UDP relay 수신 실패. 재시도합니다.");
                await Task.Delay(100, stoppingToken).ConfigureAwait(false);
                continue;
            }

            var payload = result.Buffer;

            // magic 검증
            if (!UdpPacketCodec.TryReadHeader(payload, out _, out var equipmentCount, out var packetTs))
            {
                droppedCount++;
                if (droppedCount % 100 == 1)
                {
                    _logger.LogDebug("Magic 미일치로 drop. dropped={dropped}, from={from}", droppedCount, result.RemoteEndPoint);
                }
                continue;
            }

            // 송신
            try
            {
                await sender.SendAsync(payload, payload.Length, multicastEndpoint).ConfigureAwait(false);
                packetCount++;
                _observer.NotifyLivePacket(packetTs, equipmentCount);
                _observer.NotifyLivePayload(payload);  // PR4: latest cache 갱신용

                if (packetCount % 100 == 1)
                {
                    _logger.LogDebug(
                        "Relay 진행 중. forwarded={fwd}, dropped={drop}, lastPacketTs={ts}",
                        packetCount, droppedCount, packetTs);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "UDP relay 송신 실패. multicast={mcast}", multicastEndpoint);
            }
        }

        _logger.LogInformation(
            "UDP live relay 종료. 총 forwarded={fwd}, dropped={drop}", packetCount, droppedCount);
    }

    private static void ConfigureMulticastSender(UdpClient sender, UdpRelaySettings relay)
    {
        sender.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        sender.Client.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastTimeToLive, relay.MulticastTtl);

        if (!string.IsNullOrWhiteSpace(relay.MulticastInterface))
        {
            // Windows: multicast 송신 NIC 를 명시적으로 고정
            var nicIp = IPAddress.Parse(relay.MulticastInterface);
            sender.Client.SetSocketOption(
                SocketOptionLevel.IP,
                SocketOptionName.MulticastInterface,
                nicIp.GetAddressBytes());
        }
    }
}
