namespace SqlServerLab.Application.Labs;

public sealed record EnvironmentDto(
    string InfrastructureMode,
    bool IsSimulated,
    string AuthenticationMode,
    IReadOnlyList<string> Regions,
    int MinTtlHours,
    int MaxTtlHours,
    int MaxExtensionHours,
    int MaxActiveLabsPerUser);
