#include <winsock2.h>
#include <ws2tcpip.h>
#pragma comment(lib, "ws2_32.lib")

#include <chrono>
#include <cstdint>
#include <cstring>
#include <ctime>
#include <iomanip>
#include <iostream>
#include <sstream>
#include <string>
#include <vector>

namespace {
constexpr const char* kDefaultGroup = "239.100.0.1";
constexpr int kDefaultPort = 50020;
constexpr std::size_t kHeaderSize = 16;
constexpr std::size_t kRecordSize = 86;
constexpr std::size_t kEquipmentCount = 27;
constexpr std::size_t kPacketSize = kHeaderSize + (kRecordSize * kEquipmentCount);

struct PacketHeader {
    char magic[4];
    std::uint16_t version;
    std::uint16_t equipment_count;
    std::int64_t packet_ts_epoch_ms;
};

struct EquipmentRecord {
    std::int16_t line_id;
    std::int16_t equipment_id;
    std::int64_t ts_epoch_ms;
    std::int32_t heartbeat;
    std::int16_t quality_code;
    std::uint8_t power;
    std::uint8_t reserved;
    std::int16_t status_code;
    float progress;
    float cycle_time;
    std::int32_t part_count;
    float data1_setpoint;
    float data1_sensor;
    float data2_setpoint;
    float data2_sensor;
    float data3_setpoint;
    float data3_sensor;
    float externaldata1_sensor;
    float externaldata2_sensor;
    float externaldata3_sensor;
    float externaldata4_sensor;
    std::int32_t cmd_id;
    std::int32_t cmd_accepted;
    std::int32_t cmd_status;
};

std::uint16_t read_u16_le(const std::uint8_t* p) {
    return static_cast<std::uint16_t>(p[0]) |
           (static_cast<std::uint16_t>(p[1]) << 8);
}

std::int16_t read_i16_le(const std::uint8_t* p) {
    return static_cast<std::int16_t>(read_u16_le(p));
}

std::uint32_t read_u32_le(const std::uint8_t* p) {
    return static_cast<std::uint32_t>(p[0]) |
           (static_cast<std::uint32_t>(p[1]) << 8) |
           (static_cast<std::uint32_t>(p[2]) << 16) |
           (static_cast<std::uint32_t>(p[3]) << 24);
}

std::int32_t read_i32_le(const std::uint8_t* p) {
    return static_cast<std::int32_t>(read_u32_le(p));
}

std::uint64_t read_u64_le(const std::uint8_t* p) {
    return static_cast<std::uint64_t>(p[0]) |
           (static_cast<std::uint64_t>(p[1]) << 8) |
           (static_cast<std::uint64_t>(p[2]) << 16) |
           (static_cast<std::uint64_t>(p[3]) << 24) |
           (static_cast<std::uint64_t>(p[4]) << 32) |
           (static_cast<std::uint64_t>(p[5]) << 40) |
           (static_cast<std::uint64_t>(p[6]) << 48) |
           (static_cast<std::uint64_t>(p[7]) << 56);
}

std::int64_t read_i64_le(const std::uint8_t* p) {
    return static_cast<std::int64_t>(read_u64_le(p));
}

float read_f32_le(const std::uint8_t* p) {
    std::uint32_t raw = read_u32_le(p);
    float value;
    std::memcpy(&value, &raw, sizeof(float));
    return value;
}

PacketHeader parse_header(const std::uint8_t* p) {
    PacketHeader h{};
    std::memcpy(h.magic, p, 4);
    h.version = read_u16_le(p + 4);
    h.equipment_count = read_u16_le(p + 6);
    h.packet_ts_epoch_ms = read_i64_le(p + 8);
    return h;
}

EquipmentRecord parse_record(const std::uint8_t* p) {
    EquipmentRecord r{};
    r.line_id = read_i16_le(p + 0);
    r.equipment_id = read_i16_le(p + 2);
    r.ts_epoch_ms = read_i64_le(p + 4);
    r.heartbeat = read_i32_le(p + 12);
    r.quality_code = read_i16_le(p + 16);
    r.power = p[18];
    r.reserved = p[19];
    r.status_code = read_i16_le(p + 20);
    r.progress = read_f32_le(p + 22);
    r.cycle_time = read_f32_le(p + 26);
    r.part_count = read_i32_le(p + 30);
    r.data1_setpoint = read_f32_le(p + 34);
    r.data1_sensor = read_f32_le(p + 38);
    r.data2_setpoint = read_f32_le(p + 42);
    r.data2_sensor = read_f32_le(p + 46);
    r.data3_setpoint = read_f32_le(p + 50);
    r.data3_sensor = read_f32_le(p + 54);
    r.externaldata1_sensor = read_f32_le(p + 58);
    r.externaldata2_sensor = read_f32_le(p + 62);
    r.externaldata3_sensor = read_f32_le(p + 66);
    r.externaldata4_sensor = read_f32_le(p + 70);
    r.cmd_id = read_i32_le(p + 74);
    r.cmd_accepted = read_i32_le(p + 78);
    r.cmd_status = read_i32_le(p + 82);
    return r;
}

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

std::string equipment_name(std::int16_t equipment_id) {
    if (equipment_id == 101) return "CAST-01";
    if (equipment_id == 201) return "CNC-01";
    if (equipment_id == 202) return "CNC-02";
    if (equipment_id == 203) return "CNC-03";
    if (equipment_id == 301) return "WASH-01";
    if (equipment_id == 401) return "ASSY-01";
    if (equipment_id == 402) return "ASSY-02";
    if (equipment_id == 501) return "TEST-01";
    if (equipment_id == 502) return "TEST-02";
    return "UNKNOWN";
}

void clear_terminal() {
    std::cout << "\x1B[2J\x1B[H";
}

void print_table_row(const EquipmentRecord& r) {
    std::ostringstream line_name;
    line_name << "LINE-" << std::setw(2) << std::setfill('0') << r.line_id;

    std::cout
        << std::left
        << std::setfill(' ')
        << std::setw(8) << line_name.str()
        << std::setw(10) << equipment_name(r.equipment_id)
        << std::setw(13) << format_time_ms(r.ts_epoch_ms)
        << std::right
        << std::setw(10) << r.heartbeat
        << std::setw(8) << static_cast<int>(r.power)
        << std::setw(8) << r.status_code
        << std::setw(10) << std::fixed << std::setprecision(1) << r.progress
        << std::setw(10) << std::fixed << std::setprecision(1) << r.cycle_time
        << std::setw(10) << r.part_count
        << std::setw(10) << r.cmd_id
        << std::setw(8) << r.cmd_accepted
        << std::setw(8) << r.cmd_status
        << '\n';
}

void print_summary(const PacketHeader& h,
                   const std::vector<EquipmentRecord>& rows,
                   const sockaddr_in& peer,
                   const std::string& group,
                   int port) {
    char ip[INET_ADDRSTRLEN] = {0};
    inet_ntop(AF_INET, const_cast<IN_ADDR*>(&peer.sin_addr), ip, sizeof(ip));

    clear_terminal();

    std::cout << "TOTALDAS UDP MULTICAST MONITOR\n";
    std::cout << "================================================================================================================\n";
    std::cout << "joined_group    : " << group << ':' << port << '\n';
    std::cout << "from            : " << ip << ':' << ntohs(peer.sin_port) << '\n';
    std::cout << "magic           : " << std::string(h.magic, 4) << '\n';
    std::cout << "version         : " << h.version << '\n';
    std::cout << "equipment_count : " << h.equipment_count << '\n';
    std::cout << "packet_ts       : " << format_time_ms(h.packet_ts_epoch_ms) << "\n\n";

    std::cout
        << std::left
        << std::setw(8) << "LINE"
        << std::setw(10) << "EQUIP"
        << std::setw(13) << "TS"
        << std::right
        << std::setw(10) << "HEART"
        << std::setw(8) << "PWR"
        << std::setw(8) << "STAT"
        << std::setw(10) << "PROG"
        << std::setw(10) << "CYCLE"
        << std::setw(10) << "PART"
        << std::setw(10) << "CMD_ID"
        << std::setw(8) << "ACC"
        << std::setw(8) << "CSTAT"
        << '\n';

    std::cout << "----------------------------------------------------------------------------------------------------------------\n";
    for (const auto& r : rows) {
        print_table_row(r);
    }

    auto now_ms = std::chrono::duration_cast<std::chrono::milliseconds>(
        std::chrono::system_clock::now().time_since_epoch()).count();
    std::cout << "\nUpdated at local time: " << format_time_ms(now_ms) << '\n';
    std::cout.flush();
}
}  // namespace

