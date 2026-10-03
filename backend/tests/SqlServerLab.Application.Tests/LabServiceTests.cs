using Microsoft.EntityFrameworkCore;
using SqlServerLab.Application.Errors;
using SqlServerLab.Application.Labs;
using SqlServerLab.Domain.Audit;
using SqlServerLab.Domain.Jobs;
using SqlServerLab.Domain.Labs;

namespace SqlServerLab.Application.Tests;

public sealed class LabServiceTests : IDisposable
{
    private readonly TestDb _db = new();
    private readonly CancellationToken _ct = CancellationToken.None;

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task Create_persists_lab_job_and_audit_in_one_unit()
    {
        var result = await _db.Service().CreateAsync(new CreateLabRequest("perf-1", "eastus", 4), null, _ct);

        Assert.Equal(LabState.Requested, result.Lab.State);
        Assert.Equal(LabJobType.ProvisionLab, result.Job.Type);
        Assert.Equal(LabJobStatus.Queued, result.Job.Status);
        Assert.Equal($"/api/v1/jobs/{result.Job.JobId}", result.Job.StatusUrl);
        Assert.Equal(_db.Clock.UtcNow.AddHours(4), result.Lab.ExpiresAt);
        Assert.StartsWith("rg-sqllab-perf-1-", result.Lab.ResourceGroupName);
        Assert.True(result.Lab.IsSimulated);

        await using var ctx = _db.Context();
        var actions = await ctx.AuditEvents.Select(a => a.Action).ToListAsync(_ct);
        Assert.Contains(AuditActions.LabRequested, actions);
        Assert.Contains(AuditActions.JobEnqueued, actions);
    }

    [Fact]
    public async Task Create_validates_all_fields()
    {
        var ex = await Assert.ThrowsAsync<ValidationFailedException>(() =>
            _db.Service().CreateAsync(new CreateLabRequest("Bad Name", "mars", 99), null, _ct));
        Assert.Equal(["Name", "Region", "TtlHours"], ex.Errors.Keys.Order());
    }

    [Fact]
    public async Task Create_is_restricted_to_the_deployment_regions()
    {
        var svc = _db.Service(regions: ["centralus"]);
        var ex = await Assert.ThrowsAsync<ValidationFailedException>(() => svc.CreateAsync(new CreateLabRequest("lab-r", "eastus", 1), null, _ct));
        Assert.Contains("centralus", ex.Errors["Region"][0]);
        var ok = await _db.Service(regions: ["centralus"]).CreateAsync(new CreateLabRequest("lab-r", "centralus", 1), null, _ct);
        Assert.Equal("alice (display)", ok.Lab.OwnerName);
    }

    [Fact]
    public async Task Create_enforces_per_user_limit()
    {
        var svc = _db.Service(maxLabs: 1);
        await svc.CreateAsync(new CreateLabRequest("one", "eastus", 1), null, _ct);
        await Assert.ThrowsAsync<QuotaExceededException>(() => _db.Service(maxLabs: 1).CreateAsync(new CreateLabRequest("two", "eastus", 1), null, _ct));

        // Another user's quota is independent.
        await _db.Service("bob", maxLabs: 1).CreateAsync(new CreateLabRequest("two", "eastus", 1), null, _ct);
    }

    [Fact]
    public async Task Repeated_create_returns_the_original_job()
    {
        var first = await _db.Service().CreateAsync(new CreateLabRequest("dup", "eastus", 1), null, _ct);
        var second = await _db.Service().CreateAsync(new CreateLabRequest("dup", "eastus", 1), null, _ct);
        Assert.Equal(first.Job.JobId, second.Job.JobId);

        var replay = await _db.Service().CreateAsync(new CreateLabRequest("other", "eastus", 1), "key-1", _ct);
        var replay2 = await _db.Service().CreateAsync(new CreateLabRequest("other", "eastus", 1), "key-1", _ct);
        Assert.Equal(replay.Job.JobId, replay2.Job.JobId);

        await using var ctx = _db.Context();
        Assert.Equal(2, await ctx.Labs.CountAsync(_ct));
    }

