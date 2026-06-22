using BeApi.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace BeApi.Infrastructure.Persistence;

public class CommandDbContext : DbContext
{
    public CommandDbContext(DbContextOptions<CommandDbContext> options) : base(options)
    {
    }

    public DbSet<CommandRequestEntity> CommandRequests => Set<CommandRequestEntity>();
    public DbSet<CommandResponseEventEntity> CommandResponseEvents => Set<CommandResponseEventEntity>();
    public DbSet<CommandLatestResponseEntity> CommandLatestResponses => Set<CommandLatestResponseEntity>();
    public DbSet<CommandHistoryViewEntity> CommandHistories => Set<CommandHistoryViewEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<CommandRequestEntity>(entity =>
        {
            entity.ToTable("command_request", "public");

            entity.HasKey(e => e.Id);

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.CommandId).HasColumnName("command_id").IsRequired();
            entity.Property(e => e.SourceType).HasColumnName("source_type").IsRequired();
            entity.Property(e => e.LineId).HasColumnName("line_id").IsRequired();
            entity.Property(e => e.EquipmentId).HasColumnName("equipment_id").IsRequired();
            entity.Property(e => e.CommandType).HasColumnName("command_type").IsRequired();
            entity.Property(e => e.CommandValue).HasColumnName("command_value").HasColumnType("jsonb");
            entity.Property(e => e.RequestJson).HasColumnName("request_json").HasColumnType("jsonb").IsRequired();
            entity.Property(e => e.Status).HasColumnName("status").IsRequired();
            entity.Property(e => e.Priority).HasColumnName("priority").HasDefaultValue(100);
            entity.Property(e => e.RetryCount).HasColumnName("retry_count").HasDefaultValue(0);
            entity.Property(e => e.MaxRetry).HasColumnName("max_retry").HasDefaultValue(0);
            entity.Property(e => e.CreatedBy).HasColumnName("created_by");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at");
            entity.Property(e => e.StartedAt).HasColumnName("started_at");
            entity.Property(e => e.FinishedAt).HasColumnName("finished_at");
            entity.Property(e => e.PickedAt).HasColumnName("picked_at");
            entity.Property(e => e.WorkerId).HasColumnName("worker_id");
            entity.Property(e => e.LastError).HasColumnName("last_error");
            entity.Property(e => e.IdempotencyKey).HasColumnName("idempotency_key");

            entity.HasIndex(e => e.CommandId).IsUnique();
            entity.HasIndex(e => e.IdempotencyKey).HasDatabaseName("idx_command_request_idempotency_key");
            entity.HasIndex(e => new { e.Status, e.Priority, e.CreatedAt })
                  .HasDatabaseName("idx_command_request_status_priority_created");
            entity.HasIndex(e => new { e.LineId, e.EquipmentId })
                  .HasDatabaseName("idx_command_request_line_equipment");
            entity.HasIndex(e => e.CreatedAt)
                  .HasDatabaseName("idx_command_request_created_at");
        });

        modelBuilder.Entity<CommandResponseEventEntity>(entity =>
        {
            entity.ToTable("command_response_event", "public");

            entity.HasKey(e => e.Id);

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.CommandId).HasColumnName("command_id").IsRequired();
            entity.Property(e => e.LineId).HasColumnName("line_id").IsRequired();
            entity.Property(e => e.EquipmentId).HasColumnName("equipment_id").IsRequired();
            entity.Property(e => e.Accepted).HasColumnName("accepted");
            entity.Property(e => e.CmdStatus).HasColumnName("cmd_status");
            entity.Property(e => e.Status).HasColumnName("status").IsRequired();
            entity.Property(e => e.ResponseJson).HasColumnName("response_json").HasColumnType("jsonb");
            entity.Property(e => e.SourceTsEpochMs).HasColumnName("source_tsepochms");
            entity.Property(e => e.ObservedAt).HasColumnName("observed_at");
            entity.Property(e => e.SnapshotKey).HasColumnName("snapshot_key");
            entity.Property(e => e.SourceNodeId).HasColumnName("source_node_id");
            entity.Property(e => e.LastError).HasColumnName("last_error");

            entity.HasIndex(e => new { e.CommandId, e.ObservedAt })
                  .HasDatabaseName("idx_command_response_event_command");
            entity.HasIndex(e => new { e.LineId, e.EquipmentId, e.ObservedAt })
                  .HasDatabaseName("idx_command_response_event_line_equipment");
            entity.HasIndex(e => e.ObservedAt)
                  .HasDatabaseName("idx_command_response_event_observed_at");
        });

        modelBuilder.Entity<CommandLatestResponseEntity>(entity =>
        {
            entity.ToTable("command_latest_response", "public");

            entity.HasKey(e => new { e.LineId, e.EquipmentId });

            entity.Property(e => e.LineId).HasColumnName("line_id");
            entity.Property(e => e.EquipmentId).HasColumnName("equipment_id");
            entity.Property(e => e.CommandId).HasColumnName("command_id").IsRequired();
            entity.Property(e => e.CommandType).HasColumnName("command_type");
            entity.Property(e => e.Status).HasColumnName("status").IsRequired();
            entity.Property(e => e.Accepted).HasColumnName("accepted");
            entity.Property(e => e.CmdStatus).HasColumnName("cmd_status");
            entity.Property(e => e.ResponseJson).HasColumnName("response_json").HasColumnType("jsonb");
            entity.Property(e => e.SourceTsEpochMs).HasColumnName("source_tsepochms");
            entity.Property(e => e.FirstObservedAt).HasColumnName("first_observed_at");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at");
            entity.Property(e => e.SnapshotKey).HasColumnName("snapshot_key");
            entity.Property(e => e.SourceNodeId).HasColumnName("source_node_id");
            entity.Property(e => e.LastError).HasColumnName("last_error");

            entity.HasIndex(e => e.CommandId)
                  .HasDatabaseName("idx_command_latest_response_command_id");
            entity.HasIndex(e => e.UpdatedAt)
                  .HasDatabaseName("idx_command_latest_response_updated_at");
        });

        modelBuilder.Entity<CommandHistoryViewEntity>(entity =>
        {
            entity.ToView("command_history", "public");
            entity.HasNoKey();

            entity.Property(e => e.CommandId).HasColumnName("command_id");
            entity.Property(e => e.SourceType).HasColumnName("source_type");
            entity.Property(e => e.LineId).HasColumnName("line_id");
            entity.Property(e => e.EquipmentId).HasColumnName("equipment_id");
            entity.Property(e => e.CommandType).HasColumnName("command_type");
            entity.Property(e => e.CommandValue).HasColumnName("command_value").HasColumnType("jsonb");
            entity.Property(e => e.RequestJson).HasColumnName("request_json").HasColumnType("jsonb");
            entity.Property(e => e.RequestStatus).HasColumnName("request_status");
            entity.Property(e => e.Priority).HasColumnName("priority");
            entity.Property(e => e.RetryCount).HasColumnName("retry_count");
            entity.Property(e => e.MaxRetry).HasColumnName("max_retry");
            entity.Property(e => e.CreatedBy).HasColumnName("created_by");
            entity.Property(e => e.RequestCreatedAt).HasColumnName("request_created_at");
            entity.Property(e => e.RequestStartedAt).HasColumnName("request_started_at");
            entity.Property(e => e.RequestFinishedAt).HasColumnName("request_finished_at");
            entity.Property(e => e.PickedAt).HasColumnName("picked_at");
            entity.Property(e => e.RequestWorkerId).HasColumnName("request_worker_id");
            entity.Property(e => e.RequestLastError).HasColumnName("request_last_error");
            entity.Property(e => e.IdempotencyKey).HasColumnName("idempotency_key");
            entity.Property(e => e.ResponseEventId).HasColumnName("response_event_id");
            entity.Property(e => e.OldStatus).HasColumnName("old_status");
            entity.Property(e => e.NewStatus).HasColumnName("new_status");
            entity.Property(e => e.Accepted).HasColumnName("accepted");
            entity.Property(e => e.CmdStatus).HasColumnName("cmd_status");
            entity.Property(e => e.ResponseJson).HasColumnName("response_json").HasColumnType("jsonb");
            entity.Property(e => e.SourceTsEpochMs).HasColumnName("source_tsepochms");
            entity.Property(e => e.ChangedAt).HasColumnName("changed_at");
            entity.Property(e => e.SourceNodeId).HasColumnName("source_node_id");
            entity.Property(e => e.ResponseLastError).HasColumnName("response_last_error");
            entity.Property(e => e.ProcessIndex).HasColumnName("process_index");
        });
    }
}