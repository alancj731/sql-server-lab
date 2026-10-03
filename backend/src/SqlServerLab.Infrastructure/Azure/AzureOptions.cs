using System.ComponentModel.DataAnnotations;

namespace SqlServerLab.Infrastructure.Azure;

/// <summary>Azure-mode settings. All values are identifiers, never secrets.</summary>
public sealed class AzureOptions
{
    public const string SectionName = "Azure";

    [Required]
    [RegularExpression("^[0-9a-fA-F-]{36}$")]
    public string SubscriptionId { get; set; } = string.Empty;

    [Required]
    [RegularExpression("^[a-z0-9]+$")]
    public string Location { get; set; } = string.Empty;

    [Required]
    [RegularExpression("^[a-z0-9]{2,8}$")]
    public string Environment { get; set; } = string.Empty;

    [Required]
    [RegularExpression("^/subscriptions/.+/subnets/snet-labs$")]
    public string LabSubnetId { get; set; } = string.Empty;

    [Required]
    [RegularExpression("^Standard_[A-Za-z0-9_]+$")]
    public string VmSize { get; set; } = "Standard_D2s_v7";

    [Required]
    [Url]
    public string KeyVaultUri { get; set; } = string.Empty;

    /// <summary>Client ID of the user-assigned managed identity; empty locally (Azure CLI credential is used).</summary>
    public string? ManagedIdentityClientId { get; set; }

    /// <summary>Resource groups younger than this are never treated as orphans.</summary>
    [Range(10, 1440)]
    public int OrphanGraceMinutes { get; set; } = 60;
}
