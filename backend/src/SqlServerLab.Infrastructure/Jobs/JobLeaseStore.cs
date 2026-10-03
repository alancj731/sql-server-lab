using Microsoft.EntityFrameworkCore;
using SqlServerLab.Application.Abstractions;
using SqlServerLab.Application.Errors;
using SqlServerLab.Domain.Jobs;
using SqlServerLab.Infrastructure.Persistence;

namespace SqlServerLab.Infrastructure.Jobs;

public sealed record ClaimedJob(LabJob Job, string LeaseToken, bool Recovered);

public sealed record HeartbeatResult(bool LeaseHeld, bool CancelRequested);

/// <summary>
/// Durable queue operations on the control database. Every write after a claim is conditioned on the claim's lease
/// token, so a worker whose lease expired (and was recovered by another worker) can no longer change the job.
/// </summary>
public sealed class JobLeaseStore(IDbContextFactory<ControlDbContext> contextFactory, IClock clock)
{
    /// <summary>Jobs recovered more often than this are failed instead of looping forever.</summary>
    public const int MaxClaims = 10;

    public async Task<ClaimedJob?> TryClaimNextAsync(string workerId, TimeSpan leaseDuration, CancellationToken cancellationToken)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var now = clock.UtcNow;
        var candidates = await db.Jobs.AsNoTracking()
            .Where(j => (j.Status == LabJobStatus.Queued && j.NotBefore <= now)
                        || (j.Status == LabJobStatus.Running && j.LeaseExpiresAt < now))
            .OrderBy(j => j.NotBefore)
            .Select(j => new { j.Id, j.Status })
            .Take(10)
            .ToListAsync(cancellationToken);

        foreach (var candidate in candidates)
        {
            var token = $"{workerId}:{Guid.NewGuid():N}";
            var leaseUntil = now + leaseDuration;

            // The WHERE clause re-checks claimability, so only one competing worker can update the row.
            var claimed = await db.Jobs
                .Where(j => j.Id == candidate.Id
                            && ((j.Status == LabJobStatus.Queued && j.NotBefore <= now)
                                || (j.Status == LabJobStatus.Running && j.LeaseExpiresAt < now)))
                .ExecuteUpdateAsync(s => s
                    .SetProperty(j => j.Status, LabJobStatus.Running)
                    .SetProperty(j => j.LeaseOwner, token)
                    .SetProperty(j => j.LeaseExpiresAt, leaseUntil)
                    .SetProperty(j => j.HeartbeatAt, now)
                    .SetProperty(j => j.StartedAt, j => j.StartedAt ?? now)
                    .SetProperty(j => j.AttemptCount, j => j.AttemptCount + 1)
                    .SetProperty(j => j.UpdatedAt, now),
                    cancellationToken);

            if (claimed == 1)
            {
                var job = await db.Jobs.AsNoTracking().SingleAsync(j => j.Id == candidate.Id, cancellationToken);
                return new ClaimedJob(job, token, candidate.Status == LabJobStatus.Running);
            }
        }

