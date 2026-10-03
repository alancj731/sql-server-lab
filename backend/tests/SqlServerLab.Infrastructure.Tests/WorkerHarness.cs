using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SqlServerLab.Application.Abstractions;
using SqlServerLab.Application.Jobs;
using SqlServerLab.Domain.Jobs;
using SqlServerLab.Domain.Labs;
using SqlServerLab.Domain.Policies;
using SqlServerLab.Infrastructure;
using SqlServerLab.Infrastructure.Jobs;
using SqlServerLab.Infrastructure.Persistence;
using SqlServerLab.Worker;
using SqlServerLab.Worker.Jobs;

namespace SqlServerLab.Infrastructure.Tests;

/// <summary>Real worker services over a temporary SQLite file with zero-delay simulation.</summary>
public sealed class WorkerHarness : IAsyncDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"sqllab-test-{Guid.NewGuid():N}.db");

    public WorkerHarness(Dictionary<string, string?>? overrides = null, IClock? clock = null)
    {
        var settings = new Dictionary<string, string?>
        {
            ["ControlDb:Provider"] = "Sqlite",
            ["ControlDb:ConnectionString"] = $"Data Source={_path};Default Timeout=30;Pooling=False",
            ["Infrastructure:Mode"] = "Local",
            ["LocalSimulation:ProvisionSeconds"] = "0",
            ["LocalSimulation:StartSeconds"] = "0",
            ["LocalSimulation:DeallocateSeconds"] = "0",
            ["LocalSimulation:DeleteSeconds"] = "0",
            ["LocalSimulation:SqlReadySeconds"] = "0",
            ["Worker:WorkerId"] = "test-worker",
            ["Worker:LeaseSeconds"] = "5",
            ["Worker:HeartbeatIntervalMs"] = "100",
            ["Worker:OperationPollIntervalMs"] = "20",
        };
        foreach (var (k, v) in overrides ?? [])
        {
            settings[k] = v;
        }

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection();
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.Warning));
        services.AddControlPlane(configuration);
        services.AddJobProcessing(configuration);
        if (clock is not null)
        {
            services.AddSingleton(clock);
        }

        Services = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        using var db = NewContext();
        ControlDbInitializer.MigrateAsync(db).GetAwaiter().GetResult();
    }

    public ServiceProvider Services { get; }
    public JobProcessor Processor => Services.GetRequiredService<JobProcessor>();
    public JobLeaseStore Store => Services.GetRequiredService<JobLeaseStore>();
    public IClock Clock => Services.GetRequiredService<IClock>();

    public ControlDbContext NewContext() =>
        Services.GetRequiredService<IDbContextFactory<ControlDbContext>>().CreateDbContext();

    public async Task<Lab> CreateLabAsync(string name, LabState state = LabState.Requested, LabJobType? job = LabJobType.ProvisionLab)
    {
        await using var db = NewContext();
        var id = Guid.NewGuid();
        var now = Clock.UtcNow;
        var lab = new Lab
        {
            Id = id,
            OwnerId = "alice",
            Name = name,
            Region = "eastus",
            ResourceGroupName = ResourceNaming.ResourceGroupName(name, id),
            State = state,
            IsSimulated = true,
            CreatedAt = now,
            UpdatedAt = now,
            ExpiresAt = now.AddHours(2),
        };
        db.Labs.Add(lab);
        await db.SaveChangesAsync();
        if (job is not null)
        {
            await new JobService(db, Clock).EnqueueAsync(lab, job.Value, "alice", "corr-test");
        }

        return lab;
    }

    public async Task<LabJob> EnqueueAsync(Guid labId, LabJobType type)
    {
        await using var db = NewContext();
        var lab = await db.Labs.SingleAsync(l => l.Id == labId);
        return (await new JobService(db, Clock).EnqueueAsync(lab, type, "alice", "corr-test")).Job;
    }

    /// <summary>Processes jobs until the queue has been empty for a short while (covers retry backoff).</summary>
    public async Task DrainAsync(TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(20));
        while (DateTime.UtcNow < deadline)
        {
            if (await Processor.ProcessNextAsync(CancellationToken.None))
            {
                continue;
            }

            await using var db = NewContext();
            if (!await db.Jobs.AnyAsync(j => j.Status == LabJobStatus.Queued || j.Status == LabJobStatus.Running))
            {
                return;
            }

            await Task.Delay(50);
        }

        throw new TimeoutException("Jobs did not drain.");
    }

    public async Task<Lab> LabAsync(Guid id)
    {
        await using var db = NewContext();
        return await db.Labs.AsNoTracking().SingleAsync(l => l.Id == id);
    }

    public async Task<List<LabJob>> JobsAsync(Guid labId)
    {
        await using var db = NewContext();
        return await db.Jobs.AsNoTracking().Where(j => j.LabId == labId).ToListAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await Services.DisposeAsync();
        foreach (var suffix in new[] { "", "-wal", "-shm" })
        {
            File.Delete(_path + suffix);
        }
    }
}

public sealed class ManualClock : IClock
{
    public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.UtcNow;
}
