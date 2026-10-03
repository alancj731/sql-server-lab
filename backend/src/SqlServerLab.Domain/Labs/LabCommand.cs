namespace SqlServerLab.Domain.Labs;

/// <summary>User-initiated commands whose availability depends on the lab state.</summary>
public enum LabCommand
{
    Start,
    Deallocate,
    Delete,
    ExtendExpiration,
}
