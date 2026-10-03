using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using SqlServerLab.Application.Abstractions;
using SqlServerLab.Domain.Audit;
using SqlServerLab.Domain.Jobs;
using SqlServerLab.Domain.Labs;
using SqlServerLab.Domain.Records;
using SqlServerLab.Infrastructure.Simulation;

namespace SqlServerLab.Infrastructure.Persistence;

public sealed class ControlDbContext(DbContextOptions<ControlDbContext> options) : DbContext(options), IControlDb
{
    public DbSet<Lab> Labs => Set<Lab>();
    public DbSet<LabJob> Jobs => Set<LabJob>();
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();
    public DbSet<ExperimentRun> ExperimentRuns => Set<ExperimentRun>();
    public DbSet<MetricSample> MetricSamples => Set<MetricSample>();
    public DbSet<DeadlockEvent> DeadlockEvents => Set<DeadlockEvent>();
    public DbSet<BackupRecord> BackupRecords => Set<BackupRecord>();
    public DbSet<PatchAssessment> PatchAssessments => Set<PatchAssessment>();
    public DbSet<LocalSimOperation> LocalSimOperations => Set<LocalSimOperation>();

    public void ResetTracking() => ChangeTracker.Clear();

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        ApplyRules();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        ApplyRules();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<Enum>().HaveConversion<string>().HaveMaxLength(32);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Lab>(b =>
        {
            b.ToTable("Labs");
            b.HasKey(x => x.Id);
            b.Property(x => x.OwnerId).HasMaxLength(128);
            b.Property(x => x.OwnerName).HasMaxLength(256);
            b.Property(x => x.Name).HasMaxLength(30);
            b.Property(x => x.Region).HasMaxLength(32);
            b.Property(x => x.ResourceGroupName).HasMaxLength(90);
            b.Property(x => x.VmResourceId).HasMaxLength(512);
            b.Property(x => x.SqlVmResourceId).HasMaxLength(512);
            b.Property(x => x.StateReason).HasMaxLength(600);
            b.Property(x => x.RowVersion).IsConcurrencyToken();
            b.HasIndex(x => new { x.OwnerId, x.State });
            b.HasIndex(x => x.ExpiresAt);
            b.HasIndex(x => x.UpdatedAt);
        });

        modelBuilder.Entity<LabJob>(b =>
        {
            b.ToTable("LabJobs");
            b.HasKey(x => x.Id);
            b.Property(x => x.CurrentStep).HasMaxLength(200);
            b.Property(x => x.LeaseOwner).HasMaxLength(200);
            b.Property(x => x.CorrelationId).HasMaxLength(64);
            b.Property(x => x.RequestedBy).HasMaxLength(128);
            b.Property(x => x.ErrorCode).HasMaxLength(64);
            b.Property(x => x.ErrorMessage).HasMaxLength(600);
            b.Property(x => x.IdempotencyKey).HasMaxLength(200);
            b.Property(x => x.ExternalOperationId).HasMaxLength(512);
            b.Property(x => x.MutexKey).HasMaxLength(64);
            b.Property(x => x.InputJson).HasMaxLength(4000);
            b.Ignore(x => x.IsActive);
            b.HasIndex(x => x.IdempotencyKey).IsUnique();
            b.HasIndex(x => x.MutexKey).IsUnique().HasFilter("[MutexKey] IS NOT NULL");
            b.HasIndex(x => new { x.Status, x.NotBefore });
            b.HasIndex(x => new { x.LabId, x.RequestedAt });
            b.HasIndex(x => x.UpdatedAt);
            b.HasOne<Lab>().WithMany().HasForeignKey(x => x.LabId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<AuditEvent>(b =>
        {
            b.ToTable("AuditEvents");
            b.HasKey(x => x.Id);
            b.Property(x => x.ActorId).HasMaxLength(128);
            b.Property(x => x.Action).HasMaxLength(64);
            b.Property(x => x.Detail).HasMaxLength(1000);
            b.Property(x => x.CorrelationId).HasMaxLength(64);
            b.HasIndex(x => new { x.LabId, x.OccurredAt });
        });

        modelBuilder.Entity<ExperimentRun>(b =>
        {
            b.ToTable("ExperimentRuns");
            b.Property(x => x.ScenarioId).HasMaxLength(100);
            b.Property(x => x.Status).HasMaxLength(32);
            b.HasIndex(x => new { x.LabId, x.ScenarioId });
        });
        modelBuilder.Entity<MetricSample>(b =>
        {
            b.ToTable("MetricSamples");
            b.Property(x => x.Source).HasMaxLength(32);
            b.Property(x => x.Name).HasMaxLength(100);
            b.Property(x => x.Unit).HasMaxLength(32);
            b.HasIndex(x => new { x.LabId, x.Name, x.SampledAt });
        });
        modelBuilder.Entity<DeadlockEvent>(b => b.ToTable("DeadlockEvents"));
        modelBuilder.Entity<BackupRecord>(b =>
        {
            b.ToTable("BackupRecords");
            b.Property(x => x.DatabaseName).HasMaxLength(128);
            b.Property(x => x.BackupType).HasMaxLength(32);
            b.Property(x => x.Status).HasMaxLength(32);
        });
        modelBuilder.Entity<PatchAssessment>(b =>
        {
            b.ToTable("PatchAssessments");
            b.Property(x => x.Status).HasMaxLength(32);
        });
        modelBuilder.Entity<LocalSimOperation>(b =>
        {
            b.ToTable("LocalSimOperations");
            b.Property(x => x.Id).HasMaxLength(128);
            b.HasIndex(x => new { x.LabId, x.StartedAt });
        });

        if (Database.IsSqlite())
        {
            // SQLite cannot order or compare DateTimeOffset natively; all stored values are UTC so binary is order-preserving.
            foreach (var property in modelBuilder.Model.GetEntityTypes().SelectMany(t => t.GetProperties()))
            {
                if (property.ClrType == typeof(DateTimeOffset))
                {
                    property.SetValueConverter(new DateTimeOffsetToBinaryConverter());
                }
                else if (property.ClrType == typeof(DateTimeOffset?))
                {
                    property.SetValueConverter(new DateTimeOffsetToBinaryConverter());
                }
            }
        }
    }

    private void ApplyRules()
    {
        foreach (var entry in ChangeTracker.Entries())
        {
            switch (entry.Entity)
            {
                case AuditEvent when entry.State is EntityState.Modified or EntityState.Deleted:
                    throw new InvalidOperationException("Audit events are append-only.");
                case Lab lab when entry.State == EntityState.Added && lab.RowVersion == Guid.Empty:
                    lab.RowVersion = Guid.NewGuid();
                    break;
                case Lab lab when entry.State == EntityState.Modified:
                    lab.RowVersion = Guid.NewGuid();
                    break;
            }
        }
    }
}
