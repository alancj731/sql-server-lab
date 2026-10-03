namespace SqlServerLab.Domain.Labs;

public enum VmPowerState
{
    Unknown,
    NotCreated,
    Starting,
    Running,
    Deallocating,
    Deallocated,
    Deleted,
}
