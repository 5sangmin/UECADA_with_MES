// src/Features/UdpRelay/Wire/EquipmentRecord.cs
//
// 86 B UDP record 와 1:1 매핑되는 in-memory 구조체.
//
// 주의:
//   - 이 구조체는 "wire" 표현이다. LineId/EquipmentId 는 정수형 (LUT 적용 후 값).
//   - TSDB 의 line_id('LINE-01') / equipment_id('LINE-01_CNC-02') 는 text 이므로
//     replay 시에는 EquipmentLut 으로 변환해서 채워 넣는다.

namespace BeApi.Features.UdpRelay.Wire;

public readonly record struct EquipmentRecord(
    short LineId,
    short EquipmentId,
    long TsEpochMs,
    int Heartbeat,
    short QualityCode,
    byte Power,
    byte Reserved,
    short StatusCode,
    float Progress,
    float CycleTime,
    int PartCount,
    float Data1Setpoint,
    float Data1Sensor,
    float Data2Setpoint,
    float Data2Sensor,
    float Data3Setpoint,
    float Data3Sensor,
    float ExternalData1Sensor,
    float ExternalData2Sensor,
    float ExternalData3Sensor,
    float ExternalData4Sensor,
    int CmdId,
    int CmdAccepted,
    int CmdStatus);
