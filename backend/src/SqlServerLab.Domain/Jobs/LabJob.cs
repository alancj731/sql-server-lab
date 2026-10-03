namespace SqlServerLab.Domain.Jobs;

public sealed class LabJob
{
    public Guid Id { get; set; }
    public Guid LabId { get; set; }
    public LabJobType Type { get; set; }
    public LabJobStatus Status { get; set; }

    /// <summary>Serialized, validated parameters. Never code, SQL, or scripts.</summary>
    public string? InputJson { get; set; }

    public int Progress { get; set; }
    public string? CurrentStep { get; set; }
    public int AttemptCount { get; set; }
    public int MaxAttempts { get; set; }
    /// <summary>Unique token of the current claim; every worker write is conditioned on it.</summary>
    public string? LeaseOwner { get; set; }
    public DateTimeOffset? LeaseExpiresAt { get; set; }
    public required string CorrelationId { get; set; }
    public required string RequestedBy { get; set; }
    public DateTimeOffset RequestedAt { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? HeartbeatAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>Earliest time the job may be (re)claimed; used for retry backoff.</summary>
    public DateTimeOffset NotBefore { get; set; }

    public bool CancelRequested { get; set; }
    public ErrorCategory? ErrorCategory { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public required string IdempotencyKey { get; set; }

    /// <summary>External (Azure or simulated) operation identifier, persisted so polling resumes after restart.</summary>
    public string? ExternalOperationId { get; set; }

    /// <summary>
    /// Set to the lab ID while a mutating job is active; a unique index makes "one mutating job per lab" atomic.
    /// </summary>
    public string? MutexKey { get; set; }

    public bool IsActive => Status is LabJobStatus.Queued or LabJobStatus.Running;
}
