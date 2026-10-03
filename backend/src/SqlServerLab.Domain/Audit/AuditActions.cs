namespace SqlServerLab.Domain.Audit;

public static class AuditActions
{
    public const string LabRequested = "lab.requested";
    public const string LabStateChanged = "lab.state-changed";
    public const string LabExpirationExtended = "lab.expiration-extended";
    public const string LabExpired = "lab.expired";
    public const string LabReconciled = "lab.reconciled";
    public const string LabOrphanDeleted = "lab.orphan-deleted";
    public const string JobEnqueued = "job.enqueued";
    public const string JobSucceeded = "job.succeeded";
    public const string JobFailed = "job.failed";
    public const string JobRetryScheduled = "job.retry-scheduled";
    public const string JobCancelRequested = "job.cancel-requested";
    public const string JobCancelled = "job.cancelled";
    public const string JobLeaseRecovered = "job.lease-recovered";

    public const string SystemActor = "system";
}
