namespace SqlServerLab.Domain.Policies;

public static class ResourceNaming
{
    public static string ResourceGroupName(string labName, Guid labId)
    {
        if (!LabNamePolicy.IsValid(labName))
        {
            throw new ArgumentException("Invalid lab name.", nameof(labName));
        }

        return $"rg-sqllab-{labName}-{labId.ToString("N")[..8]}";
    }
}
