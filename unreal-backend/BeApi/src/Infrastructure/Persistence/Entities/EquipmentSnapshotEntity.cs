// src/Infrastructure/Persistence/Entities/EquipmentSnapshotEntity.cs
//
// TimescaleDB의 public.equipment_snapshot hypertable과 1:1 매핑.
// 컬럼은 database/timescaledb/init/01_init.sql 기준이며, 본 backend는
// 이 테이블에 절대 마이그레이션(create/alter)을 수행하지 않는다.
// EF Core는 매핑(read/insert) 용도로만 사용한다.
//
// 주의: line_id, equipment_id는 wire format의 정수가 아니라
// TSDB 스키마상 text이다 (예: 'LINE-01', 'LINE-01_CNC-02').
// 정수 LUT 매핑은 BeApi.Features.Equipment.EquipmentLut에서 별도 처리한다.

namespace BeApi.Infrastructure.Persistence.Entities;

public class EquipmentSnapshotEntity
{
    public DateTimeOffset Ts { get; set; }
    public string LineId { get; set; } = string.Empty;
    public string EquipmentId { get; set; } = string.Empty;

    public string? EquipmentType { get; set; }
    public long? Heartbeat { get; set; }
    public short? QualityCode { get; set; }
    public bool? Power { get; set; }
    public short? StatusCode { get; set; }
    public double? Progress { get; set; }
    public double? CycleTime { get; set; }
    public long? PartCount { get; set; }

    public double? Data1Setpoint { get; set; }
    public double? Data1Sensor { get; set; }
    public double? Data2Setpoint { get; set; }
    public double? Data2Sensor { get; set; }
    public double? Data3Setpoint { get; set; }
    public double? Data3Sensor { get; set; }

    public double? ExternalData1Sensor { get; set; }
    public double? ExternalData2Sensor { get; set; }
    public double? ExternalData3Sensor { get; set; }
    public double? ExternalData4Sensor { get; set; }

    public long? CmdId { get; set; }
    public short? CmdAccepted { get; set; }
    public short? CmdStatus { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
