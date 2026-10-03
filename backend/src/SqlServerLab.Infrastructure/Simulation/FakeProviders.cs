using System.Collections.Concurrent;
using System.Security.Cryptography;
using SqlServerLab.Application.Abstractions;
using SqlServerLab.Domain.Labs;

namespace SqlServerLab.Infrastructure.Simulation;

public sealed class FakeSqlLabExecutor : ISqlLabExecutor
{
    public Task<bool> PingAsync(Lab lab, CancellationToken cancellationToken) => Task.FromResult(lab.State == LabState.Ready);
}

public sealed class FakeMetricProvider : IMetricProvider
{
    public string Source => "simulated";
}

public sealed class FakeBackupProvider : IBackupProvider
{
    public bool IsSimulated => true;
}

public sealed class FakePatchProvider : IPatchProvider
{
    public bool IsSimulated => true;
}

/// <summary>In-memory secrets for local mode. Values are generated server-side and never returned by the API.</summary>
public sealed class InMemorySecretProvider : ISecretProvider
{
    private readonly ConcurrentDictionary<string, string> _secrets = new(StringComparer.Ordinal);

    public Task<string> GetOrCreateSecretAsync(string name, CancellationToken cancellationToken) =>
        Task.FromResult(_secrets.GetOrAdd(name, _ => Convert.ToBase64String(RandomNumberGenerator.GetBytes(24))));

    public Task DeleteSecretAsync(string name, CancellationToken cancellationToken)
    {
        _secrets.TryRemove(name, out _);
        return Task.CompletedTask;
    }
}
