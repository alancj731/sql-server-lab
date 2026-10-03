using SqlServerLab.Application.Abstractions;

namespace SqlServerLab.Infrastructure.Persistence;

public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
