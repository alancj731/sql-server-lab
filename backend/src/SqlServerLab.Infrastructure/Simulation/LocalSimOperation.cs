using SqlServerLab.Application.Abstractions;
using SqlServerLab.Domain.Jobs;

namespace SqlServerLab.Infrastructure.Simulation;

/// <summary>Persisted simulated infrastructure operation; stands in for an Azure long-running operation.</summary>
public sealed class LocalSimOperation
{
    public required string Id { get; set; }
    public Guid LabId { get; set; }
    public InfraOperationKind Kind { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public double DurationSeconds { get; set; }
    public bool WillFail { get; set; }
    public ErrorCategory? FailureCategory { get; set; }
    public string? FailureMessage { get; set; }
    public string? ResourceId { get; set; }
}
