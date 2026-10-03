namespace SqlServerLab.Domain.Records;

/// <summary>Result of one experiment execution. Populated from Milestone 5 onward.</summary>
public sealed class ExperimentRun
{
    public Guid Id { get; set; }
    public Guid LabId { get; set; }
    public required string ScenarioId { get; set; }
    public int ScenarioVersion { get; set; }
    public required string Status { get; set; }
    public string? ConfigurationState { get; set; }
    public string? ParametersJson { get; set; }
    public string? WarmupPolicy { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? EndedAt { get; set; }
    public int MeasurementCount { get; set; }
    public double? P50DurationMs { get; set; }
    public double? P95DurationMs { get; set; }
    public double? CpuMs { get; set; }
    public long? LogicalReads { get; set; }
    public long? PhysicalReads { get; set; }
    public long? RowCount { get; set; }
    public byte[]? PlanXmlCompressed { get; set; }
    public string? PlanSummaryJson { get; set; }
    public string? WaitSummaryJson { get; set; }
    public string? FailureMessage { get; set; }
}
