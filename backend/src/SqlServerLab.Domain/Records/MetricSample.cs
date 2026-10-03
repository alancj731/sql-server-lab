namespace SqlServerLab.Domain.Records;

/// <summary>One telemetry sample. Populated from Milestone 7 onward.</summary>
public sealed class MetricSample
{
    public long Id { get; set; }
    public Guid LabId { get; set; }
    public required string Source { get; set; }
    public required string Name { get; set; }
    public required string Unit { get; set; }
    public int ResolutionSeconds { get; set; }
    public double Value { get; set; }
    public double? RawCounterValue { get; set; }
    public DateTimeOffset SampledAt { get; set; }
}
