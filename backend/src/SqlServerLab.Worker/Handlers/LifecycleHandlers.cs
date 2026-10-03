using SqlServerLab.Application.Abstractions;
using SqlServerLab.Application.Errors;
using SqlServerLab.Domain.Audit;
using SqlServerLab.Domain.Jobs;
using SqlServerLab.Domain.Labs;
using SqlServerLab.Worker.Jobs;

namespace SqlServerLab.Worker.Handlers;

public sealed class ProvisionLabHandler : IJobHandler
{
    public LabJobType Type => LabJobType.ProvisionLab;

    public async Task ExecuteAsync(JobContext context, CancellationToken cancellationToken)
    {
        var lab = await context.LoadLabAsync(cancellationToken);
        if (lab.State == LabState.Ready)
        {
            return; // Completed before a restart; nothing left to do.
        }

        if (lab.State is LabState.Requested)
        {
            await context.TransitionAsync(LabState.Provisioning, null, cancellationToken);
        }

        if (lab.State is LabState.Requested or LabState.Provisioning)
        {
            await context.ReportAsync(5, "Submitting deployment", cancellationToken);
            var opId = await context.EnsureOperationAsync(InfraOperationKind.Provision, cancellationToken);
            var op = await context.WaitForOperationAsync(opId, 10, 80, "Deploying VM and SQL Server", cancellationToken);
            await context.UpdateLabAsync(l =>
            {
                if (l.VmResourceId == op.ResourceId)
                {
                    return null;
                }

                l.VmResourceId = op.ResourceId;
                return "VM resource recorded";
            }, AuditActions.LabStateChanged, cancellationToken);
            await context.TransitionAsync(LabState.Configuring, null, cancellationToken);
        }

        await context.WaitForSqlReadyAsync(85, cancellationToken);
        await context.TransitionAsync(LabState.Ready, null, cancellationToken);
    }
}

public sealed class StartVmHandler : IJobHandler
{
    public LabJobType Type => LabJobType.StartVm;

    public async Task ExecuteAsync(JobContext context, CancellationToken cancellationToken)
    {
        var lab = await context.LoadLabAsync(cancellationToken);
        if (lab.State == LabState.Ready)
        {
            return;
        }

        if (lab.State == LabState.Stopped)
        {
            await context.TransitionAsync(LabState.Starting, null, cancellationToken);
        }

        if (lab.State is LabState.Stopped or LabState.Starting)
        {
            var opId = await context.EnsureOperationAsync(InfraOperationKind.Start, cancellationToken);
            await context.WaitForOperationAsync(opId, 5, 70, "Starting VM", cancellationToken);
            await context.TransitionAsync(LabState.Configuring, null, cancellationToken);
        }

        await context.WaitForSqlReadyAsync(80, cancellationToken);
        await context.TransitionAsync(LabState.Ready, null, cancellationToken);
    }
}

public sealed class DeallocateVmHandler : IJobHandler
{
    public LabJobType Type => LabJobType.DeallocateVm;

    public async Task ExecuteAsync(JobContext context, CancellationToken cancellationToken)
    {
        var lab = await context.LoadLabAsync(cancellationToken);
        if (lab.State == LabState.Stopped)
        {
            return;
        }

        await context.TransitionAsync(LabState.Deallocating, null, cancellationToken);
        var opId = await context.EnsureOperationAsync(InfraOperationKind.Deallocate, cancellationToken);
        await context.WaitForOperationAsync(opId, 5, 95, "Deallocating VM", cancellationToken);
        await context.TransitionAsync(LabState.Stopped, "Deallocated; compute is not billed while stopped.", cancellationToken);
    }
}

public sealed class DeleteLabHandler : IJobHandler
{
    public LabJobType Type => LabJobType.DeleteLab;

    public async Task ExecuteAsync(JobContext context, CancellationToken cancellationToken)
    {
        var lab = await context.LoadLabAsync(cancellationToken);
        if (lab.State == LabState.Deleted)
        {
            return;
        }

        await context.TransitionAsync(LabState.Deleting, null, cancellationToken);
        var opId = await context.EnsureOperationAsync(InfraOperationKind.Delete, cancellationToken);
        await context.WaitForOperationAsync(opId, 5, 95, "Deleting lab resource group", cancellationToken);
        await context.TransitionAsync(LabState.Deleted, null, cancellationToken);
    }
}

/// <summary>Compares the control-plane state with the observed infrastructure state and adopts the observation.</summary>
public sealed class ReconcileLabHandler : IJobHandler
{
    public LabJobType Type => LabJobType.ReconcileLab;

    public async Task ExecuteAsync(JobContext context, CancellationToken cancellationToken)
    {
        await context.ReportAsync(10, "Reading infrastructure state", cancellationToken);
        var lab = await context.LoadLabAsync(cancellationToken);
        if (lab.State == LabState.Deleted)
        {
            return;
        }

        var power = await context.Infrastructure.GetPowerStateAsync(lab, cancellationToken);
        for (var i = 0; power is VmPowerState.Starting or VmPowerState.Deallocating && i < 600; i++)
        {
            await Task.Delay(250, cancellationToken);
            power = await context.Infrastructure.GetPowerStateAsync(lab, cancellationToken);
        }

        lab = await context.LoadLabAsync(cancellationToken);
        LabState? observed = power switch
        {
            VmPowerState.Running => LabState.Ready,
            VmPowerState.Deallocated => LabState.Stopped,
            VmPowerState.Deleted => LabState.Deleted,
            VmPowerState.NotCreated when lab.State == LabState.Deleting => LabState.Deleted,
            VmPowerState.NotCreated => LabState.Failed,
            _ => null,
        };

        if (observed is null)
        {
            throw new JobExecutionException(ErrorCategory.Transient, "PowerStateUnknown", $"Observed power state {power} could not be reconciled yet.");
        }

        await context.UpdateLabAsync(l =>
        {
            if (l.State == observed)
            {
                return null;
            }

            var from = l.State;
            var reason = observed == LabState.Failed ? "No VM exists for this lab; delete it and create a new one." : $"Reconciled from observed power state {power}.";
            LabStateMachine.Reconcile(l, observed.Value, reason, context.Clock.UtcNow);
            return $"{from} -> {observed} (observed {power})";
        }, AuditActions.LabReconciled, cancellationToken);
    }
}

/// <summary>Job types whose implementation belongs to a later milestone fail fast with a validation error.</summary>
public sealed class NotYetAvailableHandler(LabJobType type) : IJobHandler
{
    public LabJobType Type { get; } = type;

    public Task ExecuteAsync(JobContext context, CancellationToken cancellationToken) =>
        throw new JobExecutionException(ErrorCategory.Validation, "NotAvailable", $"{Type} is not available in this release.");
}
