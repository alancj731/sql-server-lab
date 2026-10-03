namespace SqlServerLab.Application.Abstractions;

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}
