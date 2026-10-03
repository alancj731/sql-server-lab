using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SqlServerLab.Application.Abstractions;
using SqlServerLab.Application.Errors;
using SqlServerLab.Domain.Jobs;
using SqlServerLab.Domain.Labs;
using SqlServerLab.Infrastructure.Options;
using SqlServerLab.Infrastructure.Persistence;

namespace SqlServerLab.Infrastructure.Simulation;

/// <summary>
/// Simulates lab VM lifecycle without Azure. Operations are persisted so they survive worker restarts, exactly like
/// polling an Azure long-running operation. Fault injection is deterministic by lab name:
/// names containing "fail" fail provisioning permanently; names containing "flaky" fail the first attempt transiently.
/// </summary>
public sealed class LocalLabInfrastructure(
    IDbContextFactory<ControlDbContext> contextFactory,
    IClock clock,
    IOptions<LocalSimulationOptions> options) : ILabInfrastructure
{
    public bool IsSimulated => true;

    public async Task<InfraOperation> BeginAsync(InfraOperationKind kind, InfraRequest request, CancellationToken cancellationToken)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var id = $"sim-{kind.ToString().ToLowerInvariant()}-{request.OperationKey:N}";
        var existing = await db.LocalSimOperations.AsNoTracking().FirstOrDefaultAsync(o => o.Id == id, cancellationToken);
        if (existing is not null)
        {
            return ToOperation(existing);
        }

        var name = request.Lab.Name;
        if (kind == InfraOperationKind.Provision && name.Contains("flaky", StringComparison.Ordinal) && request.Attempt <= 1)
        {
            throw new JobExecutionException(ErrorCategory.Transient, "SimulatedThrottle", "Simulated transient error: the provider is throttling requests.");
        }

        var willFail = kind == InfraOperationKind.Provision && name.Contains("fail", StringComparison.Ordinal);
        var op = new LocalSimOperation
        {
            Id = id,
            LabId = request.Lab.Id,
            Kind = kind,
            StartedAt = clock.UtcNow,
            DurationSeconds = Duration(kind),
            WillFail = willFail,
            FailureCategory = willFail ? ErrorCategory.Permanent : null,
            FailureMessage = willFail ? "Simulated permanent failure: the requested VM image is unavailable in this region." : null,
            ResourceId = kind == InfraOperationKind.Provision
                ? $"/subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/{request.Lab.ResourceGroupName}/providers/Microsoft.Compute/virtualMachines/vm-{name}"
                : null,
        };
        db.LocalSimOperations.Add(op);
        await db.SaveChangesAsync(cancellationToken);
        return ToOperation(op);
    }

    public async Task<InfraOperation?> GetOperationAsync(string operationId, CancellationToken cancellationToken)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var op = await db.LocalSimOperations.AsNoTracking().FirstOrDefaultAsync(o => o.Id == operationId, cancellationToken);
        return op is null ? null : ToOperation(op);
    }

    public async Task<VmPowerState> GetPowerStateAsync(Lab lab, CancellationToken cancellationToken)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var ops = await db.LocalSimOperations.AsNoTracking().Where(o => o.LabId == lab.Id).ToListAsync(cancellationToken);
        var now = clock.UtcNow;
        var state = VmPowerState.NotCreated;
        foreach (var op in ops.OrderBy(o => o.StartedAt))
        {
            var done = IsComplete(op, now);
            if (op.WillFail)
            {
                continue;
            }

            state = (op.Kind, done) switch
            {
                (InfraOperationKind.Provision, true) or (InfraOperationKind.Start, true) => VmPowerState.Running,
                (InfraOperationKind.Provision, false) => VmPowerState.NotCreated,
                (InfraOperationKind.Start, false) => VmPowerState.Starting,
                (InfraOperationKind.Deallocate, true) => VmPowerState.Deallocated,
                (InfraOperationKind.Deallocate, false) => VmPowerState.Deallocating,
                (InfraOperationKind.Delete, true) => VmPowerState.Deleted,
                (InfraOperationKind.Delete, false) => state,
                _ => state,
            };
        }

        return state;
    }

    public async Task<bool> IsSqlReadyAsync(Lab lab, CancellationToken cancellationToken)
    {
        if (await GetPowerStateAsync(lab, cancellationToken) != VmPowerState.Running)
        {
            return false;
        }

        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var ops = await db.LocalSimOperations.AsNoTracking().Where(o => o.LabId == lab.Id).ToListAsync(cancellationToken);
        var last = ops.MaxBy(o => o.StartedAt);
        if (last is null)
        {
            return false;
        }

        var readyAt = last.StartedAt.AddSeconds(last.DurationSeconds + options.Value.SqlReadySeconds);
        return clock.UtcNow >= readyAt;
    }

    private double Duration(InfraOperationKind kind) => kind switch
    {
        InfraOperationKind.Provision => options.Value.ProvisionSeconds,
        InfraOperationKind.Start => options.Value.StartSeconds,
        InfraOperationKind.Deallocate => options.Value.DeallocateSeconds,
        InfraOperationKind.Delete => options.Value.DeleteSeconds,
        _ => 0,
    };

    private static bool IsComplete(LocalSimOperation op, DateTimeOffset now) => now >= op.StartedAt.AddSeconds(op.DurationSeconds);

    private InfraOperation ToOperation(LocalSimOperation op)
    {
        var now = clock.UtcNow;
        if (!IsComplete(op, now))
        {
            var elapsed = (now - op.StartedAt).TotalSeconds;
            var progress = op.DurationSeconds <= 0 ? 99 : (int)Math.Clamp(elapsed / op.DurationSeconds * 100, 0, 99);
            return new InfraOperation(op.Id, op.Kind, InfraOperationStatus.Running, progress);
        }

        return op.WillFail
            ? new InfraOperation(op.Id, op.Kind, InfraOperationStatus.Failed, 100, null, op.FailureCategory, op.FailureMessage)
            : new InfraOperation(op.Id, op.Kind, InfraOperationStatus.Succeeded, 100, op.ResourceId);
    }
}
