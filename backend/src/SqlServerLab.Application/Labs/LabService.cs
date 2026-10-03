using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SqlServerLab.Application.Abstractions;
using SqlServerLab.Application.Audit;
using SqlServerLab.Application.Errors;
using SqlServerLab.Application.Jobs;
using SqlServerLab.Application.Options;
using SqlServerLab.Domain.Audit;
using SqlServerLab.Domain.Jobs;
using SqlServerLab.Domain.Labs;
using SqlServerLab.Domain.Policies;

namespace SqlServerLab.Application.Labs;

/// <summary>User-facing lab use cases. Every lookup is scoped to the caller; other users' labs are reported as not found.</summary>
public sealed class LabService(
    IControlDb db,
    IClock clock,
    ICurrentUser user,
    ICorrelationContext correlation,
    ILabInfrastructure infrastructure,
    JobService jobs,
    IOptions<LabLimitsOptions> limits)
{
    public async Task<IReadOnlyList<LabDto>> ListAsync(CancellationToken cancellationToken)
    {
        var labs = await OwnedLabs()
            .Where(l => l.State != LabState.Deleted)
            .ToListAsync(cancellationToken);
        var ids = labs.Select(l => l.Id).ToList();
        var activeJobs = await db.Jobs.AsNoTracking()
            .Where(j => ids.Contains(j.LabId) && (j.Status == LabJobStatus.Queued || j.Status == LabJobStatus.Running))
            .ToListAsync(cancellationToken);
        return labs
            .OrderByDescending(l => l.CreatedAt)
            .Select(l => ToDto(l, CurrentJob(activeJobs.Where(j => j.LabId == l.Id))))
            .ToList();
    }

    public async Task<LabDto> GetAsync(Guid labId, CancellationToken cancellationToken)
    {
        var lab = await FindOwnedAsync(labId, cancellationToken);
        return await ToDtoAsync(db, lab, cancellationToken);
    }

    public async Task<CreateLabResponse> CreateAsync(CreateLabRequest request, string? idempotencyKey, CancellationToken cancellationToken)
    {
        Validate(request, limits.Value.EffectiveRegions());

        var scopedKey = idempotencyKey is null ? null : $"{user.UserId}:create:{idempotencyKey}";
        if (scopedKey is not null)
        {
            var replay = await db.Jobs.AsNoTracking().FirstOrDefaultAsync(j => j.IdempotencyKey == scopedKey, cancellationToken);
            if (replay is not null)
            {
                var replayLab = await FindOwnedAsync(replay.LabId, cancellationToken);
                return new CreateLabResponse(await ToDtoAsync(db, replayLab, cancellationToken), JobDto.From(replay));
            }
        }

        var sameName = await db.Labs.AsNoTracking()
            .FirstOrDefaultAsync(l => l.OwnerId == user.UserId && l.Name == request.Name && l.State != LabState.Deleted, cancellationToken);
        if (sameName is not null)
        {
            // A repeated submit while provisioning is still queued/running returns the original job.
            var pending = await db.Jobs.AsNoTracking().FirstOrDefaultAsync(
                j => j.LabId == sameName.Id && j.Type == LabJobType.ProvisionLab
                     && (j.Status == LabJobStatus.Queued || j.Status == LabJobStatus.Running),
                cancellationToken);
            if (pending is not null)
            {
                return new CreateLabResponse(await ToDtoAsync(db, sameName, cancellationToken), JobDto.From(pending));
            }

            throw new ConflictException($"You already have a lab named '{request.Name}'.");
        }

        var active = await db.Labs.CountAsync(l => l.OwnerId == user.UserId && l.State != LabState.Deleted, cancellationToken);
        if (active >= limits.Value.MaxActiveLabsPerUser)
        {
            throw new QuotaExceededException(
                $"You can have at most {limits.Value.MaxActiveLabsPerUser} labs. Delete a lab before creating another.");
        }

        var now = clock.UtcNow;
        var id = Guid.NewGuid();
        var lab = new Lab
        {
            Id = id,
            OwnerId = user.UserId,
            OwnerName = user.DisplayName,
            Name = request.Name,
            Region = request.Region,
            ResourceGroupName = ResourceNaming.ResourceGroupName(request.Name, id),
            State = LabState.Requested,
            IsSimulated = infrastructure.IsSimulated,
            CreatedAt = now,
            UpdatedAt = now,
            ExpiresAt = now.AddHours(request.TtlHours),
            RowVersion = Guid.NewGuid(),
        };
        db.Labs.Add(lab);
        AuditWriter.Add(db, now, user.UserId, AuditActions.LabRequested, correlation.CorrelationId, lab.Id,
            detail: $"name={lab.Name} region={lab.Region} ttlHours={request.TtlHours}");
        var job = jobs.Stage(lab, LabJobType.ProvisionLab, user.UserId, correlation.CorrelationId, scopedKey);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            db.ResetTracking();
            throw new ConflictException("The lab could not be created because of a concurrent request. Try again.");
        }

        return new CreateLabResponse(ToDto(lab, job), JobDto.From(job));
    }

    public Task<JobDto> StartAsync(Guid labId, string? idempotencyKey, CancellationToken cancellationToken) =>
        EnqueueCommandAsync(labId, LabCommand.Start, LabJobType.StartVm, idempotencyKey, cancellationToken);

    public Task<JobDto> DeallocateAsync(Guid labId, string? idempotencyKey, CancellationToken cancellationToken) =>
        EnqueueCommandAsync(labId, LabCommand.Deallocate, LabJobType.DeallocateVm, idempotencyKey, cancellationToken);

    public async Task<JobDto> DeleteAsync(Guid labId, DeleteLabRequest request, string? idempotencyKey, CancellationToken cancellationToken)
    {
        var lab = await FindOwnedAsync(labId, cancellationToken);
        if (!string.Equals(request.ConfirmName, lab.Name, StringComparison.Ordinal))
        {
            throw ValidationFailedException.For(nameof(request.ConfirmName), "Type the lab name exactly to confirm deletion.");
        }

        return await EnqueueCommandAsync(labId, LabCommand.Delete, LabJobType.DeleteLab, idempotencyKey, cancellationToken);
    }

    public async Task<LabDto> ExtendExpirationAsync(Guid labId, ExtendExpirationRequest request, CancellationToken cancellationToken)
    {
        if (!ExpiryPolicy.IsValidExtension(request.Hours))
        {
            throw ValidationFailedException.For(nameof(request.Hours), $"Extend by 1 to {ExpiryPolicy.MaxExtensionHours} hours.");
        }

        var lab = await FindOwnedAsync(labId, cancellationToken, tracked: true);
        if (!LabStateMachine.IsAllowed(lab.State, LabCommand.ExtendExpiration))
        {
            throw new ConflictException($"The expiration cannot be extended while the lab is {lab.State}.");
        }

        var now = clock.UtcNow;
        var newExpiry = ExpiryPolicy.Extend(lab.CreatedAt, lab.ExpiresAt, now, request.Hours);
        if (newExpiry <= lab.ExpiresAt)
        {
            throw new ConflictException("The lab has reached its maximum lifetime and cannot be extended further.");
        }

        var previous = lab.ExpiresAt;
        lab.ExpiresAt = newExpiry;
        lab.UpdatedAt = now;
        AuditWriter.Add(db, now, user.UserId, AuditActions.LabExpirationExtended, correlation.CorrelationId, lab.Id,
            detail: $"from={previous:O} to={newExpiry:O}");
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            db.ResetTracking();
            throw new ConflictException("The lab changed while extending its expiration. Refresh and try again.");
        }

        return await ToDtoAsync(db, lab, cancellationToken);
    }

    public async Task<IReadOnlyList<JobDto>> ListJobsAsync(Guid labId, CancellationToken cancellationToken)
    {
        await FindOwnedAsync(labId, cancellationToken);
        var list = await db.Jobs.AsNoTracking().Where(j => j.LabId == labId).ToListAsync(cancellationToken);
        return list.OrderByDescending(j => j.RequestedAt).Take(100).Select(JobDto.From).ToList();
    }

    public async Task<IReadOnlyList<AuditEventDto>> ListAuditAsync(Guid labId, CancellationToken cancellationToken)
    {
        await FindOwnedAsync(labId, cancellationToken);
        var list = await db.AuditEvents.AsNoTracking().Where(a => a.LabId == labId).ToListAsync(cancellationToken);
        return list.OrderByDescending(a => a.OccurredAt).Take(500).Select(AuditEventDto.From).ToList();
    }

    public async Task<JobDto> GetJobAsync(Guid jobId, CancellationToken cancellationToken)
    {
        var job = await db.Jobs.AsNoTracking().FirstOrDefaultAsync(j => j.Id == jobId, cancellationToken)
                  ?? throw new NotFoundException("Job");
        await FindOwnedAsync(job.LabId, cancellationToken, notFoundResource: "Job");
        return JobDto.From(job);
    }

    public async Task<JobDto> CancelJobAsync(Guid jobId, CancellationToken cancellationToken)
    {
        var job = await db.Jobs.FirstOrDefaultAsync(j => j.Id == jobId, cancellationToken) ?? throw new NotFoundException("Job");
        var lab = await FindOwnedAsync(job.LabId, cancellationToken, tracked: true, notFoundResource: "Job");
        var now = clock.UtcNow;

        switch (job.Status)
        {
            case LabJobStatus.Queued:
                job.Status = LabJobStatus.Cancelled;
                job.CompletedAt = now;
                job.MutexKey = null;
                job.CurrentStep = "Cancelled before start";
                job.ErrorCategory = ErrorCategory.Cancellation;
                job.UpdatedAt = now;
                lab.Touch(now); // new RowVersion, so the next identical request gets a fresh job
                AuditWriter.Add(db, now, user.UserId, AuditActions.JobCancelled, correlation.CorrelationId, lab.Id, job.Id);
                break;
            case LabJobStatus.Running when !job.CancelRequested:
                job.CancelRequested = true;
                job.UpdatedAt = now;
                AuditWriter.Add(db, now, user.UserId, AuditActions.JobCancelRequested, correlation.CorrelationId, lab.Id, job.Id);
                break;
            case LabJobStatus.Running:
                break;
            default:
                throw new ConflictException($"The job has already finished ({job.Status}).");
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            db.ResetTracking();
            throw new ConflictException("The job changed while cancelling. Refresh and try again.");
        }

        return JobDto.From(job);
    }

    /// <summary>Returns true when the caller may see the lab (used by the SignalR hub).</summary>
    public async Task<bool> CanAccessAsync(Guid labId, CancellationToken cancellationToken) =>
        await OwnedLabs().AnyAsync(l => l.Id == labId, cancellationToken);

    public static async Task<LabDto> ToDtoAsync(IControlDb db, Lab lab, CancellationToken cancellationToken)
    {
        var active = await db.Jobs.AsNoTracking()
            .Where(j => j.LabId == lab.Id && (j.Status == LabJobStatus.Queued || j.Status == LabJobStatus.Running))
            .ToListAsync(cancellationToken);
        return ToDto(lab, CurrentJob(active));
    }

    public static LabDto ToDto(Lab lab, LabJob? currentJob)
    {
        var busy = currentJob is not null && LabJobPolicy.IsMutating(currentJob.Type);
        var allowed = LabStateMachine.AllowedCommands(lab.State)
            .Where(c => c == LabCommand.ExtendExpiration || !busy)
            .Order()
            .ToList();
        return new LabDto(
            lab.Id,
            lab.Name,
            lab.OwnerId,
            lab.OwnerName ?? lab.OwnerId,
            lab.Region,
            lab.ResourceGroupName,
            lab.State,
            lab.StateReason,
            LabStateMachine.ImpliedPowerState(lab.State),
            lab.IsSimulated,
            lab.CreatedAt,
            lab.UpdatedAt,
            lab.ExpiresAt,
            currentJob is null ? null : JobDto.From(currentJob),
            allowed);
    }

    private static LabJob? CurrentJob(IEnumerable<LabJob> active) =>
        active.OrderByDescending(j => j.Status == LabJobStatus.Running).ThenBy(j => j.RequestedAt).FirstOrDefault();

    private async Task<JobDto> EnqueueCommandAsync(
        Guid labId, LabCommand command, LabJobType type, string? idempotencyKey, CancellationToken cancellationToken)
    {
        var lab = await FindOwnedAsync(labId, cancellationToken);
        var scopedKey = idempotencyKey is null ? null : $"{user.UserId}:{type}:{idempotencyKey}";

        // A repeat of an accepted request returns the original job even though the lab state has moved on.
        var key = scopedKey ?? JobService.DeterministicKey(lab, type);
        var replay = await db.Jobs.AsNoTracking().FirstOrDefaultAsync(j => j.IdempotencyKey == key, cancellationToken);
        if (replay is not null)
        {
            return JobDto.From(replay);
        }

        if (!LabStateMachine.IsAllowed(lab.State, command))
        {
            throw new ConflictException($"{command} is not allowed while the lab is {lab.State}.");
        }

        var result = await jobs.EnqueueAsync(lab, type, user.UserId, correlation.CorrelationId, key, cancellationToken);
        return JobDto.From(result.Job);
    }

    private IQueryable<Lab> OwnedLabs()
    {
        var query = db.Labs.AsNoTracking();
        return user.IsAdmin ? query : query.Where(l => l.OwnerId == user.UserId);
    }

    private async Task<Lab> FindOwnedAsync(Guid labId, CancellationToken cancellationToken, bool tracked = false, string notFoundResource = "Lab")
    {
        var query = tracked ? db.Labs.AsQueryable() : db.Labs.AsNoTracking();
        if (!user.IsAdmin)
        {
            query = query.Where(l => l.OwnerId == user.UserId);
        }

        return await query.FirstOrDefaultAsync(l => l.Id == labId, cancellationToken) ?? throw new NotFoundException(notFoundResource);
    }

    private static void Validate(CreateLabRequest request, IReadOnlyList<string> regions)
    {
        var errors = new Dictionary<string, string[]>();
        if (!LabNamePolicy.IsValid(request.Name))
        {
            errors[nameof(request.Name)] = [LabNamePolicy.Description];
        }

        if (!RegionAllowList.IsAllowed(request.Region) || !regions.Contains(request.Region))
        {
            errors[nameof(request.Region)] = [$"Region must be one of: {string.Join(", ", regions)}."];
        }

        if (!ExpiryPolicy.IsValidInitialTtl(request.TtlHours))
        {
            errors[nameof(request.TtlHours)] = [$"TTL must be between {ExpiryPolicy.MinInitialHours} and {ExpiryPolicy.MaxInitialHours} hours."];
        }

        if (errors.Count > 0)
        {
            throw new ValidationFailedException(errors);
        }
    }
}
