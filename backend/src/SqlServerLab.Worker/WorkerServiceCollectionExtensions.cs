using SqlServerLab.Domain.Jobs;
using SqlServerLab.Worker.Handlers;
using SqlServerLab.Worker.Jobs;
using SqlServerLab.Worker.Services;

namespace SqlServerLab.Worker;

public static class WorkerServiceCollectionExtensions
{
    /// <summary>Job processing services without the hosted loops (tests drive <see cref="JobProcessor"/> directly).</summary>
    public static IServiceCollection AddJobProcessing(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<WorkerOptions>().Bind(configuration.GetSection(WorkerOptions.SectionName)).ValidateDataAnnotations().ValidateOnStart();
        services.AddSingleton<IJobHandler, ProvisionLabHandler>();
        services.AddSingleton<IJobHandler, StartVmHandler>();
        services.AddSingleton<IJobHandler, DeallocateVmHandler>();
        services.AddSingleton<IJobHandler, DeleteLabHandler>();
        services.AddSingleton<IJobHandler, ReconcileLabHandler>();
        services.AddSingleton<JobProcessor>();
        services.AddSingleton<ExpiryService>();
        services.AddOptions<OrphanOptions>().Bind(configuration.GetSection(OrphanOptions.SectionName));
        services.AddSingleton<OrphanReconciler>();
        return services;
    }
}
