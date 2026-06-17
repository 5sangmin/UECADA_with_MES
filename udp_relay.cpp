#include <winsock2.h>
#include <ws2tcpip.h>
#pragma comment(lib, "ws2_32.lib")

#include <chrono>
#include <cstdint>
#include <ctime>
#include <iomanip>
#include <iostream>
#include <sstream>
#include <string>
#include <vector>

namespace {
constexpr int kListenPort = 50010;
constexpr const char* kDefaultMulticastGroup = "239.100.0.1";
constexpr int kDefaultMulticastPort = 50020;
constexpr int kDefaultTtl = 1;
constexpr int kBufferSize = 65535;

std::string format_time_ms(std::int64_t epoch_ms) {
    if (epoch_ms <= 0) return "--:--:--.---";

    std::time_t sec = static_cast<std::time_t>(epoch_ms / 1000);
    int ms = static_cast<int>(epoch_ms % 1000);
    if (ms < 0) ms += 1000;

    std::tm local_tm{};
    localtime_s(&local_tm, &sec);

    std::ostringstream oss;
    oss << std::put_time(&local_tm, "%H:%M:%S")
        << '.' << std::setw(3) << std::setfill('0') << ms;
    return oss.str();
}

std::int64_t now_ms() {
    return std::chrono::duration_cast<std::chrono::milliseconds>(
        std::chrono::system_clock::now().time_since_epoch()).count();
}
}  // namespace

int main(int argc, char* argv[]) {
    int listen_port = kListenPort;
    std::string multicast_group = kDefaultMulticastGroup;
    int multicast_port = kDefaultMulticastPort;
    int multicast_ttl = kDefaultTtl;

    if (argc >= 2) {
        listen_port = std::stoi(argv[1]);
    }
    if (argc >= 3) {
        multicast_group = argv[2];
    }
    if (argc >= 4) {
        multicast_port = std::stoi(argv[3]);
    }
    if (argc >= 5) {
        multicast_ttl = std::stoi(argv[4]);
    }

    WSADATA wsaData{};
    if (WSAStartup(MAKEWORD(2, 2), &wsaData) != 0) {
        std::cerr << "WSAStartup() failed\n";
        return 1;
    }

    SOCKET recv_sock = ::socket(AF_INET, SOCK_DGRAM, IPPROTO_UDP);
    if (recv_sock == INVALID_SOCKET) {
        std::cerr << "recv socket() failed: " << WSAGetLastError() << '\n';
        WSACleanup();
        return 1;
    }

    SOCKET send_sock = ::socket(AF_INET, SOCK_DGRAM, IPPROTO_UDP);
    if (send_sock == INVALID_SOCKET) {
        std::cerr << "send socket() failed: " << WSAGetLastError() << '\n';
        closesocket(recv_sock);
        WSACleanup();
        return 1;
    }

    BOOL reuse = TRUE;
    setsockopt(recv_sock, SOL_SOCKET, SO_REUSEADDR,
               reinterpret_cast<const char*>(&reuse), sizeof(reuse));

    sockaddr_in recv_addr{};
    recv_addr.sin_family = AF_INET;
    recv_addr.sin_addr.s_addr = htonl(INADDR_ANY);
    recv_addr.sin_port = htons(static_cast<u_short>(listen_port));

    if (::bind(recv_sock, reinterpret_cast<sockaddr*>(&recv_addr), sizeof(recv_addr)) == SOCKET_ERROR) {
        std::cerr << "bind() failed: " << WSAGetLastError() << '\n';
        closesocket(send_sock);
        closesocket(recv_sock);
        WSACleanup();
        return 1;
    }

    DWORD ttl = static_cast<DWORD>(multicast_ttl);
    if (setsockopt(send_sock, IPPROTO_IP, IP_MULTICAST_TTL,
                   reinterpret_cast<const char*>(&ttl), sizeof(ttl)) == SOCKET_ERROR) {
        std::cerr << "setsockopt(IP_MULTICAST_TTL) failed: " << WSAGetLastError() << '\n';
        closesocket(send_sock);
        closesocket(recv_sock);
        WSACleanup();
        return 1;
    }

    sockaddr_in mcast_addr{};
    mcast_addr.sin_family = AF_INET;
    mcast_addr.sin_port = htons(static_cast<u_short>(multicast_port));
    mcast_addr.sin_addr.s_addr = inet_addr(multicast_group.c_str());

    if (mcast_addr.sin_addr.s_addr == INADDR_NONE) {
        std::cerr << "invalid multicast group: " << multicast_group << '\n';
        closesocket(send_sock);
        closesocket(recv_sock);
        WSACleanup();
        return 1;
    }

    std::cout << "UDP relay started\n";
    std::cout << "  listen      : 0.0.0.0:" << listen_port << '\n';
    std::cout << "  multicast to: " << multicast_group << ':' << multicast_port << '\n';
    std::cout << "  ttl         : " << multicast_ttl << '\n';
    std::cout << "  logging     : relay time only\n";

    std::vector<char> buffer(kBufferSize);
    std::uint64_t relay_count = 0;

    while (true) {
        sockaddr_in peer{};
        int peer_len = sizeof(peer);
        int received = ::recvfrom(recv_sock,
                                  buffer.data(),
                                  static_cast<int>(buffer.size()),
                                  0,
                                  reinterpret_cast<sockaddr*>(&peer),
                                  &peer_len);
        if (received == SOCKET_ERROR) {
            std::cerr << "recvfrom() failed: " << WSAGetLastError() << '\n';
            continue;
        }

        int sent = ::sendto(send_sock,
                            buffer.data(),
                            received,
                            0,
                            reinterpret_cast<const sockaddr*>(&mcast_addr),
                            sizeof(mcast_addr));
        if (sent == SOCKET_ERROR) {
            std::cerr << "sendto() failed: " << WSAGetLastError() << '\n';
            continue;
        }

        ++relay_count;
        std::cout << '[' << relay_count << "] relayed at " << format_time_ms(now_ms())
                  << " size=" << received << " bytes\n";
    }

    closesocket(send_sock);
    closesocket(recv_sock);
    WSACleanup();
    return 0;
}