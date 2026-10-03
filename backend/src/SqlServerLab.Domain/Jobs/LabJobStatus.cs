namespace SqlServerLab.Domain.Jobs;

public enum LabJobStatus
{
    Queued,
    Running,
    Succeeded,
    Failed,
    Cancelled,
}
