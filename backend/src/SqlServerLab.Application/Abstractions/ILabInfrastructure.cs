using SqlServerLab.Domain.Jobs;
using SqlServerLab.Domain.Labs;

namespace SqlServerLab.Application.Abstractions;

public enum InfraOperationKind
{
    Provision,
    Start,
    Deallocate,
    Delete,
}

public enum InfraOperationStatus
{
    Running,
    Succeeded,
    Failed,
}

/// <param name="OperationId">Durable identifier (Azure deployment/operation ID or simulated ID).</param>
public sealed record InfraOperation(
    string OperationId,
    InfraOperationKind Kind,
    InfraOperationStatus Status,
    int Progress,
    string? ResourceId = null,
    ErrorCategory? FailureCategory = null,
    string? FailureMessage = null);

/// <param name="OperationKey">Stable key (the job ID); beginning the same key twice returns the same operation.</param>
public sealed record InfraRequest(Lab Lab, Guid OperationKey, int Attempt);

/// <summary>
/// Lab VM lifecycle. Every call is idempotent per <see cref="InfraRequest.OperationKey"/> and returns a durable
/// operation that can be polled after a process restart.
/// </summary>
public interface ILabInfrastructure
{
    bool IsSimulated { get; }

    Task<InfraOperation> BeginAsync(InfraOperationKind kind, InfraRequest request, CancellationToken cancellationToken);

    Task<InfraOperation?> GetOperationAsync(string operationId, CancellationToken cancellationToken);

    Task<VmPowerState> GetPowerStateAsync(Lab lab, CancellationToken cancellationToken);

    /// <summary>SQL readiness health check (service up, lab login usable).</summary>
    Task<bool> IsSqlReadyAsync(Lab lab, CancellationToken cancellationToken);
}
