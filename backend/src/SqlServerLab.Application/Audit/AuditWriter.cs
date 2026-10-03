using SqlServerLab.Application.Abstractions;
using SqlServerLab.Application.Errors;
using SqlServerLab.Domain.Audit;

namespace SqlServerLab.Application.Audit;

/// <summary>Adds audit events to the current unit of work; they commit with the change they describe.</summary>
public static class AuditWriter
{
    public static AuditEvent Add(
        IControlDb db,
        DateTimeOffset now,
        string actorId,
        string action,
        string correlationId,
        Guid? labId = null,
        Guid? jobId = null,
        string? detail = null)
    {
        var audit = new AuditEvent
        {
            Id = Guid.NewGuid(),
            LabId = labId,
            JobId = jobId,
            ActorId = actorId,
            Action = action,
            Detail = Redactor.Sanitize(detail),
            CorrelationId = correlationId,
            OccurredAt = now,
        };
        db.AuditEvents.Add(audit);
        return audit;
    }
}
