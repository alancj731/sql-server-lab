namespace SqlServerLab.Application.Abstractions;

public interface ICurrentUser
{
    string UserId { get; }
    string DisplayName { get; }
    bool IsAdmin { get; }
}
