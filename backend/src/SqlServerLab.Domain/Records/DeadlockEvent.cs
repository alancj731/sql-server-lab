namespace SqlServerLab.Domain.Records;

/// <summary>A captured deadlock graph. Populated from Milestone 6 onward.</summary>
public sealed class DeadlockEvent
{
    public Guid Id { get; set; }
    public Guid LabId { get; set; }
    public Guid? ExperimentRunId { get; set; }
    public DateTimeOffset CapturedAt { get; set; }
    public string? VictimProcessId { get; set; }
    public byte[]? RawXmlCompressed { get; set; }
    public string? ParsedJson { get; set; }
}
