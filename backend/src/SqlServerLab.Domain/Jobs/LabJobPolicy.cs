namespace SqlServerLab.Domain.Jobs;

public static class LabJobPolicy
{
    private static readonly TimeSpan BaseDelay = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan MaxDelay = TimeSpan.FromMinutes(2);

    /// <summary>Only read-only collection may run alongside another job on the same lab.</summary>
    public static bool IsMutating(LabJobType type) => type is not LabJobType.CollectMetrics;

    /// <summary>
    /// Deletion and patch installation must not be retried blindly after an ambiguous result;
    /// the actual state is reconciled first.
    /// </summary>
    public static bool AutoRetryAllowed(LabJobType type) =>
        type is not (LabJobType.DeleteLab or LabJobType.InstallPatches);

    public static int MaxAttempts(LabJobType type) => AutoRetryAllowed(type) ? 5 : 1;

    public static bool ShouldRetry(LabJobType type, ErrorCategory category, int attemptCount) =>
        category == ErrorCategory.Transient && AutoRetryAllowed(type) && attemptCount < MaxAttempts(type);

    /// <summary>Exponential backoff with full jitter: uniform in [0, min(max, base * 2^(attempt-1))].</summary>
    public static TimeSpan Backoff(int attemptCount, Random random)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(attemptCount, 1);
        var exponent = Math.Min(attemptCount - 1, 16);
        var ceiling = Math.Min(MaxDelay.TotalMilliseconds, BaseDelay.TotalMilliseconds * Math.Pow(2, exponent));
        return TimeSpan.FromMilliseconds(random.NextDouble() * ceiling);
    }

    public static TimeSpan BackoffCeiling(int attemptCount) =>
        TimeSpan.FromMilliseconds(Math.Min(
            MaxDelay.TotalMilliseconds,
            BaseDelay.TotalMilliseconds * Math.Pow(2, Math.Min(attemptCount - 1, 16))));
}
