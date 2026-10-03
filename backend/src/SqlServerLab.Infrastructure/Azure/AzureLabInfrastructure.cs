using System.ClientModel.Primitives;
using System.Text.Json;
using Azure;
using Azure.Core;
using Azure.ResourceManager;
using Azure.ResourceManager.Compute;
using Azure.ResourceManager.Resources;
using Azure.ResourceManager.Resources.Deployments;
using Azure.ResourceManager.Resources.Deployments.Models;
using Microsoft.Extensions.Options;
using SqlServerLab.Application.Abstractions;
using SqlServerLab.Application.Errors;
using SqlServerLab.Domain.Jobs;
using SqlServerLab.Domain.Labs;

namespace SqlServerLab.Infrastructure.Azure;

/// <summary>
/// Lab VM lifecycle on Azure. Operations are started with <see cref="WaitUntil.Started"/> and their status is
/// re-derived from Azure state on every poll, so a restarted worker resumes without SDK rehydration.
/// </summary>
public sealed class AzureLabInfrastructure(
    ArmClient arm,
    ISecretProvider secrets,
    ISqlLabExecutor sql,
    IOptions<AzureOptions> options) : ILabInfrastructure
{
    private readonly AzureOptions _options = options.Value;

    public bool IsSimulated => false;

    public async Task<InfraOperation> BeginAsync(InfraOperationKind kind, InfraRequest request, CancellationToken cancellationToken)
    {
        var lab = request.Lab;
        LabDeployment.EnsureLabResourceGroup(lab.ResourceGroupName);
        try
        {
            return kind switch
            {
                InfraOperationKind.Provision => await BeginProvisionAsync(lab, request.OperationKey, cancellationToken),
                InfraOperationKind.Start => await BeginVmAsync(lab, AzureOperationId.VmStart, cancellationToken),
                InfraOperationKind.Deallocate => await BeginVmAsync(lab, AzureOperationId.VmDeallocate, cancellationToken),
                InfraOperationKind.Delete => await BeginDeleteAsync(lab, cancellationToken),
                _ => throw new ArgumentOutOfRangeException(nameof(kind)),
            };
        }
        catch (RequestFailedException ex)
        {
            throw ToJobException(ex);
        }
    }

    public async Task<InfraOperation?> GetOperationAsync(string operationId, CancellationToken cancellationToken)
    {
        var op = AzureOperationId.Parse(operationId);
        try
        {
            return op.Kind switch
            {
                AzureOperationId.Deploy => await DeploymentStatusAsync(op, operationId, cancellationToken),
                AzureOperationId.VmStart => await VmStatusAsync(op, operationId, InfraOperationKind.Start, "PowerState/running", cancellationToken),
                AzureOperationId.VmDeallocate => await VmStatusAsync(op, operationId, InfraOperationKind.Deallocate, "PowerState/deallocated", cancellationToken),
                _ => await DeleteStatusAsync(op, operationId, cancellationToken),
            };
        }
        catch (RequestFailedException ex)
        {
            throw ToJobException(ex);
        }
    }

    public async Task<VmPowerState> GetPowerStateAsync(Lab lab, CancellationToken cancellationToken)
    {
        LabDeployment.EnsureLabResourceGroup(lab.ResourceGroupName);
        try
        {
            if (await GetResourceGroupAsync(lab.ResourceGroupName, cancellationToken) is null)
            {
                return VmPowerState.Deleted;
            }

            var vm = await GetVmAsync(lab, cancellationToken);
            if (vm is null)
            {
                return VmPowerState.NotCreated;
            }

            var view = (await vm.InstanceViewAsync(cancellationToken)).Value;
            return view.Statuses.Select(s => s.Code).FirstOrDefault(c => c?.StartsWith("PowerState/", StringComparison.Ordinal) == true) switch
            {
                "PowerState/running" => VmPowerState.Running,
                "PowerState/starting" => VmPowerState.Starting,
                "PowerState/deallocating" or "PowerState/stopping" => VmPowerState.Deallocating,
                "PowerState/deallocated" or "PowerState/stopped" => VmPowerState.Deallocated,
                _ => VmPowerState.Unknown,
            };
        }
        catch (RequestFailedException ex)
        {
            throw ToJobException(ex);
        }
    }

    public async Task<bool> IsSqlReadyAsync(Lab lab, CancellationToken cancellationToken) =>
        await GetPowerStateAsync(lab, cancellationToken) == VmPowerState.Running && await sql.PingAsync(lab, cancellationToken);

    private async Task<InfraOperation> BeginProvisionAsync(Lab lab, Guid operationKey, CancellationToken cancellationToken)
    {
        if (!string.Equals(lab.Region, _options.Location, StringComparison.Ordinal))
        {
            throw new JobExecutionException(ErrorCategory.Validation, "RegionMismatch",
                $"This deployment provisions labs in {_options.Location} only.");
        }

        var tags = LabDeployment.Tags(lab, _options.Environment);
        var data = new ResourceGroupData(new AzureLocation(_options.Location));
        foreach (var (key, value) in tags)
        {
            data.Tags[key] = value;
        }

        var group = (await Subscription.GetResourceGroups().CreateOrUpdateAsync(WaitUntil.Completed, lab.ResourceGroupName, data, cancellationToken)).Value;
        var deploymentName = LabDeployment.DeploymentName(operationKey);
        var opId = new AzureOperationId(AzureOperationId.Deploy, lab.ResourceGroupName, deploymentName).ToString();

        // Re-beginning the same job never submits a second deployment.
        var existing = await ResourcesDeploymentsExtensions.GetArmDeployments(group).GetIfExistsAsync(deploymentName, cancellationToken);
        if (!existing.HasValue)
        {
            var adminPassword = await secrets.GetOrCreateSecretAsync(LabDeployment.AdminSecretName(lab.Id), cancellationToken);
            var saPassword = await secrets.GetOrCreateSecretAsync(LabDeployment.SaSecretName(lab.Id), cancellationToken);
            var sqlPassword = await secrets.GetOrCreateSecretAsync(LabDeployment.SqlSecretName(lab.Id), cancellationToken);
            var properties = new ArmDeploymentProperties(ArmDeploymentMode.Incremental)
            {
                Template = BinaryData.FromString(LabDeployment.Template()),
                Parameters = LabDeployment.Parameters(lab, _options, adminPassword, saPassword, sqlPassword),
            };
            await ResourcesDeploymentsExtensions.GetArmDeployments(group).CreateOrUpdateAsync(WaitUntil.Started, deploymentName, new ArmDeploymentContent(properties), cancellationToken);
        }

        return new InfraOperation(opId, InfraOperationKind.Provision, InfraOperationStatus.Running, 0);
    }

    private async Task<InfraOperation> BeginVmAsync(Lab lab, string kind, CancellationToken cancellationToken)
    {
        var vm = await GetVmAsync(lab, cancellationToken)
                 ?? throw new JobExecutionException(ErrorCategory.Permanent, "VmNotFound", "The lab VM does not exist.");
        if (kind == AzureOperationId.VmStart)
        {
            await vm.PowerOnAsync(WaitUntil.Started, cancellationToken);
        }
        else
        {
            await vm.DeallocateAsync(WaitUntil.Started, cancellationToken: cancellationToken);
        }

        var opId = new AzureOperationId(kind, lab.ResourceGroupName, LabDeployment.VmName(lab.Id)).ToString();
        var infraKind = kind == AzureOperationId.VmStart ? InfraOperationKind.Start : InfraOperationKind.Deallocate;
        return new InfraOperation(opId, infraKind, InfraOperationStatus.Running, 0);
    }

    private async Task<InfraOperation> BeginDeleteAsync(Lab lab, CancellationToken cancellationToken)
    {
        var opId = new AzureOperationId(AzureOperationId.ResourceGroupDelete, lab.ResourceGroupName, lab.Id.ToString("N")).ToString();
        var group = await GetResourceGroupAsync(lab.ResourceGroupName, cancellationToken);
        if (group is not null)
        {
            await group.DeleteAsync(WaitUntil.Started, cancellationToken: cancellationToken);
        }

        return new InfraOperation(opId, InfraOperationKind.Delete, InfraOperationStatus.Running, 0);
    }

    private async Task<InfraOperation> DeploymentStatusAsync(AzureOperationId op, string operationId, CancellationToken cancellationToken)
    {
        var group = await GetResourceGroupAsync(op.ResourceGroup, cancellationToken)
                    ?? throw new JobExecutionException(ErrorCategory.Permanent, "ResourceGroupMissing", "The lab resource group no longer exists.");
        var deployment = (await ResourcesDeploymentsExtensions.GetArmDeployments(group).GetAsync(op.Name, cancellationToken)).Value;
        var properties = deployment.Data.Properties;
        var state = properties.ProvisioningState?.ToString();

        if (state == "Succeeded")
        {
            var vmId = ReadOutput(properties.Outputs, "vmResourceId");
            return new InfraOperation(operationId, InfraOperationKind.Provision, InfraOperationStatus.Succeeded, 100, vmId);
        }

        if (state is "Failed" or "Canceled")
        {
            var (code, message) = InnermostError(properties.Error);
            return new InfraOperation(operationId, InfraOperationKind.Provision, InfraOperationStatus.Failed, 100, null,
                AzureErrorClassifier.Classify(0, code), Redactor.Sanitize($"{code}: {message}"));
        }

        // Progress: share of the two lab resources (NIC, VM) whose deployment operation succeeded.
        var done = 0;
        await foreach (var item in deployment.GetDeploymentOperationsAsync(cancellationToken: cancellationToken))
        {
            if (item.Properties?.ProvisioningState == "Succeeded" && item.Properties.TargetResource is not null)
            {
                done++;
            }
        }

        return new InfraOperation(operationId, InfraOperationKind.Provision, InfraOperationStatus.Running, Math.Min(95, 5 + (done * 45)));
    }

    private async Task<InfraOperation> VmStatusAsync(
        AzureOperationId op, string operationId, InfraOperationKind kind, string targetPowerState, CancellationToken cancellationToken)
    {
        var id = VirtualMachineResource.CreateResourceIdentifier(_options.SubscriptionId, op.ResourceGroup, op.Name!);
        var view = (await arm.GetVirtualMachineResource(id).InstanceViewAsync(cancellationToken)).Value;
        var codes = view.Statuses.Select(s => s.Code ?? string.Empty).ToList();

        if (codes.Contains("ProvisioningState/failed", StringComparer.OrdinalIgnoreCase)
            || codes.Any(c => c.StartsWith("ProvisioningState/failed/", StringComparison.OrdinalIgnoreCase)))
        {
            var detail = view.Statuses.FirstOrDefault(s => s.Code?.StartsWith("ProvisioningState/failed", StringComparison.OrdinalIgnoreCase) == true);
            var code = detail?.Code?.Split('/').LastOrDefault();
            return new InfraOperation(operationId, kind, InfraOperationStatus.Failed, 100, null,
                AzureErrorClassifier.Classify(0, code), Redactor.Sanitize(detail?.Message ?? "The VM operation failed."));
        }

        var reached = codes.Contains(targetPowerState) && codes.Contains("ProvisioningState/succeeded", StringComparer.OrdinalIgnoreCase);
        return new InfraOperation(operationId, kind, reached ? InfraOperationStatus.Succeeded : InfraOperationStatus.Running, reached ? 100 : 50);
    }

    private async Task<InfraOperation> DeleteStatusAsync(AzureOperationId op, string operationId, CancellationToken cancellationToken)
    {
        if (await GetResourceGroupAsync(op.ResourceGroup, cancellationToken) is not null)
        {
            return new InfraOperation(operationId, InfraOperationKind.Delete, InfraOperationStatus.Running, 50);
        }

        if (Guid.TryParseExact(op.Name, "N", out var labId))
        {
            foreach (var name in LabDeployment.SecretNames(labId))
            {
                await secrets.DeleteSecretAsync(name, cancellationToken);
            }
        }

        return new InfraOperation(operationId, InfraOperationKind.Delete, InfraOperationStatus.Succeeded, 100);
    }

    private SubscriptionResource Subscription =>
        arm.GetSubscriptionResource(SubscriptionResource.CreateResourceIdentifier(_options.SubscriptionId));

    private async Task<ResourceGroupResource?> GetResourceGroupAsync(string name, CancellationToken cancellationToken)
    {
        var result = await Subscription.GetResourceGroups().GetIfExistsAsync(name, cancellationToken);
        return result.HasValue ? result.Value : null;
    }

    private async Task<VirtualMachineResource?> GetVmAsync(Lab lab, CancellationToken cancellationToken)
    {
        var id = VirtualMachineResource.CreateResourceIdentifier(_options.SubscriptionId, lab.ResourceGroupName, LabDeployment.VmName(lab.Id));
        try
        {
            return (await arm.GetVirtualMachineResource(id).GetAsync(cancellationToken: cancellationToken)).Value;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    internal static string? ReadOutput(BinaryData? outputs, string name)
    {
        if (outputs is null)
        {
            return null;
        }

        using var doc = JsonDocument.Parse(outputs);
        return doc.RootElement.TryGetProperty(name, out var output) && output.TryGetProperty("value", out var value)
            ? value.GetString()
            : null;
    }

    private static (string? Code, string? Message) InnermostError<T>(T? error)
        where T : class, IPersistableModel<T>
    {
        return error is null ? (null, null) : InnermostError(ModelReaderWriter.Write(error).ToString());
    }

    /// <summary>ARM nests the useful cause in <c>details</c>; returns the deepest code and message.</summary>
    internal static (string? Code, string? Message) InnermostError(string errorJson)
    {
        using var doc = JsonDocument.Parse(errorJson);
        var current = doc.RootElement;
        if (current.TryGetProperty("error", out var wrapped))
        {
            current = wrapped;
        }

        while (current.TryGetProperty("details", out var details) && details.ValueKind == JsonValueKind.Array && details.GetArrayLength() > 0)
        {
            current = details[0];
        }

        return (Str(current, "code"), Str(current, "message"));

        static string? Str(JsonElement e, string name) =>
            e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    }

    internal static JobExecutionException ToJobException(RequestFailedException ex) =>
        new(AzureErrorClassifier.Classify(ex.Status, ex.ErrorCode), ex.ErrorCode ?? $"Http{ex.Status}",
            Redactor.Sanitize($"Azure request failed ({ex.Status} {ex.ErrorCode}).") ?? "Azure request failed.", ex);
}

/// <summary>Lab resource groups tagged for this environment, for orphan reconciliation.</summary>
public sealed class AzureLabResourceInventory(ArmClient arm, IOptions<AzureOptions> options) : ILabResourceInventory
{
    public bool Enabled => true;

    public async Task<IReadOnlyList<LabResourceGroup>> ListAsync(CancellationToken cancellationToken)
    {
        var subscription = arm.GetSubscriptionResource(SubscriptionResource.CreateResourceIdentifier(options.Value.SubscriptionId));
        var filter = $"tagName eq 'environment' and tagValue eq '{options.Value.Environment}'";
        var result = new List<LabResourceGroup>();
        await foreach (var group in subscription.GetResourceGroups().GetAllAsync(filter, cancellationToken: cancellationToken))
        {
            var name = group.Data.Name;
            if (!LabDeployment.IsLabResourceGroup(name))
            {
                continue;
            }

            var tags = group.Data.Tags;
            var labId = tags.TryGetValue("labId", out var id) && Guid.TryParse(id, out var parsed) ? parsed : (Guid?)null;
            var created = tags.TryGetValue("createdAt", out var at) && DateTimeOffset.TryParse(at, out var parsedAt) ? parsedAt : (DateTimeOffset?)null;
            result.Add(new LabResourceGroup(name, labId, created));
        }

        return result;
    }

    public async Task DeleteAsync(string resourceGroupName, CancellationToken cancellationToken)
    {
        LabDeployment.EnsureLabResourceGroup(resourceGroupName);
        var subscription = arm.GetSubscriptionResource(SubscriptionResource.CreateResourceIdentifier(options.Value.SubscriptionId));
        var group = await subscription.GetResourceGroups().GetIfExistsAsync(resourceGroupName, cancellationToken);
        if (group.HasValue)
        {
            await group.Value!.DeleteAsync(WaitUntil.Started, cancellationToken: cancellationToken);
        }
    }
}

/// <summary>Local mode has no cloud resources to reconcile.</summary>
public sealed class NoLabResourceInventory : ILabResourceInventory
{
    public bool Enabled => false;

    public Task<IReadOnlyList<LabResourceGroup>> ListAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<LabResourceGroup>>([]);

    public Task DeleteAsync(string resourceGroupName, CancellationToken cancellationToken) => Task.CompletedTask;
}
