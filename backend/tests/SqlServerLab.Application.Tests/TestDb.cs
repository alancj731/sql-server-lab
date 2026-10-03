using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SqlServerLab.Application.Abstractions;
using SqlServerLab.Application.Jobs;
using SqlServerLab.Application.Labs;
using SqlServerLab.Application.Options;
using SqlServerLab.Domain.Labs;
using SqlServerLab.Infrastructure.Persistence;

namespace SqlServerLab.Application.Tests;

public sealed class FakeClock : IClock
{
    public DateTimeOffset UtcNow { get; set; } = new(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);
}

public sealed class FakeUser(string id, bool admin = false) : ICurrentUser
{
    public string UserId { get; } = id;
    public string DisplayName => $"{UserId} (display)";
    public bool IsAdmin { get; } = admin;
}

public sealed class FakeCorrelation : ICorrelationContext
{
    public string CorrelationId => "test-correlation";
}

public sealed class StubInfrastructure : ILabInfrastructure
{
    public bool IsSimulated => true;
    public Task<InfraOperation> BeginAsync(InfraOperationKind kind, InfraRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task<InfraOperation?> GetOperationAsync(string operationId, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task<VmPowerState> GetPowerStateAsync(Lab lab, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task<bool> IsSqlReadyAsync(Lab lab, CancellationToken cancellationToken) => throw new NotSupportedException();
}

/// <summary>Shared in-memory SQLite database; each <see cref="Context"/> call is a fresh unit of work.</summary>
public sealed class TestDb : IDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");

    public TestDb()
    {
        _connection.Open();
        using var db = Context();
        db.Database.Migrate();
    }

    public FakeClock Clock { get; } = new();

    public ControlDbContext Context() =>
        new(new DbContextOptionsBuilder<ControlDbContext>().UseSqlite(_connection).Options);

    public LabService Service(string user = "alice", bool admin = false, int maxLabs = 2, string[]? regions = null)
    {
        var db = Context();
        return new LabService(db, Clock, new FakeUser(user, admin), new FakeCorrelation(), new StubInfrastructure(),
            new JobService(db, Clock), Microsoft.Extensions.Options.Options.Create(new LabLimitsOptions { MaxActiveLabsPerUser = maxLabs, AllowedRegions = regions ?? [] }));
    }

    public void SetState(Guid labId, LabState state)
    {
        using var db = Context();
        var lab = db.Labs.Single(l => l.Id == labId);
        lab.State = state;
        lab.UpdatedAt = Clock.UtcNow;
        db.SaveChanges();
    }

    public void Dispose() => _connection.Dispose();
}
