namespace SqlServerLab.Domain.Records;

/// <summary>Result of an Update Manager assessment. Populated from Milestone 9 onward.</summary>
public sealed class PatchAssessment
{
    public Guid Id { get; set; }
    public Guid LabId { get; set; }
    public DateTimeOffset AssessedAt { get; set; }
    public required string Status { get; set; }
    public string? UpdatesJson { get; set; }
}
