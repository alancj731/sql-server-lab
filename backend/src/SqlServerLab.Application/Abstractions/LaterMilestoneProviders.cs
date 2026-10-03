using SqlServerLab.Domain.Labs;

namespace SqlServerLab.Application.Abstractions;

// Seams defined now so application logic never depends on Azure SDK or SqlClient types directly.
// Real implementations arrive with the milestone noted on each interface.

/// <summary>Allow-listed SQL experiments against a lab (health check now; experiments in Milestones 4–6).</summary>
public interface ISqlLabExecutor
{
    /// <summary>True when SQL Server on the lab answers <c>SELECT 1</c> with the lab login.</summary>
    Task<bool> PingAsync(Lab lab, CancellationToken cancellationToken);
}

/// <summary>SQL/Azure telemetry (Milestone 7).</summary>
public interface IMetricProvider
{
    string Source { get; }
}

/// <summary>Backup to storage (Milestone 8).</summary>
public interface IBackupProvider
{
    bool IsSimulated { get; }
}

/// <summary>Update Manager assessment/installation (Milestone 9).</summary>
public interface IPatchProvider
{
    bool IsSimulated { get; }
}

/// <summary>Server-side secret storage (Key Vault in Azure). Secret values never leave the server.</summary>
public interface ISecretProvider
{
    Task<string> GetOrCreateSecretAsync(string name, CancellationToken cancellationToken);

    /// <summary>Removes a secret permanently (used when a lab is deleted). Missing secrets are ignored.</summary>
    Task DeleteSecretAsync(string name, CancellationToken cancellationToken);
}

/// <summary>A lab resource group found in the cloud, used to detect orphans.</summary>
public sealed record LabResourceGroup(string Name, Guid? LabId, DateTimeOffset? CreatedAt);

/// <summary>Lists and removes lab resource groups for orphan reconciliation. Disabled in local mode.</summary>
public interface ILabResourceInventory
{
    bool Enabled { get; }

    Task<IReadOnlyList<LabResourceGroup>> ListAsync(CancellationToken cancellationToken);

    Task DeleteAsync(string resourceGroupName, CancellationToken cancellationToken);
}
