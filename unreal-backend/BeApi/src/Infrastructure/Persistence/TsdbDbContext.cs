// src/Infrastructure/Persistence/TsdbDbContext.cs
//
// TimescaleDB(시계열) 읽기 전용 DbContext.
// public.equipment_snapshot hypertable에만 접근하며,
// 추후 Dapper 기반 무거운 시계열 쿼리는 별도 Query 레이어로 분리한다.
//
// 본 컨텍스트는 절대 SaveChanges / Migrate를 호출하지 않는 것을 원칙으로 한다.

using BeApi.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace BeApi.Infrastructure.Persistence;

public class TsdbDbContext : DbContext
{
    public TsdbDbContext(DbContextOptions<TsdbDbContext> options) : base(options)
    {
    }

    public DbSet<EquipmentSnapshotEntity> EquipmentSnapshots => Set<EquipmentSnapshotEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<EquipmentSnapshotEntity>(entity =>
        {
            entity.ToTable("equipment_snapshot", "public");

            // hypertable PK: (ts, line_id, equipment_id)
            entity.HasKey(e => new { e.Ts, e.LineId, e.EquipmentId });

            entity.Property(e => e.Ts).HasColumnName("ts");
            entity.Property(e => e.LineId).HasColumnName("line_id");
            entity.Property(e => e.EquipmentId).HasColumnName("equipment_id");
            entity.Property(e => e.EquipmentType).HasColumnName("equipment_type");
            entity.Property(e => e.Heartbeat).HasColumnName("heartbeat");
            entity.Property(e => e.QualityCode).HasColumnName("quality_code");
            entity.Property(e => e.Power).HasColumnName("power");
            entity.Property(e => e.StatusCode).HasColumnName("status_code");
            entity.Property(e => e.Progress).HasColumnName("progress");
            entity.Property(e => e.CycleTime).HasColumnName("cycle_time");
            entity.Property(e => e.PartCount).HasColumnName("part_count");

            entity.Property(e => e.Data1Setpoint).HasColumnName("data1_setpoint");
            entity.Property(e => e.Data1Sensor).HasColumnName("data1_sensor");
            entity.Property(e => e.Data2Setpoint).HasColumnName("data2_setpoint");
            entity.Property(e => e.Data2Sensor).HasColumnName("data2_sensor");
            entity.Property(e => e.Data3Setpoint).HasColumnName("data3_setpoint");
            entity.Property(e => e.Data3Sensor).HasColumnName("data3_sensor");

            entity.Property(e => e.ExternalData1Sensor).HasColumnName("externaldata1_sensor");
            entity.Property(e => e.ExternalData2Sensor).HasColumnName("externaldata2_sensor");
            entity.Property(e => e.ExternalData3Sensor).HasColumnName("externaldata3_sensor");
            entity.Property(e => e.ExternalData4Sensor).HasColumnName("externaldata4_sensor");

            entity.Property(e => e.CmdId).HasColumnName("cmd_id");
            entity.Property(e => e.CmdAccepted).HasColumnName("cmd_accepted");
            entity.Property(e => e.CmdStatus).HasColumnName("cmd_status");

            entity.Property(e => e.CreatedAt).HasColumnName("created_at");
        });
    }
}