    [Fact]
    public async Task Duplicate_name_is_rejected_once_provisioning_finished()
    {
        var created = await _db.Service().CreateAsync(new CreateLabRequest("dup", "eastus", 1), null, _ct);
        await FinishActiveJobsAsync();
        _db.SetState(created.Lab.Id, LabState.Ready);
        await Assert.ThrowsAsync<ConflictException>(() => _db.Service().CreateAsync(new CreateLabRequest("dup", "eastus", 1), null, _ct));
    }

    [Fact]
    public async Task Commands_are_rejected_in_conflicting_states()
    {
        var created = await _db.Service().CreateAsync(new CreateLabRequest("lab-a", "eastus", 1), null, _ct);
        await Assert.ThrowsAsync<ConflictException>(() => _db.Service().StartAsync(created.Lab.Id, null, _ct));
        await Assert.ThrowsAsync<ConflictException>(() => _db.Service().DeallocateAsync(created.Lab.Id, null, _ct));
    }

    [Fact]
    public async Task Repeated_command_is_idempotent_and_conflicting_command_is_rejected()
    {
        var labId = await ReadyLabAsync();

        var first = await _db.Service().DeallocateAsync(labId, null, _ct);
        var again = await _db.Service().DeallocateAsync(labId, null, _ct);
        Assert.Equal(first.JobId, again.JobId);

        // Delete is allowed in Ready, but a mutating job is already active.
        await Assert.ThrowsAsync<ConflictException>(() => _db.Service().DeleteAsync(labId, new DeleteLabRequest("ready-lab"), null, _ct));

        var lab = await _db.Service().GetAsync(labId, _ct);
        Assert.Equal(first.JobId, lab.CurrentJob?.JobId);
        Assert.Equal([LabCommand.ExtendExpiration], lab.AllowedActions);

        await using var ctx = _db.Context();
        Assert.Equal(1, await ctx.Jobs.CountAsync(j => j.Type == LabJobType.DeallocateVm, _ct));
    }

    [Fact]
    public async Task Client_idempotency_key_replays_after_state_changes()
    {
        var labId = await ReadyLabAsync();
        var first = await _db.Service().DeallocateAsync(labId, "click-1", _ct);
        await FinishActiveJobsAsync();
        _db.SetState(labId, LabState.Stopped);

        var replay = await _db.Service().DeallocateAsync(labId, "click-1", _ct);
        Assert.Equal(first.JobId, replay.JobId);
    }

    [Fact]
    public async Task Delete_requires_exact_name()
    {
        var labId = await ReadyLabAsync();
        var ex = await Assert.ThrowsAsync<ValidationFailedException>(() =>
            _db.Service().DeleteAsync(labId, new DeleteLabRequest("READY-LAB"), null, _ct));
        Assert.Contains("ConfirmName", ex.Errors.Keys);

        var job = await _db.Service().DeleteAsync(labId, new DeleteLabRequest("ready-lab"), null, _ct);
        Assert.Equal(LabJobType.DeleteLab, job.Type);
        Assert.Equal(1, job.MaxAttempts);
    }

    [Fact]
    public async Task Other_users_labs_are_invisible()
    {
        var created = await _db.Service("alice").CreateAsync(new CreateLabRequest("secret", "eastus", 1), null, _ct);

        Assert.Empty(await _db.Service("bob").ListAsync(_ct));
        await Assert.ThrowsAsync<NotFoundException>(() => _db.Service("bob").GetAsync(created.Lab.Id, _ct));
        await Assert.ThrowsAsync<NotFoundException>(() => _db.Service("bob").GetJobAsync(created.Job.JobId, _ct));
        await Assert.ThrowsAsync<NotFoundException>(() => _db.Service("bob").CancelJobAsync(created.Job.JobId, _ct));
        await Assert.ThrowsAsync<NotFoundException>(() => _db.Service("bob").ListAuditAsync(created.Lab.Id, _ct));
        await Assert.ThrowsAsync<NotFoundException>(() => _db.Service("bob").DeleteAsync(created.Lab.Id, new DeleteLabRequest("secret"), null, _ct));
        Assert.False(await _db.Service("bob").CanAccessAsync(created.Lab.Id, _ct));

        Assert.Single(await _db.Service("admin", admin: true).ListAsync(_ct));
    }

