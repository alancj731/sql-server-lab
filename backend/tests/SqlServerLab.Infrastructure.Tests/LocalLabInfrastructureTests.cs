using Microsoft.Extensions.DependencyInjection;
using SqlServerLab.Application.Abstractions;
using SqlServerLab.Application.Errors;
using SqlServerLab.Domain.Jobs;
using SqlServerLab.Domain.Labs;

namespace SqlServerLab.Infrastructure.Tests;

public class LocalLabInfrastructureTests
{
    [Fact]
    public async Task Operations_are_idempotent_per_key_and_complete_over_time()
    {
        var clock = new ManualClock();
        await using var h = new WorkerHarness(new() { ["LocalSimulation:ProvisionSeconds"] = "10" }, clock);
        var infra = h.Services.GetRequiredService<ILabInfrastructure>();
        var lab = await h.CreateLabAsync("sim", job: null);
        var key = Guid.NewGuid();

        var first = await infra.BeginAsync(InfraOperationKind.Provision, new InfraRequest(lab, key, 1), CancellationToken.None);
        var again = await infra.BeginAsync(InfraOperationKind.Provision, new InfraRequest(lab, key, 1), CancellationToken.None);
        Assert.Equal(first.OperationId, again.OperationId);
        Assert.Equal(InfraOperationStatus.Running, first.Status);
        Assert.Equal(VmPowerState.NotCreated, await infra.GetPowerStateAsync(lab, CancellationToken.None));

        clock.UtcNow = clock.UtcNow.AddSeconds(5);
        Assert.InRange((await infra.GetOperationAsync(first.OperationId, CancellationToken.None))!.Progress, 40, 60);

        clock.UtcNow = clock.UtcNow.AddSeconds(6);
        var done = await infra.GetOperationAsync(first.OperationId, CancellationToken.None);
        Assert.Equal(InfraOperationStatus.Succeeded, done!.Status);
        Assert.Contains(lab.ResourceGroupName, done.ResourceId);
        Assert.Equal(VmPowerState.Running, await infra.GetPowerStateAsync(lab, CancellationToken.None));
    }

    [Fact]
    public async Task Fault_injection_is_deterministic_by_name()
    {
        await using var h = new WorkerHarness();
        var infra = h.Services.GetRequiredService<ILabInfrastructure>();

        var flaky = await h.CreateLabAsync("flaky-lab", job: null);
        var ex = await Assert.ThrowsAsync<JobExecutionException>(() =>
            infra.BeginAsync(InfraOperationKind.Provision, new InfraRequest(flaky, Guid.NewGuid(), 1), CancellationToken.None));
        Assert.Equal(ErrorCategory.Transient, ex.Category);
        var ok = await infra.BeginAsync(InfraOperationKind.Provision, new InfraRequest(flaky, Guid.NewGuid(), 2), CancellationToken.None);
        Assert.Equal(InfraOperationStatus.Succeeded, ok.Status);

        var broken = await h.CreateLabAsync("fail-lab", job: null);
        var failed = await infra.BeginAsync(InfraOperationKind.Provision, new InfraRequest(broken, Guid.NewGuid(), 1), CancellationToken.None);
        Assert.Equal(InfraOperationStatus.Failed, failed.Status);
        Assert.Equal(ErrorCategory.Permanent, failed.FailureCategory);
    }
}
