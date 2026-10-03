using SqlServerLab.Application.Jobs;
using SqlServerLab.Domain.Labs;

namespace SqlServerLab.Application.Labs;

public sealed record LabDto(
    Guid Id,
    string Name,
    string OwnerId,
    string OwnerName,
    string Region,
    string ResourceGroupName,
    LabState State,
    string? StateReason,
    VmPowerState PowerState,
    bool IsSimulated,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset ExpiresAt,
    JobDto? CurrentJob,
    IReadOnlyList<LabCommand> AllowedActions);

public sealed record CreateLabRequest(string Name, string Region, int TtlHours);

public sealed record DeleteLabRequest(string ConfirmName);

public sealed record ExtendExpirationRequest(int Hours);

public sealed record CreateLabResponse(LabDto Lab, JobDto Job);
