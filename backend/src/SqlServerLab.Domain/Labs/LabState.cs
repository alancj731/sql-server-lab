namespace SqlServerLab.Domain.Labs;

public enum LabState
{
    Requested,
    Provisioning,
    Stopped,
    Starting,
    Configuring,
    Ready,
    Deallocating,
    Maintenance,
    Deleting,
    Deleted,
    Failed,
}
