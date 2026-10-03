using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SqlServerLab.Application.Abstractions;
using SqlServerLab.Application.Audit;
using SqlServerLab.Domain.Audit;
using SqlServerLab.Domain.Labs;
using SqlServerLab.Infrastructure.Persistence;

namespace SqlServerLab.Worker.Services;

public sealed class OrphanOptions
{
    public const string SectionName = "Orphans";

    public int ScanMinutes { get; set; } = 10;

    /// <summary>Resource groups younger than this are left alone (a deployment may still be registering).</summary>
    public int GraceMinutes { get; set; } = 60;
}

/// <summary>
/// Deletes lab resource groups that outlived their control-plane record (missing or already Deleted), so failed
/// deletions and lost records never keep billing.
/// </summary>
public sealed partial class OrphanReconciler(
    ILabResourceInventory inventory,
    IDbContextFactory<ControlDbContext> contextFactory,
    IClock clock,
    IOptions<OrphanOptions> options,
    ILogger<OrphanReconciler> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!inventory.Enabled)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ScanAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                LogScanFailed(ex);
            }

            try
            {
                await Task.Delay(TimeSpan.FromMinutes(options.Value.ScanMinutes), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    public async Task<IReadOnlyList<string>> ScanAsync(CancellationToken cancellationToken)
    {
        var groups = await inventory.ListAsync(cancellationToken);
        if (groups.Count == 0)
        {
            return [];
        }

        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var ids = groups.Where(g => g.LabId is not null).Select(g => g.LabId!.Value).ToList();
        var states = await db.Labs.AsNoTracking().Where(l => ids.Contains(l.Id))
            .Select(l => new { l.Id, l.State }).ToDictionaryAsync(l => l.Id, l => l.State, cancellationToken);

        var orphans = FindOrphans(groups, states, clock.UtcNow, TimeSpan.FromMinutes(options.Value.GraceMinutes));
        foreach (var group in orphans)
        {
            await inventory.DeleteAsync(group.Name, cancellationToken);
            AuditWriter.Add(db, clock.UtcNow, AuditActions.SystemActor, AuditActions.LabOrphanDeleted, $"orphan-{Guid.NewGuid():N}",
                group.LabId, detail: $"resourceGroup={group.Name}");
            LogOrphan(group.Name);
        }

        await db.SaveChangesAsync(cancellationToken);
        return orphans.Select(o => o.Name).ToList();
    }

    /// <summary>Pure decision rule, unit tested.</summary>
    public static IReadOnlyList<LabResourceGroup> FindOrphans(
        IReadOnlyList<LabResourceGroup> groups, IReadOnlyDictionary<Guid, LabState> states, DateTimeOffset now, TimeSpan grace) =>
        groups.Where(g =>
                (g.CreatedAt is null || now - g.CreatedAt.Value > grace)
                && (g.LabId is null || !states.TryGetValue(g.LabId.Value, out var state) || state == LabState.Deleted))
            .ToList();

    [LoggerMessage(Level = LogLevel.Warning, Message = "Deleting orphaned lab resource group {ResourceGroup}")]
    private partial void LogOrphan(string resourceGroup);

    [LoggerMessage(Level = LogLevel.Error, Message = "Orphan scan failed")]
    private partial void LogScanFailed(Exception ex);
}
