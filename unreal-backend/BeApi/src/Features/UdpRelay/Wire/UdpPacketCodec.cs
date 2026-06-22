// src/Features/UdpRelay/Wire/UdpPacketCodec.cs
//
// total-das ↔ Unreal 간의 UDP wire format encoder/decoder.
// 모든 정수/실수는 Little Endian.
//
// Header (16 B):
//   magic 'TDAS' (4) + version u16 (2) + equipment_count u16 (2) + packet_ts_epoch_ms i64 (8)
//
// Record (86 B) × equipment_count:
//   line_id           i16 @ 0
//   equipment_id      i16 @ 2
//   ts_epoch_ms       i64 @ 4
//   heartbeat         i32 @ 12
//   quality_code      i16 @ 16
//   power             u8  @ 18
//   reserved          u8  @ 19
//   status_code       i16 @ 20
//   progress          f32 @ 22
//   cycle_time        f32 @ 26
//   part_count        i32 @ 30
//   data1_setpoint    f32 @ 34
//   data1_sensor      f32 @ 38
//   data2_setpoint    f32 @ 42
//   data2_sensor      f32 @ 46
//   data3_setpoint    f32 @ 50
//   data3_sensor      f32 @ 54
//   externaldata1     f32 @ 58
//   externaldata2     f32 @ 62
//   externaldata3     f32 @ 66
//   externaldata4     f32 @ 70
//   cmd_id            i32 @ 74
//   cmd_accepted      i32 @ 78
//   cmd_status        i32 @ 82
//
// 27 장비 기준 16 + 86 × 27 = 2338 B.

using System.Buffers.Binary;

namespace BeApi.Features.UdpRelay.Wire;

public static class UdpPacketCodec
{
    public const int HeaderSize = 16;
    public const int RecordSize = 86;

    /// <summary>Magic 'TDAS' (4 bytes). Wire 상 이 패키지가 total-das wire 포먷임을 표시.</summary>
    public static readonly byte[] MagicBytes = new byte[] { (byte)'T', (byte)'D', (byte)'A', (byte)'S' };

    public const ushort CurrentVersion = 1;

    public static int CalculateLength(int equipmentCount) =>
        HeaderSize + RecordSize * equipmentCount;

    /// <summary>
    /// 주어진 record 배열을 wire format buffer 로 인코딩한다.
    /// </summary>
    public static byte[] Encode(long packetTsEpochMs, IReadOnlyList<EquipmentRecord> records)
    {
        var buffer = new byte[CalculateLength(records.Count)];
        WriteHeader(buffer, packetTsEpochMs, (ushort)records.Count);
        for (int i = 0; i < records.Count; i++)
        {
            WriteRecord(buffer.AsSpan(HeaderSize + i * RecordSize, RecordSize), records[i]);
        }
        return buffer;
    }

    public static void WriteHeader(Span<byte> dest, long packetTsEpochMs, ushort equipmentCount)
    {
        if (dest.Length < HeaderSize) throw new ArgumentException("destination buffer too small");
        MagicBytes.CopyTo(dest);
        BinaryPrimitives.WriteUInt16LittleEndian(dest.Slice(4, 2), CurrentVersion);
        BinaryPrimitives.WriteUInt16LittleEndian(dest.Slice(6, 2), equipmentCount);
        BinaryPrimitives.WriteInt64LittleEndian(dest.Slice(8, 8), packetTsEpochMs);
    }

    public static void WriteRecord(Span<byte> dest, in EquipmentRecord r)
    {
        if (dest.Length < RecordSize) throw new ArgumentException("destination buffer too small");

        BinaryPrimitives.WriteInt16LittleEndian(dest.Slice(0, 2), r.LineId);
        BinaryPrimitives.WriteInt16LittleEndian(dest.Slice(2, 2), r.EquipmentId);
        BinaryPrimitives.WriteInt64LittleEndian(dest.Slice(4, 8), r.TsEpochMs);
        BinaryPrimitives.WriteInt32LittleEndian(dest.Slice(12, 4), r.Heartbeat);
        BinaryPrimitives.WriteInt16LittleEndian(dest.Slice(16, 2), r.QualityCode);
        dest[18] = r.Power;
        dest[19] = r.Reserved;
        BinaryPrimitives.WriteInt16LittleEndian(dest.Slice(20, 2), r.StatusCode);
        BinaryPrimitives.WriteSingleLittleEndian(dest.Slice(22, 4), r.Progress);
        BinaryPrimitives.WriteSingleLittleEndian(dest.Slice(26, 4), r.CycleTime);
        BinaryPrimitives.WriteInt32LittleEndian(dest.Slice(30, 4), r.PartCount);
        BinaryPrimitives.WriteSingleLittleEndian(dest.Slice(34, 4), r.Data1Setpoint);
        BinaryPrimitives.WriteSingleLittleEndian(dest.Slice(38, 4), r.Data1Sensor);
        BinaryPrimitives.WriteSingleLittleEndian(dest.Slice(42, 4), r.Data2Setpoint);
        BinaryPrimitives.WriteSingleLittleEndian(dest.Slice(46, 4), r.Data2Sensor);
        BinaryPrimitives.WriteSingleLittleEndian(dest.Slice(50, 4), r.Data3Setpoint);
        BinaryPrimitives.WriteSingleLittleEndian(dest.Slice(54, 4), r.Data3Sensor);
        BinaryPrimitives.WriteSingleLittleEndian(dest.Slice(58, 4), r.ExternalData1Sensor);
        BinaryPrimitives.WriteSingleLittleEndian(dest.Slice(62, 4), r.ExternalData2Sensor);
        BinaryPrimitives.WriteSingleLittleEndian(dest.Slice(66, 4), r.ExternalData3Sensor);
        BinaryPrimitives.WriteSingleLittleEndian(dest.Slice(70, 4), r.ExternalData4Sensor);
        BinaryPrimitives.WriteInt32LittleEndian(dest.Slice(74, 4), r.CmdId);
        BinaryPrimitives.WriteInt32LittleEndian(dest.Slice(78, 4), r.CmdAccepted);
        BinaryPrimitives.WriteInt32LittleEndian(dest.Slice(82, 4), r.CmdStatus);
    }

    /// <summary>
    /// 디버그/관측용: 헤더만 빠르게 파싱.
    /// </summary>
    public static bool TryReadHeader(
        ReadOnlySpan<byte> source,
        out ushort version,
        out ushort equipmentCount,
        out long packetTsEpochMs)
    {
        version = 0;
        equipmentCount = 0;
        packetTsEpochMs = 0;
        if (source.Length < HeaderSize) return false;
        if (!source.Slice(0, 4).SequenceEqual(MagicBytes)) return false;

        version = BinaryPrimitives.ReadUInt16LittleEndian(source.Slice(4, 2));
        equipmentCount = BinaryPrimitives.ReadUInt16LittleEndian(source.Slice(6, 2));
        packetTsEpochMs = BinaryPrimitives.ReadInt64LittleEndian(source.Slice(8, 8));
        return true;
    }
}
