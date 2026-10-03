using SqlServerLab.Domain.Jobs;

namespace SqlServerLab.Application.Jobs;

public sealed record JobDto(
    Guid JobId,
    Guid LabId,
    LabJobType Type,
    LabJobStatus Status,
    int Progress,
    string? CurrentStep,
    int AttemptCount,
    int MaxAttempts,
    string RequestedBy,
    DateTimeOffset RequestedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    DateTimeOffset UpdatedAt,
    bool CancelRequested,
    ErrorCategory? ErrorCategory,
    string? ErrorCode,
    string? ErrorMessage,
    string StatusUrl)
{
    public static JobDto From(LabJob job) => new(
        job.Id,
        job.LabId,
        job.Type,
        job.Status,
        job.Progress,
        job.CurrentStep,
        job.AttemptCount,
        job.MaxAttempts,
        job.RequestedBy,
        job.RequestedAt,
        job.StartedAt,
        job.CompletedAt,
        job.UpdatedAt,
        job.CancelRequested,
        job.ErrorCategory,
        job.ErrorCode,
        job.ErrorMessage,
        $"/api/v1/jobs/{job.Id}");
}
