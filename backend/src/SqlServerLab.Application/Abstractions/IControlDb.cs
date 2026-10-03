using Microsoft.EntityFrameworkCore;
using SqlServerLab.Domain.Audit;
using SqlServerLab.Domain.Jobs;
using SqlServerLab.Domain.Labs;

namespace SqlServerLab.Application.Abstractions;

/// <summary>Control-plane persistence used by application services.</summary>
public interface IControlDb
{
    DbSet<Lab> Labs { get; }
    DbSet<LabJob> Jobs { get; }
    DbSet<AuditEvent> AuditEvents { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>Detaches all tracked entities after a failed save.</summary>
    void ResetTracking();
}
