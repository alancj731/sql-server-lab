using Microsoft.EntityFrameworkCore;
using SqlServerLab.Application.Abstractions;
using SqlServerLab.Application.Audit;
using SqlServerLab.Application.Errors;
using SqlServerLab.Domain.Audit;
using SqlServerLab.Domain.Jobs;
using SqlServerLab.Domain.Labs;
using SqlServerLab.Infrastructure.Jobs;
using SqlServerLab.Infrastructure.Persistence;

namespace SqlServerLab.Worker.Jobs;

/// <summary>Everything a handler needs to advance a job durably. All writes are lease-guarded or concurrency-checked.</summary>
public sealed class JobContext(
    LabJob job,
    string leaseToken,
    JobLeaseStore store,
    IDbContextFactory<ControlDbContext> contextFactory,
    ILabInfrastructure infrastructure,
    IClock clock,
    WorkerOptions options)
{
    public LabJob Job { get; } = job;
    public string LeaseToken { get; } = leaseToken;
    public ILabInfrastructure Infrastructure { get; } = infrastructure;
    public IClock Clock => clock;

    public async Task<Lab> LoadLabAsync(CancellationToken cancellationToken)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await db.Labs.AsNoTracking().SingleAsync(l => l.Id == Job.LabId, cancellationToken);
    }

    public Task ReportAsync(int progress, string step, CancellationToken cancellationToken)
    {
        Job.Progress = progress;
        Job.CurrentStep = step;
        return store.ReportProgressAsync(Job.Id, LeaseToken, progress, step, cancellationToken);
    }

    /// <summary>Moves the lab to <paramref name="to"/> unless it is already there (resumed jobs repeat steps safely).</summary>
    public Task<Lab> TransitionAsync(LabState to, string? reason, CancellationToken cancellationToken) =>
        UpdateLabAsync(lab =>
        {
            if (lab.State == to)
            {
                return null;
            }

            var from = lab.State;
            LabStateMachine.Transition(lab, to, reason, clock.UtcNow);
            return $"{from} -> {to}";
        }, AuditActions.LabStateChanged, cancellationToken);

    /// <summary>Applies a change to the lab with optimistic-concurrency retries; the mutation returns audit detail or null for no-op.</summary>
    public async Task<Lab> UpdateLabAsync(Func<Lab, string?> mutate, string auditAction, CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
            var lab = await db.Labs.SingleAsync(l => l.Id == Job.LabId, cancellationToken);
            var detail = mutate(lab);
            if (detail is null)
            {
                return lab;
            }

            lab.UpdatedAt = clock.UtcNow;
            AuditWriter.Add(db, clock.UtcNow, AuditActions.SystemActor, auditAction, Job.CorrelationId, lab.Id, Job.Id, detail);
            try
            {
                await db.SaveChangesAsync(cancellationToken);
                return lab;
            }
            catch (DbUpdateConcurrencyException) when (attempt < 5)
            {
                // The API changed the lab (e.g. extended expiry); reload and reapply.
            }
        }
    }

    /// <summary>Begins the infrastructure operation once and persists its ID, or returns the ID saved by an earlier attempt.</summary>
    public async Task<string> EnsureOperationAsync(InfraOperationKind kind, CancellationToken cancellationToken)
    {
        if (Job.ExternalOperationId is not null)
        {
            return Job.ExternalOperationId;
        }

        var lab = await LoadLabAsync(cancellationToken);
        var op = await Infrastructure.BeginAsync(kind, new InfraRequest(lab, Job.Id, Job.AttemptCount), cancellationToken);
        await store.SetExternalOperationAsync(Job.Id, LeaseToken, op.OperationId, cancellationToken);
        Job.ExternalOperationId = op.OperationId;
        return op.OperationId;
    }

    /// <summary>Polls an operation until it finishes, mapping its progress into [from, to].</summary>
    public async Task<InfraOperation> WaitForOperationAsync(string operationId, int from, int to, string step, CancellationToken cancellationToken)
    {
        var deadline = clock.UtcNow.AddMinutes(options.OperationTimeoutMinutes);
        var lastReported = -1;
        while (true)
        {
            var op = await Infrastructure.GetOperationAsync(operationId, cancellationToken)
                     ?? throw new JobExecutionException(ErrorCategory.Permanent, "OperationNotFound", "The infrastructure operation could not be found.");

            var mapped = from + (int)Math.Round((to - from) * (op.Progress / 100.0));
            if (mapped != lastReported)
            {
                await ReportAsync(mapped, step, cancellationToken);
                lastReported = mapped;
            }

            switch (op.Status)
            {
                case InfraOperationStatus.Succeeded:
                    return op;
                case InfraOperationStatus.Failed:
                    var category = op.FailureCategory ?? ErrorCategory.Permanent;
                    if (category == ErrorCategory.Transient)
                    {
                        // The next attempt must begin a fresh operation rather than re-poll the failed one.
                        Job.ExternalOperationId = null;
                        await store.SetExternalOperationAsync(Job.Id, LeaseToken, null, cancellationToken);
                    }

                    throw new JobExecutionException(category, "OperationFailed", op.FailureMessage ?? "The infrastructure operation failed.");
            }

            if (clock.UtcNow > deadline)
            {
                throw new JobExecutionException(ErrorCategory.Transient, "OperationTimeout", $"{step} did not finish within {options.OperationTimeoutMinutes} minutes.");
            }

            await Task.Delay(options.OperationPollIntervalMs, cancellationToken);
        }
    }

    public async Task WaitForSqlReadyAsync(int progress, CancellationToken cancellationToken)
    {
        await ReportAsync(progress, "Waiting for SQL Server readiness", cancellationToken);
        var deadline = clock.UtcNow.AddMinutes(options.OperationTimeoutMinutes);
        while (!await Infrastructure.IsSqlReadyAsync(await LoadLabAsync(cancellationToken), cancellationToken))
        {
            if (clock.UtcNow > deadline)
            {
                throw new JobExecutionException(ErrorCategory.Transient, "SqlNotReady", "SQL Server did not become ready in time.");
            }

            await Task.Delay(options.OperationPollIntervalMs, cancellationToken);
        }
    }
}
