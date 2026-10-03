using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SqlServerLab.Application.Abstractions;
using SqlServerLab.Application.Audit;
using SqlServerLab.Application.Errors;
using SqlServerLab.Application.Jobs;
using SqlServerLab.Domain.Audit;
using SqlServerLab.Domain.Jobs;
using SqlServerLab.Domain.Labs;
using SqlServerLab.Infrastructure.Jobs;
using SqlServerLab.Infrastructure.Persistence;

namespace SqlServerLab.Worker.Jobs;

/// <summary>Claims and executes one job at a time with lease heartbeats, retry classification, and reconciliation.</summary>
public sealed partial class JobProcessor(
    JobLeaseStore store,
    IDbContextFactory<ControlDbContext> contextFactory,
    ILabInfrastructure infrastructure,
    IClock clock,
    IEnumerable<IJobHandler> handlers,
    IOptions<WorkerOptions> options,
    ILogger<JobProcessor> logger)
{
    private readonly Dictionary<LabJobType, IJobHandler> _handlers = handlers.ToDictionary(h => h.Type);
    private readonly WorkerOptions _options = options.Value;
    private readonly string _workerId = options.Value.ResolveWorkerId();

    /// <summary>Returns true if a job was processed.</summary>
    public async Task<bool> ProcessNextAsync(CancellationToken stoppingToken)
    {
        var lease = TimeSpan.FromSeconds(_options.LeaseSeconds);
        var claimed = await store.TryClaimNextAsync(_workerId, lease, stoppingToken);
        if (claimed is null)
        {
            return false;
        }

        await ExecuteAsync(claimed, lease, stoppingToken);
        return true;
    }

    private async Task ExecuteAsync(ClaimedJob claimed, TimeSpan lease, CancellationToken stoppingToken)
    {
        var job = claimed.Job;
        using var scope = logger.BeginScope(new Dictionary<string, object> { ["JobId"] = job.Id, ["LabId"] = job.LabId, ["CorrelationId"] = job.CorrelationId });
        LogClaimed(job.Id, job.Type, job.AttemptCount, claimed.Recovered);

        if (claimed.Recovered)
        {
            await AuditAsync(job, AuditActions.JobLeaseRecovered, $"attempt={job.AttemptCount}", stoppingToken);
        }

        if (job.AttemptCount > JobLeaseStore.MaxClaims)
        {
            await FailAsync(job, claimed.LeaseToken, ErrorCategory.Permanent, "TooManyRecoveries", "The job was interrupted too many times.", stoppingToken);
            return;
        }

        using var jobCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        var userCancelled = false;
        var leaseLost = false;
        using var heartbeatCts = new CancellationTokenSource();
        var heartbeat = Task.Run(async () =>
        {
            try
            {
                while (!heartbeatCts.IsCancellationRequested)
                {
                    await Task.Delay(_options.HeartbeatIntervalMs, heartbeatCts.Token);
                    var result = await store.HeartbeatAsync(job.Id, claimed.LeaseToken, lease, heartbeatCts.Token);
                    if (!result.LeaseHeld)
                    {
                        leaseLost = true;
                        await jobCts.CancelAsync();
                        return;
                    }

                    if (result.CancelRequested && !userCancelled)
                    {
                        userCancelled = true;
                        await jobCts.CancelAsync();
                    }
                }
            }
            catch (OperationCanceledException)
            {
            }
        }, CancellationToken.None);

        var handler = _handlers.GetValueOrDefault(job.Type) ?? new Handlers.NotYetAvailableHandler(job.Type);
        var context = new JobContext(job, claimed.LeaseToken, store, contextFactory, infrastructure, clock, _options);
        try
        {
            if (job.CancelRequested)
            {
                userCancelled = true;
                throw new OperationCanceledException();
            }

            await handler.ExecuteAsync(context, jobCts.Token);
            await StopHeartbeatAsync();
            await store.CompleteAsync(job.Id, claimed.LeaseToken, stoppingToken);
            await TouchLabAsync(job, AuditActions.JobSucceeded, job.Type.ToString(), stoppingToken);
            LogSucceeded(job.Id, job.Type);
        }
        catch (Exception ex) when (ex is LeaseLostException || (ex is OperationCanceledException && leaseLost))
        {
            LogLeaseLost(job.Id);
        }
        catch (OperationCanceledException) when (userCancelled)
        {
            await StopHeartbeatAsync();
            await FinishCancelledAsync(job, claimed.LeaseToken, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutdown: the lease expires and another worker (or this one after restart) resumes the job.
            LogShutdown(job.Id);
        }
        catch (Exception ex)
        {
            await StopHeartbeatAsync();
            var (category, code, message) = Classify(ex);
            if (category == ErrorCategory.Transient)
            {
                LogTransientFailure(ex, job.Id, code);
            }
            else
            {
                LogFailure(ex, job.Id, category, code);
            }

            await HandleFailureAsync(job, claimed.LeaseToken, category, code, message, stoppingToken);
        }
        finally
        {
            await StopHeartbeatAsync();
        }

        async Task StopHeartbeatAsync()
        {
            if (!heartbeatCts.IsCancellationRequested)
            {
                await heartbeatCts.CancelAsync();
            }

            await heartbeat;
        }
    }

    private async Task HandleFailureAsync(LabJob job, string token, ErrorCategory category, string code, string message, CancellationToken cancellationToken)
    {
        if (LabJobPolicy.ShouldRetry(job.Type, category, job.AttemptCount))
        {
            var delay = LabJobPolicy.Backoff(job.AttemptCount, Random.Shared);
            await store.RescheduleAsync(job.Id, token, clock.UtcNow + delay, category, code, message, cancellationToken);
            await AuditAsync(job, AuditActions.JobRetryScheduled, $"attempt={job.AttemptCount} delayMs={(int)delay.TotalMilliseconds} code={code}", cancellationToken);
            return;
        }

        await FailAsync(job, token, category, code, message, cancellationToken);

        if (category == ErrorCategory.Transient && !LabJobPolicy.AutoRetryAllowed(job.Type))
        {
            // Ambiguous outcome for an operation that must not be blindly repeated: reconcile with reality first.
            await EnqueueReconcileAsync(job, cancellationToken);
        }
        else if (category is not (ErrorCategory.Validation or ErrorCategory.Cancellation))
        {
            await MarkLabFailedAsync(job, $"{job.Type} failed: {message}", cancellationToken);
        }
    }

    private async Task FailAsync(LabJob job, string token, ErrorCategory category, string code, string message, CancellationToken cancellationToken)
    {
        await store.FailAsync(job.Id, token, LabJobStatus.Failed, category, code, message, cancellationToken);
        await TouchLabAsync(job, AuditActions.JobFailed, $"{job.Type} {category}/{code}: {message}", cancellationToken);
    }

    private async Task FinishCancelledAsync(LabJob job, string token, CancellationToken cancellationToken)
    {
        await store.FailAsync(job.Id, token, LabJobStatus.Cancelled, ErrorCategory.Cancellation, "Cancelled", "Cancelled at the user's request.", cancellationToken);
        await TouchLabAsync(job, AuditActions.JobCancelled, job.Type.ToString(), cancellationToken);
        if (job.Type != LabJobType.ReconcileLab)
        {
            await EnqueueReconcileAsync(job, cancellationToken);
        }
    }

    private async Task EnqueueReconcileAsync(LabJob job, CancellationToken cancellationToken)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var lab = await db.Labs.SingleAsync(l => l.Id == job.LabId, cancellationToken);
        if (lab.State == LabState.Deleted)
        {
            return;
        }

        var jobs = new JobService(db, clock);
        try
        {
            await jobs.EnqueueAsync(lab, LabJobType.ReconcileLab, AuditActions.SystemActor, job.CorrelationId,
                $"{job.Id:N}:reconcile", cancellationToken);
        }
        catch (ConflictException)
        {
            // Another mutating job already owns the lab; it will leave the lab in a known state.
        }
    }

    private async Task MarkLabFailedAsync(LabJob job, string reason, CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= 5; attempt++)
        {
            await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
            var lab = await db.Labs.SingleAsync(l => l.Id == job.LabId, cancellationToken);
            if (!LabStateMachine.CanTransition(lab.State, LabState.Failed))
            {
                return;
            }

            var from = lab.State;
            LabStateMachine.Transition(lab, LabState.Failed, Redactor.Sanitize(reason), clock.UtcNow);
            AuditWriter.Add(db, clock.UtcNow, AuditActions.SystemActor, AuditActions.LabStateChanged, job.CorrelationId, lab.Id, job.Id, $"{from} -> Failed");
            try
            {
                await db.SaveChangesAsync(cancellationToken);
                return;
            }
            catch (DbUpdateConcurrencyException)
            {
            }
        }
    }

    /// <summary>Writes an audit event and bumps the lab's version so the next identical request is a new job.</summary>
    private async Task TouchLabAsync(LabJob job, string action, string detail, CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= 5; attempt++)
        {
            await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
            var lab = await db.Labs.SingleAsync(l => l.Id == job.LabId, cancellationToken);
            lab.Touch(clock.UtcNow);
            AuditWriter.Add(db, clock.UtcNow, AuditActions.SystemActor, action, job.CorrelationId, job.LabId, job.Id, detail);
            try
            {
                await db.SaveChangesAsync(cancellationToken);
                return;
            }
            catch (DbUpdateConcurrencyException)
            {
            }
        }
    }

    private async Task AuditAsync(LabJob job, string action, string detail, CancellationToken cancellationToken)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        AuditWriter.Add(db, clock.UtcNow, AuditActions.SystemActor, action, job.CorrelationId, job.LabId, job.Id, detail);
        await db.SaveChangesAsync(cancellationToken);
    }

    internal static (ErrorCategory Category, string Code, string Message) Classify(Exception ex) => ex switch
    {
        JobExecutionException j => (j.Category, j.Code, j.Message),
        InvalidLabTransitionException t => (ErrorCategory.Permanent, "InvalidLabState", t.Message),
        TimeoutException => (ErrorCategory.Transient, "Timeout", "The operation timed out."),
        HttpRequestException => (ErrorCategory.Transient, "NetworkError", "A network error occurred."),
        DbUpdateException => (ErrorCategory.Transient, "ControlDbError", "The control database rejected an update."),
        UnauthorizedAccessException => (ErrorCategory.Authorization, "Unauthorized", "The worker is not authorized to perform this operation."),
        _ => (ErrorCategory.Transient, "Unexpected", $"Unexpected {ex.GetType().Name}."),
    };

    [LoggerMessage(Level = LogLevel.Information, Message = "Claimed job {JobId} ({JobType}) attempt {Attempt}, recovered={Recovered}")]
    private partial void LogClaimed(Guid jobId, LabJobType jobType, int attempt, bool recovered);

    [LoggerMessage(Level = LogLevel.Information, Message = "Job {JobId} ({JobType}) succeeded")]
    private partial void LogSucceeded(Guid jobId, LabJobType jobType);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Lost lease on job {JobId}; another worker owns it now")]
    private partial void LogLeaseLost(Guid jobId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Shutdown while running job {JobId}; it will resume after lease expiry")]
    private partial void LogShutdown(Guid jobId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Job {JobId} transient failure {Code}")]
    private partial void LogTransientFailure(Exception ex, Guid jobId, string code);

    [LoggerMessage(Level = LogLevel.Error, Message = "Job {JobId} failed: {Category}/{Code}")]
    private partial void LogFailure(Exception ex, Guid jobId, ErrorCategory category, string code);
}
