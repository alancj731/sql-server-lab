namespace SqlServerLab.Domain.Policies;

public static class RegionAllowList
{
    public static readonly IReadOnlyList<string> Regions =
        ["centralus", "eastus", "eastus2", "westus2", "canadacentral", "westeurope", "northeurope", "uksouth"];

    public static bool IsAllowed(string? region) => region is not null && Regions.Contains(region);
}
