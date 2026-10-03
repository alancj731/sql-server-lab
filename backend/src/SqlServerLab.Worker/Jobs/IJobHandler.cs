using SqlServerLab.Domain.Jobs;

namespace SqlServerLab.Worker.Jobs;

public interface IJobHandler
{
    LabJobType Type { get; }

    Task ExecuteAsync(JobContext context, CancellationToken cancellationToken);
}