int main(int argc, char* argv[]) {
    std::string group = kDefaultGroup;
    int port = kDefaultPort;

    if (argc >= 2) {
        group = argv[1];
    }
    if (argc >= 3) {
        port = std::stoi(argv[2]);
    }

    WSADATA wsaData{};
    if (WSAStartup(MAKEWORD(2, 2), &wsaData) != 0) {
        std::cerr << "WSAStartup() failed\n";
        return 1;
    }

    SOCKET sock = ::socket(AF_INET, SOCK_DGRAM, IPPROTO_UDP);
    if (sock == INVALID_SOCKET) {
        std::cerr << "socket() failed: " << WSAGetLastError() << '\n';
        WSACleanup();
        return 1;
    }

    BOOL reuse = TRUE;
    setsockopt(sock, SOL_SOCKET, SO_REUSEADDR,
               reinterpret_cast<const char*>(&reuse), sizeof(reuse));

    sockaddr_in addr{};
    addr.sin_family = AF_INET;
    addr.sin_addr.s_addr = htonl(INADDR_ANY);
    addr.sin_port = htons(static_cast<u_short>(port));

    if (::bind(sock, reinterpret_cast<sockaddr*>(&addr), sizeof(addr)) == SOCKET_ERROR) {
        std::cerr << "bind() failed: " << WSAGetLastError() << '\n';
        closesocket(sock);
        WSACleanup();
        return 1;
    }

    ip_mreq mreq{};
    mreq.imr_multiaddr.s_addr = inet_addr(group.c_str());
    mreq.imr_interface.s_addr = htonl(INADDR_ANY);

    if (mreq.imr_multiaddr.s_addr == INADDR_NONE) {
        std::cerr << "invalid multicast group: " << group << '\n';
        closesocket(sock);
        WSACleanup();
        return 1;
    }

    if (setsockopt(sock, IPPROTO_IP, IP_ADD_MEMBERSHIP,
                   reinterpret_cast<const char*>(&mreq), sizeof(mreq)) == SOCKET_ERROR) {
        std::cerr << "setsockopt(IP_ADD_MEMBERSHIP) failed: " << WSAGetLastError() << '\n';
        closesocket(sock);
        WSACleanup();
        return 1;
    }

    std::cout << "Listening multicast UDP group=" << group << ':' << port
              << " (expected packet size=" << kPacketSize << " bytes)" << std::endl;

    std::vector<std::uint8_t> buffer(4096);

    while (true) {
        sockaddr_in peer{};
        int peer_len = sizeof(peer);
        int received = ::recvfrom(sock,
                                  reinterpret_cast<char*>(buffer.data()),
                                  static_cast<int>(buffer.size()),
                                  0,
                                  reinterpret_cast<sockaddr*>(&peer),
                                  &peer_len);
        if (received == SOCKET_ERROR) {
            std::cerr << "recvfrom() failed: " << WSAGetLastError() << '\n';
            continue;
        }

        if (static_cast<std::size_t>(received) != kPacketSize) {
            std::cerr << "skip: unexpected packet size=" << received
                      << ", expected=" << kPacketSize << '\n';
            continue;
        }

        PacketHeader header = parse_header(buffer.data());
        if (std::string(header.magic, 4) != "TDAS") {
            std::cerr << "skip: invalid magic\n";
            continue;
        }
        if (header.version != 1) {
            std::cerr << "skip: unsupported version=" << header.version << '\n';
            continue;
        }
        if (header.equipment_count != kEquipmentCount) {
            std::cerr << "skip: invalid equipment_count=" << header.equipment_count << '\n';
            continue;
        }

        std::vector<EquipmentRecord> rows;
        rows.reserve(kEquipmentCount);
        for (std::size_t i = 0; i < kEquipmentCount; ++i) {
            const std::size_t offset = kHeaderSize + (i * kRecordSize);
            rows.push_back(parse_record(buffer.data() + offset));
        }

        print_summary(header, rows, peer, group, port);
    }

    closesocket(sock);
    WSACleanup();
    return 0;
}