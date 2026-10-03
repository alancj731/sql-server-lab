using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using SqlServerLab.Application.Jobs;
using SqlServerLab.Application.Labs;
using SqlServerLab.Infrastructure.Persistence;

namespace SqlServerLab.Api.Realtime;

/// <summary>
/// Polls the control database for lab/job rows changed since the last tick and pushes them to SignalR groups.
/// The worker never talks to the API directly (see docs/architecture/adr/0001-job-processing.md).
/// </summary>
public sealed partial class JobProgressBroadcaster(
    IServiceScopeFactory scopes,
    IHubContext<LabsHub> hub,
    ILogger<JobProgressBroadcaster> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(750);

    // Rows committed slightly out of timestamp order are caught by re-reading a short overlap window.
    private static readonly TimeSpan Overlap = TimeSpan.FromSeconds(5);

    private readonly Dictionary<Guid, DateTimeOffset> _sentLabs = [];
    private readonly Dictionary<Guid, DateTimeOffset> _sentJobs = [];
    private DateTimeOffset _watermark = DateTimeOffset.UtcNow;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    await TickAsync(stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    LogTickFailed(ex);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal shutdown.
        }
    }

    internal async Task TickAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
        var since = _watermark - Overlap;

        var jobs = await db.Jobs.AsNoTracking().Where(j => j.UpdatedAt > since).ToListAsync(cancellationToken);
        var labs = await db.Labs.AsNoTracking().Where(l => l.UpdatedAt > since).ToListAsync(cancellationToken);

        var jobLabIds = jobs.Select(j => j.LabId).Distinct().ToList();
        var owners = await db.Labs.AsNoTracking().Where(l => jobLabIds.Contains(l.Id))
            .Select(l => new { l.Id, l.OwnerId }).ToDictionaryAsync(l => l.Id, l => l.OwnerId, cancellationToken);

        foreach (var job in jobs.OrderBy(j => j.UpdatedAt))
        {
            if (_sentJobs.TryGetValue(job.Id, out var sent) && sent >= job.UpdatedAt)
            {
                continue;
            }

            _sentJobs[job.Id] = job.UpdatedAt;
            var groups = new List<string> { LabsHub.LabGroup(job.LabId), LabsHub.AdminGroup };
            if (owners.TryGetValue(job.LabId, out var owner))
            {
                groups.Add(LabsHub.UserGroup(owner));
            }

            await hub.Clients.Groups(groups).SendAsync("jobChanged", JobDto.From(job), cancellationToken);
            Advance(job.UpdatedAt);
        }

        foreach (var lab in labs.OrderBy(l => l.UpdatedAt))
        {
            if (_sentLabs.TryGetValue(lab.Id, out var sent) && sent >= lab.UpdatedAt)
            {
                continue;
            }

            _sentLabs[lab.Id] = lab.UpdatedAt;
            var dto = await LabService.ToDtoAsync(db, lab, cancellationToken);
            await hub.Clients.Groups(LabsHub.LabGroup(lab.Id), LabsHub.UserGroup(lab.OwnerId), LabsHub.AdminGroup)
                .SendAsync("labChanged", dto, cancellationToken);
            Advance(lab.UpdatedAt);
        }

        Prune(_sentJobs, since);
        Prune(_sentLabs, since);
    }

    private void Advance(DateTimeOffset value)
    {
        if (value > _watermark)
        {
            _watermark = value;
        }
    }

    private static void Prune(Dictionary<Guid, DateTimeOffset> sent, DateTimeOffset since)
    {
        foreach (var key in sent.Where(kv => kv.Value < since).Select(kv => kv.Key).ToList())
        {
            sent.Remove(key);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Broadcast tick failed")]
    private partial void LogTickFailed(Exception ex);
}