    [Fact]
    public async Task Cancel_queued_job_releases_the_lab()
    {
        var labId = await ReadyLabAsync();
        var job = await _db.Service().DeallocateAsync(labId, null, _ct);

        var cancelled = await _db.Service().CancelJobAsync(job.JobId, _ct);
        Assert.Equal(LabJobStatus.Cancelled, cancelled.Status);

        // Mutex released and lab version changed, so a new request creates a new job.
        var next = await _db.Service().DeallocateAsync(labId, null, _ct);
        Assert.NotEqual(job.JobId, next.JobId);
    }

    [Fact]
    public async Task Cancel_running_job_sets_flag_and_finished_job_conflicts()
    {
        var labId = await ReadyLabAsync();
        var job = await _db.Service().DeallocateAsync(labId, null, _ct);
        await using (var ctx = _db.Context())
        {
            await ctx.Jobs.Where(j => j.Id == job.JobId).ExecuteUpdateAsync(s => s.SetProperty(j => j.Status, LabJobStatus.Running), _ct);
        }

        var running = await _db.Service().CancelJobAsync(job.JobId, _ct);
        Assert.True(running.CancelRequested);
        Assert.Equal(LabJobStatus.Running, running.Status);

        await FinishActiveJobsAsync();
        await Assert.ThrowsAsync<ConflictException>(() => _db.Service().CancelJobAsync(job.JobId, _ct));
    }

    [Fact]
    public async Task Extend_expiration_is_bounded_and_audited()
    {
        var created = await _db.Service().CreateAsync(new CreateLabRequest("ext", "eastus", 8), null, _ct);
        await Assert.ThrowsAsync<ValidationFailedException>(() => _db.Service().ExtendExpirationAsync(created.Lab.Id, new ExtendExpirationRequest(5), _ct));

        var lab = await _db.Service().ExtendExpirationAsync(created.Lab.Id, new ExtendExpirationRequest(4), _ct);
        Assert.Equal(created.Lab.ExpiresAt.AddHours(4), lab.ExpiresAt);
        for (var i = 0; i < 3; i++)
        {
            lab = await _db.Service().ExtendExpirationAsync(created.Lab.Id, new ExtendExpirationRequest(4), _ct);
        }

        Assert.Equal(created.Lab.CreatedAt.AddHours(24), lab.ExpiresAt);
        await Assert.ThrowsAsync<ConflictException>(() => _db.Service().ExtendExpirationAsync(created.Lab.Id, new ExtendExpirationRequest(1), _ct));

        var audit = await _db.Service().ListAuditAsync(created.Lab.Id, _ct);
        Assert.Equal(4, audit.Count(a => a.Action == AuditActions.LabExpirationExtended));
    }

    private async Task<Guid> ReadyLabAsync()
    {
        var created = await _db.Service().CreateAsync(new CreateLabRequest("ready-lab", "eastus", 2), null, _ct);
        await FinishActiveJobsAsync();
        _db.SetState(created.Lab.Id, LabState.Ready);
        return created.Lab.Id;
    }

    private async Task FinishActiveJobsAsync()
    {
        await using var ctx = _db.Context();
        await ctx.Jobs.Where(j => j.Status == LabJobStatus.Queued || j.Status == LabJobStatus.Running)
            .ExecuteUpdateAsync(s => s.SetProperty(j => j.Status, LabJobStatus.Succeeded).SetProperty(j => j.MutexKey, (string?)null), _ct);
    }
}
