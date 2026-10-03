using System.ComponentModel.DataAnnotations;
using SqlServerLab.Domain.Policies;

namespace SqlServerLab.Application.Options;

public sealed class LabLimitsOptions
{
    public const string SectionName = "LabLimits";

    /// <summary>Labs per user that are not yet deleted.</summary>
    [Range(1, 20)]
    public int MaxActiveLabsPerUser { get; set; } = 2;

    /// <summary>
    /// Regions offered in this deployment (subset of the domain allow-list). In Azure mode this is the platform
    /// region, because lab VMs join the shared regional VNet. Empty means the full allow-list.
    /// </summary>
    public string[] AllowedRegions { get; set; } = [];

    public IReadOnlyList<string> EffectiveRegions() =>
        AllowedRegions.Length == 0
            ? RegionAllowList.Regions
            : RegionAllowList.Regions.Where(r => AllowedRegions.Contains(r, StringComparer.Ordinal)).ToList();
}
