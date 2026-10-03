using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SqlServerLab.Application.Abstractions;
using SqlServerLab.Domain.Audit;
using SqlServerLab.Domain.Jobs;
using SqlServerLab.Domain.Labs;
using SqlServerLab.Infrastructure.Persistence;
using SqlServerLab.Worker;
using SqlServerLab.Worker.Jobs;
using SqlServerLab.Worker.Services;

namespace SqlServerLab.Infrastructure.Tests;

public class WorkerLifecycleTests
{
    [Fact]
    public async Task Simulated_lab_goes_through_the_full_lifecycle()
    {
        await using var h = new WorkerHarness();
        var lab = await h.CreateLabAsync("life");

        await h.DrainAsync();
        var ready = await h.LabAsync(lab.Id);
        Assert.Equal(LabState.Ready, ready.State);
        Assert.NotNull(ready.VmResourceId);

        await h.EnqueueAsync(lab.Id, LabJobType.DeallocateVm);
        await h.DrainAsync();
        Assert.Equal(LabState.Stopped, (await h.LabAsync(lab.Id)).State);

        await h.EnqueueAsync(lab.Id, LabJobType.StartVm);
        await h.DrainAsync();
        Assert.Equal(LabState.Ready, (await h.LabAsync(lab.Id)).State);

        await h.EnqueueAsync(lab.Id, LabJobType.DeleteLab);
        await h.DrainAsync();
        Assert.Equal(LabState.Deleted, (await h.LabAsync(lab.Id)).State);

        var jobs = await h.JobsAsync(lab.Id);
        Assert.All(jobs, j => Assert.Equal(LabJobStatus.Succeeded, j.Status));
        Assert.All(jobs, j => Assert.Null(j.MutexKey));
        Assert.Equal(4, jobs.Count);

        await using var db = h.NewContext();
        var transitions = await db.AuditEvents.Where(a => a.LabId == lab.Id && a.Action == AuditActions.LabStateChanged).CountAsync();
        Assert.True(transitions >= 8);
    }

    [Fact]
    public async Task Transient_failure_is_retried_with_backoff()
    {
        await using var h = new WorkerHarness();
        var lab = await h.CreateLabAsync("flaky-one");

        await h.DrainAsync();

        Assert.Equal(LabState.Ready, (await h.LabAsync(lab.Id)).State);
        var job = Assert.Single(await h.JobsAsync(lab.Id));
        Assert.Equal(LabJobStatus.Succeeded, job.Status);
        Assert.Equal(2, job.AttemptCount);
        await using var db = h.NewContext();
        Assert.True(await db.AuditEvents.AnyAsync(a => a.JobId == job.Id && a.Action == AuditActions.JobRetryScheduled));
    }

    [Fact]
    public async Task Permanent_failure_fails_job_and_lab_without_retry()
    {
        await using var h = new WorkerHarness();
        var lab = await h.CreateLabAsync("fail-lab");

        await h.DrainAsync();

        var failed = await h.LabAsync(lab.Id);
        Assert.Equal(LabState.Failed, failed.State);
        Assert.Contains("unavailable", failed.StateReason);
        var job = Assert.Single(await h.JobsAsync(lab.Id));
        Assert.Equal(LabJobStatus.Failed, job.Status);
        Assert.Equal(ErrorCategory.Permanent, job.ErrorCategory);
        Assert.Equal(1, job.AttemptCount);

        // A failed lab can still be deleted.
        await h.EnqueueAsync(lab.Id, LabJobType.DeleteLab);
        await h.DrainAsync();
        Assert.Equal(LabState.Deleted, (await h.LabAsync(lab.Id)).State);
    }

