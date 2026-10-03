using SqlServerLab.Domain.Audit;

namespace SqlServerLab.Application.Audit;

public sealed record AuditEventDto(
    Guid Id,
    Guid? LabId,
    Guid? JobId,
    string ActorId,
    string Action,
    string? Detail,
    string CorrelationId,
    DateTimeOffset OccurredAt)
{
    public static AuditEventDto From(AuditEvent e) =>
        new(e.Id, e.LabId, e.JobId, e.ActorId, e.Action, e.Detail, e.CorrelationId, e.OccurredAt);
}
