using Microsoft.EntityFrameworkCore;
using SqlServerLab.Domain.Jobs;
using SqlServerLab.Domain.Labs;
using Testcontainers.MsSql;

namespace SqlServerLab.Infrastructure.Tests;

/// <summary>
/// Runs the SQL Server migration set and the full job lifecycle against a real SQL Server container, proving the
/// lease queries, filtered unique indexes, and concurrency tokens translate correctly (Azure SQL uses the same engine).
/// Requires Docker; returns early when Docker is unavailable.
/// </summary>
public sealed class SqlServerControlDbTests : IAsyncLifetime
{
    private MsSqlContainer? _sql;

    public async Task InitializeAsync()
    {
        if (!DockerAvailable())
        {
            return;
        }

        _sql = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();
        await _sql.StartAsync();
    }

    public async Task DisposeAsync()
    {
        if (_sql is not null)
        {
            await _sql.DisposeAsync();
        }
    }

    [Fact]
    public async Task Migrations_and_job_lifecycle_work_on_sql_server()
    {
        if (_sql is null)
        {
            return;
        }

        await using var h = new WorkerHarness(new()
        {
            ["ControlDb:Provider"] = "SqlServer",
            ["ControlDb:ConnectionString"] = _sql.GetConnectionString(),
        });

        await using (var db = h.NewContext())
        {
            Assert.Empty(await db.Database.GetPendingMigrationsAsync());
            Assert.True(db.Database.IsSqlServer());
        }

        var lab = await h.CreateLabAsync("sqlsrv");
        await h.DrainAsync();
        Assert.Equal(LabState.Ready, (await h.LabAsync(lab.Id)).State);

        // Filtered unique index: a second active mutating job for the same lab is rejected.
        await h.EnqueueAsync(lab.Id, LabJobType.DeallocateVm);
        await Assert.ThrowsAnyAsync<Exception>(() => h.EnqueueAsync(lab.Id, LabJobType.DeleteLab));

        await h.DrainAsync();
        Assert.Equal(LabState.Stopped, (await h.LabAsync(lab.Id)).State);

        // Competing claims on SQL Server.
        await h.EnqueueAsync(lab.Id, LabJobType.StartVm);
        var claims = await Task.WhenAll(Enumerable.Range(0, 6).Select(i =>
            Task.Run(() => h.Store.TryClaimNextAsync($"w{i}", TimeSpan.FromSeconds(30), CancellationToken.None))));
        Assert.Single(claims, c => c is not null);
    }

    private static bool DockerAvailable()
    {
        try
        {
            using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("docker", "info")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            });
            process!.WaitForExit(10_000);
            return process.ExitCode == 0;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }
}
