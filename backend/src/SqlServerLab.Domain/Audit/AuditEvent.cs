namespace SqlServerLab.Domain.Audit;

/// <summary>Append-only record of a security-relevant or state-changing action.</summary>
public sealed class AuditEvent
{
    public Guid Id { get; set; }
    public Guid? LabId { get; set; }
    public Guid? JobId { get; set; }
    public required string ActorId { get; set; }
    public required string Action { get; set; }
    public string? Detail { get; set; }
    public required string CorrelationId { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
}
