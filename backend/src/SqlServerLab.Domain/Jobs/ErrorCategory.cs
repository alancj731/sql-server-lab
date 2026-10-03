namespace SqlServerLab.Domain.Jobs;

public enum ErrorCategory
{
    Transient,
    Permanent,
    Authorization,
    Quota,
    Validation,
    Cancellation,
}
