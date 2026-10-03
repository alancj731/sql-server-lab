using Microsoft.Extensions.Options;

namespace SqlServerLab.Worker.Jobs;

public sealed partial class JobRunner(JobProcessor processor, IOptions<WorkerOptions> options, ILogger<JobRunner> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var workerId = options.Value.ResolveWorkerId();
        LogStarted(workerId);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (await processor.ProcessNextAsync(stoppingToken))
                {
                    continue;
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                LogLoopError(ex);
            }

            try
            {
                await Task.Delay(options.Value.PollIntervalMs, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Job runner {WorkerId} started")]
    private partial void LogStarted(string workerId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Job runner loop error")]
    private partial void LogLoopError(Exception ex);
}
