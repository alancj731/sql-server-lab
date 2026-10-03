namespace SqlServerLab.Domain.Policies;

public static class ExpiryPolicy
{
    public const int MinInitialHours = 1;
    public const int MaxInitialHours = 8;
    public const int MaxExtensionHours = 4;
    public static readonly TimeSpan AbsoluteMaximumLifetime = TimeSpan.FromHours(24);

    public static bool IsValidInitialTtl(int hours) => hours is >= MinInitialHours and <= MaxInitialHours;

    public static bool IsValidExtension(int hours) => hours is >= 1 and <= MaxExtensionHours;

    /// <summary>Extends from the later of now and the current expiry, capped at the absolute lifetime.</summary>
    public static DateTimeOffset Extend(DateTimeOffset createdAt, DateTimeOffset currentExpiry, DateTimeOffset now, int hours)
    {
        if (!IsValidExtension(hours))
        {
            throw new ArgumentOutOfRangeException(nameof(hours));
        }

        var start = currentExpiry > now ? currentExpiry : now;
        var cap = createdAt + AbsoluteMaximumLifetime;
        var proposed = start.AddHours(hours);
        return proposed > cap ? cap : proposed;
    }
}
