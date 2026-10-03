using Microsoft.EntityFrameworkCore;
using SqlServerLab.Application.Abstractions;
using SqlServerLab.Application.Audit;
using SqlServerLab.Application.Errors;
using SqlServerLab.Domain.Audit;
using SqlServerLab.Domain.Jobs;
using SqlServerLab.Domain.Labs;

namespace SqlServerLab.Application.Jobs;

public sealed record EnqueueResult(LabJob Job, bool Created);

public sealed class JobService(IControlDb db, IClock clock)
{
    public static string DeterministicKey(Lab lab, LabJobType type) => $"{lab.Id:N}:{type}:{lab.RowVersion:N}";

    /// <summary>
    /// Stages a job (without saving) so it can commit together with other changes. Idempotency and mutual exclusion
    /// are enforced by unique indexes on <see cref="LabJob.IdempotencyKey"/> and <see cref="LabJob.MutexKey"/>.
    /// </summary>
    public LabJob Stage(Lab lab, LabJobType type, string actorId, string correlationId, string? idempotencyKey = null, string? inputJson = null)
    {
        var now = clock.UtcNow;
        var job = new LabJob
        {
            Id = Guid.NewGuid(),
            LabId = lab.Id,
            Type = type,
            Status = LabJobStatus.Queued,
            InputJson = inputJson,
            MaxAttempts = LabJobPolicy.MaxAttempts(type),
            CorrelationId = correlationId,
            RequestedBy = actorId,
            RequestedAt = now,
            UpdatedAt = now,
            NotBefore = now,
            IdempotencyKey = idempotencyKey ?? DeterministicKey(lab, type),
            MutexKey = LabJobPolicy.IsMutating(type) ? lab.Id.ToString("N") : null,
            CurrentStep = "Queued",
        };
        db.Jobs.Add(job);
        AuditWriter.Add(db, now, actorId, AuditActions.JobEnqueued, correlationId, lab.Id, job.Id, type.ToString());
        return job;
    }

    /// <summary>Enqueues a job, returning the existing job for a repeated idempotency key.</summary>
    public async Task<EnqueueResult> EnqueueAsync(
        Lab lab,
        LabJobType type,
        string actorId,
        string correlationId,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default)
    {
        var key = idempotencyKey ?? DeterministicKey(lab, type);
        var existing = await db.Jobs.AsNoTracking().FirstOrDefaultAsync(j => j.IdempotencyKey == key, cancellationToken);
        if (existing is not null)
        {
            return new EnqueueResult(existing, false);
        }

        await EnsureNoActiveMutatingJobAsync(lab.Id, type, cancellationToken);
        var job = Stage(lab, type, actorId, correlationId, key);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return new EnqueueResult(job, true);
        }
        catch (DbUpdateException)
        {
            // Lost a race: either the same request (idempotency key) or a conflicting mutation (mutex key).
            db.ResetTracking();
            existing = await db.Jobs.AsNoTracking().FirstOrDefaultAsync(j => j.IdempotencyKey == key, cancellationToken);
            if (existing is not null)
            {
                return new EnqueueResult(existing, false);
            }

            throw new ConflictException("Another operation is already in progress for this lab.");
        }
    }

    public async Task EnsureNoActiveMutatingJobAsync(Guid labId, LabJobType type, CancellationToken cancellationToken)
    {
        if (!LabJobPolicy.IsMutating(type))
        {
            return;
        }

        var mutex = labId.ToString("N");
        if (await db.Jobs.AnyAsync(j => j.MutexKey == mutex, cancellationToken))
        {
            throw new ConflictException("Another operation is already in progress for this lab.");
        }
    }
}
