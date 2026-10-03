using Microsoft.EntityFrameworkCore;
using SqlServerLab.Domain.Audit;
using SqlServerLab.Domain.Jobs;
using SqlServerLab.Infrastructure.Jobs;

namespace SqlServerLab.Infrastructure.Tests;

public class JobLeaseStoreTests
{
    [Fact]
    public async Task Migrations_apply_cleanly()
    {
        await using var h = new WorkerHarness();
        await using var db = h.NewContext();
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
    }

    [Fact]
    public async Task Competing_workers_claim_a_job_exactly_once()
    {
        await using var h = new WorkerHarness();
        await h.CreateLabAsync("race");

        var claims = await Task.WhenAll(Enumerable.Range(0, 8).Select(i =>
            Task.Run(() => h.Store.TryClaimNextAsync($"worker-{i}", TimeSpan.FromSeconds(30), CancellationToken.None))));

        var winner = Assert.Single(claims, c => c is not null);
        Assert.Equal(1, winner!.Job.AttemptCount);
        Assert.False(winner.Recovered);
    }

    [Fact]
    public async Task Expired_lease_is_recovered_and_the_old_owner_is_fenced_out()
    {
        var clock = new ManualClock();
        await using var h = new WorkerHarness(clock: clock);
        await h.CreateLabAsync("recover");

        var first = await h.Store.TryClaimNextAsync("worker-a", TimeSpan.FromSeconds(10), CancellationToken.None);
        Assert.NotNull(first);
        Assert.Null(await h.Store.TryClaimNextAsync("worker-b", TimeSpan.FromSeconds(10), CancellationToken.None));

        clock.UtcNow = clock.UtcNow.AddSeconds(11);
        var second = await h.Store.TryClaimNextAsync("worker-b", TimeSpan.FromSeconds(10), CancellationToken.None);
        Assert.NotNull(second);
        Assert.True(second!.Recovered);
        Assert.Equal(2, second.Job.AttemptCount);

        await Assert.ThrowsAsync<LeaseLostException>(() =>
            h.Store.ReportProgressAsync(first!.Job.Id, first.LeaseToken, 50, "zombie", CancellationToken.None));
        Assert.False((await h.Store.HeartbeatAsync(first!.Job.Id, first.LeaseToken, TimeSpan.FromSeconds(10), CancellationToken.None)).LeaseHeld);
        Assert.True((await h.Store.HeartbeatAsync(second.Job.Id, second.LeaseToken, TimeSpan.FromSeconds(10), CancellationToken.None)).LeaseHeld);
    }

    [Fact]
    public async Task Rescheduled_job_waits_for_backoff()
    {
        var clock = new ManualClock();
        await using var h = new WorkerHarness(clock: clock);
        await h.CreateLabAsync("backoff");
        var claim = (await h.Store.TryClaimNextAsync("w", TimeSpan.FromSeconds(10), CancellationToken.None))!;

        await h.Store.RescheduleAsync(claim.Job.Id, claim.LeaseToken, clock.UtcNow.AddSeconds(5), ErrorCategory.Transient, "x", "y", CancellationToken.None);
        Assert.Null(await h.Store.TryClaimNextAsync("w", TimeSpan.FromSeconds(10), CancellationToken.None));
        clock.UtcNow = clock.UtcNow.AddSeconds(6);
        Assert.NotNull(await h.Store.TryClaimNextAsync("w", TimeSpan.FromSeconds(10), CancellationToken.None));
    }

    [Fact]
    public async Task Only_one_active_mutating_job_per_lab_is_possible_at_the_database_level()
    {
        await using var h = new WorkerHarness();
        var lab = await h.CreateLabAsync("mutex");
        await using var db = h.NewContext();
        db.Jobs.Add(new LabJob
        {
            Id = Guid.NewGuid(),
            LabId = lab.Id,
            Type = LabJobType.DeleteLab,
            CorrelationId = "c",
            RequestedBy = "alice",
            IdempotencyKey = "different-key",
            MutexKey = lab.Id.ToString("N"),
        });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Audit_events_are_append_only()
    {
        await using var h = new WorkerHarness();
        await h.CreateLabAsync("audit");
        await using var db = h.NewContext();
        var audit = await db.AuditEvents.FirstAsync();

        audit.Detail = "tampered";
        Assert.Throws<InvalidOperationException>(() => db.SaveChanges());
        db.ChangeTracker.Clear();

        db.AuditEvents.Remove(await db.AuditEvents.FirstAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        Assert.Contains(await db.AuditEvents.AsNoTracking().Select(a => a.Action).ToListAsync(), a => a == AuditActions.JobEnqueued);
    }
}