        return null;
    }

    public async Task<HeartbeatResult> HeartbeatAsync(Guid jobId, string token, TimeSpan leaseDuration, CancellationToken cancellationToken)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var now = clock.UtcNow;
        var leaseUntil = now + leaseDuration;
        var updated = await db.Jobs
            .Where(j => j.Id == jobId && j.LeaseOwner == token && j.Status == LabJobStatus.Running)
            .ExecuteUpdateAsync(s => s.SetProperty(j => j.LeaseExpiresAt, leaseUntil).SetProperty(j => j.HeartbeatAt, now), cancellationToken);
        if (updated == 0)
        {
            return new HeartbeatResult(false, false);
        }

        var cancel = await db.Jobs.Where(j => j.Id == jobId).Select(j => j.CancelRequested).SingleAsync(cancellationToken);
        return new HeartbeatResult(true, cancel);
    }

    public Task ReportProgressAsync(Guid jobId, string token, int progress, string step, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var clamped = Math.Clamp(progress, 0, 100);
        return UpdateOwnedAsync(jobId, token, s => s
            .SetProperty(j => j.Progress, clamped)
            .SetProperty(j => j.CurrentStep, step)
            .SetProperty(j => j.UpdatedAt, now), cancellationToken);
    }

    public Task SetExternalOperationAsync(Guid jobId, string token, string? operationId, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        return UpdateOwnedAsync(jobId, token, s => s
            .SetProperty(j => j.ExternalOperationId, operationId)
            .SetProperty(j => j.UpdatedAt, now), cancellationToken);
    }

    public Task CompleteAsync(Guid jobId, string token, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        return UpdateOwnedAsync(jobId, token, s => s
            .SetProperty(j => j.Status, LabJobStatus.Succeeded)
            .SetProperty(j => j.Progress, 100)
            .SetProperty(j => j.CurrentStep, "Completed")
            .SetProperty(j => j.CompletedAt, now)
            .SetProperty(j => j.LeaseOwner, (string?)null)
            .SetProperty(j => j.LeaseExpiresAt, (DateTimeOffset?)null)
            .SetProperty(j => j.MutexKey, (string?)null)
            .SetProperty(j => j.UpdatedAt, now), cancellationToken);
    }

    public Task FailAsync(Guid jobId, string token, LabJobStatus finalStatus, ErrorCategory category, string code, string message, CancellationToken cancellationToken)
    {
        if (finalStatus is not (LabJobStatus.Failed or LabJobStatus.Cancelled))
        {
            throw new ArgumentOutOfRangeException(nameof(finalStatus));
        }

        var now = clock.UtcNow;
        var safe = Redactor.Sanitize(message);
        return UpdateOwnedAsync(jobId, token, s => s
            .SetProperty(j => j.Status, finalStatus)
            .SetProperty(j => j.CurrentStep, finalStatus == LabJobStatus.Cancelled ? "Cancelled" : "Failed")
            .SetProperty(j => j.ErrorCategory, category)
            .SetProperty(j => j.ErrorCode, code)
            .SetProperty(j => j.ErrorMessage, safe)
            .SetProperty(j => j.CompletedAt, now)
            .SetProperty(j => j.LeaseOwner, (string?)null)
            .SetProperty(j => j.LeaseExpiresAt, (DateTimeOffset?)null)
            .SetProperty(j => j.MutexKey, (string?)null)
            .SetProperty(j => j.UpdatedAt, now), cancellationToken);
    }

    /// <summary>Returns the job to the queue after a transient failure. The mutex stays held so nothing else interleaves.</summary>
    public Task RescheduleAsync(Guid jobId, string token, DateTimeOffset notBefore, ErrorCategory category, string code, string message, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var safe = Redactor.Sanitize(message);
        return UpdateOwnedAsync(jobId, token, s => s
            .SetProperty(j => j.Status, LabJobStatus.Queued)
            .SetProperty(j => j.CurrentStep, "Waiting to retry")
            .SetProperty(j => j.NotBefore, notBefore)
            .SetProperty(j => j.ErrorCategory, category)
            .SetProperty(j => j.ErrorCode, code)
            .SetProperty(j => j.ErrorMessage, safe)
            .SetProperty(j => j.LeaseOwner, (string?)null)
            .SetProperty(j => j.LeaseExpiresAt, (DateTimeOffset?)null)
            .SetProperty(j => j.UpdatedAt, now), cancellationToken);
    }

    private async Task UpdateOwnedAsync(
        Guid jobId,
        string token,
        Action<Microsoft.EntityFrameworkCore.Query.UpdateSettersBuilder<LabJob>> setters,
        CancellationToken cancellationToken)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var updated = await db.Jobs
            .Where(j => j.Id == jobId && j.LeaseOwner == token && j.Status == LabJobStatus.Running)
            .ExecuteUpdateAsync(setters, cancellationToken);
        if (updated == 0)
        {
            throw new LeaseLostException(jobId);
        }
    }
}

public sealed class LeaseLostException(Guid jobId) : Exception($"The lease on job {jobId} is no longer held by this worker.")
{
    public Guid JobId { get; } = jobId;
}