    [Fact]
    public async Task Worker_restart_resumes_the_same_external_operation()
    {
        await using var h = new WorkerHarness(new() { ["LocalSimulation:ProvisionSeconds"] = "1" });
        var lab = await h.CreateLabAsync("resume");

        // Worker A claims the job, begins the deployment, then "crashes" without completing.
        var lease = TimeSpan.FromMilliseconds(300);
        var claim = (await h.Store.TryClaimNextAsync("worker-a", lease, CancellationToken.None))!;
        var context = new JobContext(claim.Job, claim.LeaseToken, h.Store,
            h.Services.GetRequiredService<IDbContextFactory<ControlDbContext>>(),
            h.Services.GetRequiredService<ILabInfrastructure>(), h.Clock,
            h.Services.GetRequiredService<IOptions<WorkerOptions>>().Value);
        await context.TransitionAsync(LabState.Provisioning, null, CancellationToken.None);
        var operationId = await context.EnsureOperationAsync(InfraOperationKind.Provision, CancellationToken.None);

        await Task.Delay(400); // lease expires
        await h.DrainAsync();

        Assert.Equal(LabState.Ready, (await h.LabAsync(lab.Id)).State);
        var job = Assert.Single(await h.JobsAsync(lab.Id));
        Assert.Equal(operationId, job.ExternalOperationId);
        await using var db = h.NewContext();
        Assert.Equal(1, await db.LocalSimOperations.CountAsync(o => o.LabId == lab.Id));
        Assert.True(await db.AuditEvents.AnyAsync(a => a.JobId == job.Id && a.Action == AuditActions.JobLeaseRecovered));
    }

    [Fact]
    public async Task Cancel_requested_while_running_cancels_and_reconciles()
    {
        await using var h = new WorkerHarness(new() { ["LocalSimulation:DeallocateSeconds"] = "30" });
        var lab = await h.CreateLabAsync("cancel");
        await h.DrainAsync();
        var job = await h.EnqueueAsync(lab.Id, LabJobType.DeallocateVm);

        var processing = h.Processor.ProcessNextAsync(CancellationToken.None);
        await WaitUntilAsync(h, j => j.Id == job.Id && j.Status == LabJobStatus.Running && j.ExternalOperationId != null);
        await using (var db = h.NewContext())
        {
            await db.Jobs.Where(j => j.Id == job.Id).ExecuteUpdateAsync(s => s.SetProperty(j => j.CancelRequested, true));
        }

        await processing;
        var jobs = await h.JobsAsync(lab.Id);
        Assert.Equal(LabJobStatus.Cancelled, jobs.Single(j => j.Id == job.Id).Status);
        Assert.Contains(jobs, j => j.Type == LabJobType.ReconcileLab && j.Status == LabJobStatus.Queued);
    }

    [Fact]
    public async Task Expired_labs_are_scheduled_for_deletion()
    {
        var clock = new ManualClock();
        await using var h = new WorkerHarness(clock: clock);
        var ready = await h.CreateLabAsync("expired", LabState.Ready, job: null);
        var busy = await h.CreateLabAsync("busy", LabState.Provisioning, job: null);
        var fresh = await h.CreateLabAsync("fresh", LabState.Ready, job: null);
        await using (var db = h.NewContext())
        {
            await db.Labs.Where(l => l.Id == ready.Id || l.Id == busy.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(l => l.ExpiresAt, clock.UtcNow.AddMinutes(-1)));
        }

        var expiry = h.Services.GetRequiredService<ExpiryService>();
        Assert.Equal(1, await expiry.ScanAsync(CancellationToken.None));
        Assert.Equal(0, await expiry.ScanAsync(CancellationToken.None)); // idempotent

        Assert.Equal(LabJobType.DeleteLab, Assert.Single(await h.JobsAsync(ready.Id)).Type);
        Assert.Empty(await h.JobsAsync(busy.Id));
        Assert.Empty(await h.JobsAsync(fresh.Id));
        await using var check = h.NewContext();
        Assert.True(await check.AuditEvents.AnyAsync(a => a.LabId == ready.Id && a.Action == AuditActions.LabExpired && a.ActorId == AuditActions.SystemActor));
    }

    [Fact]
    public async Task Job_types_from_later_milestones_fail_without_touching_the_lab()
    {
        await using var h = new WorkerHarness();
        var lab = await h.CreateLabAsync("later", LabState.Ready, LabJobType.RunDeadlock);
        await h.DrainAsync();
        var job = Assert.Single(await h.JobsAsync(lab.Id));
        Assert.Equal(LabJobStatus.Failed, job.Status);
        Assert.Equal(ErrorCategory.Validation, job.ErrorCategory);
        Assert.Equal(LabState.Ready, (await h.LabAsync(lab.Id)).State);
    }

    private static async Task WaitUntilAsync(WorkerHarness h, Func<LabJob, bool> predicate)
    {
        for (var i = 0; i < 200; i++)
        {
            await using var db = h.NewContext();
            if ((await db.Jobs.AsNoTracking().ToListAsync()).Any(predicate))
            {
                return;
            }

            await Task.Delay(25);
        }

        throw new TimeoutException();
    }
}
