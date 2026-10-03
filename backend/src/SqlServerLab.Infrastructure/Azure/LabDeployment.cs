using System.Text.Json;
using System.Text.RegularExpressions;
using SqlServerLab.Domain.Labs;

namespace SqlServerLab.Infrastructure.Azure;

/// <summary>
/// Pure naming, tagging, and parameter rules for a lab deployment. Every Azure call validates its inputs here first,
/// so the worker can only touch resources that follow the lab naming convention.
/// </summary>
public static partial class LabDeployment
{
    public const string SqlLogin = "labworker";
    public const string AdminUsername = "labadmin";

    public static string VmName(Guid labId) => $"sqllab{labId:N}"[..14];

    public static string NicName(Guid labId) => $"nic-{VmName(labId)}";

    public static string DeploymentName(Guid operationKey) => $"lab-{operationKey:N}";

    public static string AdminSecretName(Guid labId) => $"lab-{labId:N}-vmadmin";

    public static string SqlSecretName(Guid labId) => $"lab-{labId:N}-sql";

    public static string SaSecretName(Guid labId) => $"lab-{labId:N}-sa";

    public static IReadOnlyList<string> SecretNames(Guid labId) => [AdminSecretName(labId), SaSecretName(labId), SqlSecretName(labId)];

    public static void EnsureLabResourceGroup(string name)
    {
        if (!LabResourceGroupPattern().IsMatch(name))
        {
            throw new ArgumentException($"'{name}' is not a lab resource group name.", nameof(name));
        }
    }

    public static bool IsLabResourceGroup(string name) => LabResourceGroupPattern().IsMatch(name);

    public static Dictionary<string, string> Tags(Lab lab, string environment) => new(StringComparer.Ordinal)
    {
        ["labId"] = lab.Id.ToString(),
        ["ownerId"] = lab.OwnerId,
        ["environment"] = environment,
        ["createdAt"] = lab.CreatedAt.UtcDateTime.ToString("O"),
        ["expiresAt"] = lab.ExpiresAt.UtcDateTime.ToString("O"),
        ["app"] = "sql-server-lab",
    };

    /// <summary>ARM deployment parameters. Secret values appear only here, in memory, and are sent to ARM over TLS.</summary>
    public static BinaryData Parameters(Lab lab, AzureOptions options, string adminPassword, string saPassword, string sqlPassword)
    {
        EnsureLabResourceGroup(lab.ResourceGroupName);
        var parameters = new Dictionary<string, object>
        {
            ["labId"] = new { value = lab.Id.ToString() },
            ["vmName"] = new { value = VmName(lab.Id) },
            ["location"] = new { value = options.Location },
            ["vmSize"] = new { value = options.VmSize },
            ["diskControllerType"] = new { value = DiskControllerType(options.VmSize) },
            ["subnetId"] = new { value = options.LabSubnetId },
            ["adminUsername"] = new { value = AdminUsername },
            ["adminPassword"] = new { value = adminPassword },
            ["saPassword"] = new { value = saPassword },
            ["sqlLogin"] = new { value = SqlLogin },
            ["sqlPassword"] = new { value = sqlPassword },
            ["tags"] = new { value = Tags(lab, options.Environment) },
        };
        return BinaryData.FromString(JsonSerializer.Serialize(parameters));
    }

    /// <summary>v6 and later VM generations expose disks over NVMe only.</summary>
    public static string DiskControllerType(string vmSize) =>
        NvmeGeneration().IsMatch(vmSize) ? "NVMe" : "SCSI";

    [GeneratedRegex("_v([6-9]|\\d{2,})$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 100)]
    private static partial Regex NvmeGeneration();

    public static string Template()
    {
        using var stream = typeof(LabDeployment).Assembly.GetManifestResourceStream("SqlServerLab.Infrastructure.Azure.lab.json")
                           ?? throw new InvalidOperationException("The compiled lab template is missing.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    [GeneratedRegex("^rg-sqllab-[a-z][a-z0-9-]{1,28}[a-z0-9]-[0-9a-f]{8}$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 100)]
    private static partial Regex LabResourceGroupPattern();
}

/// <summary>Durable, restart-safe operation identifiers. Status is always re-derived from Azure state.</summary>
public sealed record AzureOperationId(string Kind, string ResourceGroup, string? Name)
{
    public const string Deploy = "deploy";
    public const string VmStart = "vm-start";
    public const string VmDeallocate = "vm-deallocate";
    public const string ResourceGroupDelete = "rg-delete";

    public override string ToString() => Name is null ? $"{Kind}|{ResourceGroup}" : $"{Kind}|{ResourceGroup}|{Name}";

    public static AzureOperationId Parse(string value)
    {
        var parts = value.Split('|');
        if (parts.Length is < 2 or > 3 || parts[0] is not (Deploy or VmStart or VmDeallocate or ResourceGroupDelete))
        {
            throw new FormatException("Unrecognised operation identifier.");
        }

        LabDeployment.EnsureLabResourceGroup(parts[1]);
        return new AzureOperationId(parts[0], parts[1], parts.Length == 3 ? parts[2] : null);
    }
}
