// src/Features/Latest/LatestModels.cs
//
// PR4 (Step 7~8) Latest Data API 모델.
// EquipmentRecord 의 모든 27 컬럼 + 응답 메타(source, ageSeconds) 포함.

namespace BeApi.Features.Latest;

/// <summary>응답 한 건. EquipmentRecord 전체 컬럼을 평탄화한 DTO.</summary>
public sealed record EquipmentLatestDto(
    short LineId,
    short EquipmentId,
    long TsEpochMs,
    DateTimeOffset Ts,
    int Heartbeat,
    short QualityCode,
    bool Power,
    short StatusCode,
    float Progress,
    float CycleTime,
    long PartCount,
    float Data1Setpoint, float Data1Sensor,
    float Data2Setpoint, float Data2Sensor,
    float Data3Setpoint, float Data3Sensor,
    float ExternalData1Sensor, float ExternalData2Sensor,
    float ExternalData3Sensor, float ExternalData4Sensor,
    int CmdId, int CmdAccepted, int CmdStatus,
    string Source,         // "cache" | "tsdb"
    double AgeSeconds);    // 응답 시각 기준 ts 와의 차이

/// <summary>여러 장비의 latest 를 묶어 응답.</summary>
public sealed record EquipmentLatestEnvelopeDto(
    int Count,
    string OverallSource,                  // "cache" | "tsdb" | "mixed" | "empty"
    DateTimeOffset GeneratedAt,
    IReadOnlyList<EquipmentLatestDto> Items);
