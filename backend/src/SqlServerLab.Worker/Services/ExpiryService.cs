using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SqlServerLab.Application.Abstractions;
using SqlServerLab.Application.Audit;
using SqlServerLab.Application.Errors;
using SqlServerLab.Application.Jobs;
using SqlServerLab.Domain.Audit;
using SqlServerLab.Domain.Jobs;
using SqlServerLab.Domain.Labs;
using SqlServerLab.Infrastructure.Persistence;

namespace SqlServerLab.Worker.Services;

/// <summary>Enqueues deletion for labs past their expiry so abandoned labs never run indefinitely.</summary>
public sealed partial class ExpiryService(
    IDbContextFactory<ControlDbContext> contextFactory,
    IClock clock,
    IOptions<WorkerOptions> options,
    ILogger<ExpiryService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
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
                LogScanError(ex);
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(options.Value.ExpiryScanSeconds), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    public async Task<int> ScanAsync(CancellationToken cancellationToken)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var now = clock.UtcNow;
        var expired = await db.Labs.AsNoTracking()
            .Where(l => l.ExpiresAt < now && l.State != LabState.Deleted && l.State != LabState.Deleting)
            .ToListAsync(cancellationToken);

        var enqueued = 0;
        foreach (var lab in expired.Where(l => LabStateMachine.IsAllowed(l.State, LabCommand.Delete)))
        {
            await using var scopeDb = await contextFactory.CreateDbContextAsync(cancellationToken);
            var jobs = new JobService(scopeDb, clock);
            var correlationId = $"expiry-{Guid.NewGuid():N}";
            try
            {
                var result = await jobs.EnqueueAsync(lab, LabJobType.DeleteLab, AuditActions.SystemActor, correlationId, cancellationToken: cancellationToken);
                if (result.Created)
                {
                    AuditWriter.Add(scopeDb, now, AuditActions.SystemActor, AuditActions.LabExpired, correlationId, lab.Id, result.Job.Id, $"expiredAt={lab.ExpiresAt:O}");
                    await scopeDb.SaveChangesAsync(cancellationToken);
                    enqueued++;
                    LogExpired(lab.Id);
                }
            }
            catch (ConflictException)
            {
                // Another operation is running; the next scan retries.
            }
        }

        return enqueued;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Lab {LabId} expired; deletion enqueued")]
    private partial void LogExpired(Guid labId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Expiry scan failed")]
    private partial void LogScanError(Exception ex);
}
