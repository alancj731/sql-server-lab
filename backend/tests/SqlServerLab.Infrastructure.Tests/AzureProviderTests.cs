using System.Text.Json;
using SqlServerLab.Application.Abstractions;
using SqlServerLab.Domain.Jobs;
using SqlServerLab.Domain.Labs;
using SqlServerLab.Infrastructure.Azure;
using SqlServerLab.Worker.Services;

namespace SqlServerLab.Infrastructure.Tests;

public class AzureErrorClassifierTests
{
    [Theory]
    [InlineData(409, "QuotaExceeded", ErrorCategory.Quota)]
    [InlineData(409, "OperationNotAllowed", ErrorCategory.Quota)]
    [InlineData(0, "ResourceQuotaExceeded", ErrorCategory.Quota)]
    [InlineData(403, "AuthorizationFailed", ErrorCategory.Authorization)]
    [InlineData(0, "RequestDisallowedByPolicy", ErrorCategory.Authorization)]
    [InlineData(401, null, ErrorCategory.Authorization)]
    [InlineData(0, "AllocationFailed", ErrorCategory.Transient)]
    [InlineData(429, null, ErrorCategory.Transient)]
    [InlineData(503, null, ErrorCategory.Transient)]
    [InlineData(409, null, ErrorCategory.Transient)]
    [InlineData(0, "SkuNotAvailable", ErrorCategory.Permanent)]
    [InlineData(400, "InvalidTemplateDeployment", ErrorCategory.Permanent)]
    [InlineData(404, null, ErrorCategory.Permanent)]
    [InlineData(0, "SomethingNew", ErrorCategory.Transient)]
    public void Maps_azure_failures_to_retry_categories(int status, string? code, ErrorCategory expected) =>
        Assert.Equal(expected, AzureErrorClassifier.Classify(status, code));
}

public class LabDeploymentTests
{
    private static readonly AzureOptions Options = new()
    {
        SubscriptionId = "00000000-0000-0000-0000-000000000000",
        Location = "centralus",
        Environment = "dev",
        LabSubnetId = "/subscriptions/x/resourceGroups/rg/providers/Microsoft.Network/virtualNetworks/v/subnets/snet-labs",
        VmSize = "Standard_D2s_v7",
        KeyVaultUri = "https://kv.vault.azure.net/",
    };

    private static Lab NewLab() => new()
    {
        Id = Guid.Parse("0123456789abcdef0123456789abcdef"),
        OwnerId = "owner-oid",
        Name = "perf",
        Region = "centralus",
        ResourceGroupName = "rg-sqllab-perf-01234567",
        CreatedAt = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero),
        ExpiresAt = new DateTimeOffset(2026, 10, 1, 4, 0, 0, TimeSpan.Zero),
    };

    [Fact]
    public void Names_fit_azure_limits()
    {
        var lab = NewLab();
        Assert.Equal("sqllab01234567", LabDeployment.VmName(lab.Id));
        Assert.True(LabDeployment.VmName(lab.Id).Length <= 15);
        Assert.Equal("nic-sqllab01234567", LabDeployment.NicName(lab.Id));
        Assert.Equal("lab-0123456789abcdef0123456789abcdef-sql", LabDeployment.SqlSecretName(lab.Id));
    }

    [Theory]
    [InlineData("rg-sqllab-perf-01234567", true)]
    [InlineData("rg-sqllab-platform-dev", false)]
    [InlineData("rg-production", false)]
    [InlineData("rg-sqllab-perf-0123456Z", false)]
    [InlineData("rg-sqllab-../x-01234567", false)]
    public void Only_lab_resource_groups_are_accepted(string name, bool valid)
    {
        Assert.Equal(valid, LabDeployment.IsLabResourceGroup(name));
        if (!valid)
        {
            Assert.Throws<ArgumentException>(() => LabDeployment.EnsureLabResourceGroup(name));
        }
    }

    [Fact]
    public void Tags_include_every_required_key()
    {
        var tags = LabDeployment.Tags(NewLab(), "dev");
        foreach (var key in new[] { "labId", "ownerId", "environment", "createdAt", "expiresAt" })
        {
            Assert.True(tags.ContainsKey(key), key);
        }

        Assert.Equal("dev", tags["environment"]);
        Assert.Equal("2026-10-01T04:00:00.0000000Z", tags["expiresAt"]);
    }

    [Fact]
    public void Parameters_match_the_compiled_template()
    {
        using var parameters = JsonDocument.Parse(LabDeployment.Parameters(NewLab(), Options, "Admin#Pass1", "Sa#Pass1", "Sql#Pass1").ToString());
        using var template = JsonDocument.Parse(LabDeployment.Template());
        var declared = template.RootElement.GetProperty("parameters").EnumerateObject().Select(p => p.Name).Order().ToList();
        var supplied = parameters.RootElement.EnumerateObject().Select(p => p.Name).Order().ToList();
        Assert.Equal(declared, supplied);

        Assert.Equal("securestring", template.RootElement.GetProperty("parameters").GetProperty("adminPassword").GetProperty("type").GetString());
        Assert.Equal("securestring", template.RootElement.GetProperty("parameters").GetProperty("sqlPassword").GetProperty("type").GetString());
        Assert.Equal("securestring", template.RootElement.GetProperty("parameters").GetProperty("saPassword").GetProperty("type").GetString());
        Assert.Equal("centralus", parameters.RootElement.GetProperty("location").GetProperty("value").GetString());
        Assert.Equal("Standard_D2s_v7", parameters.RootElement.GetProperty("vmSize").GetProperty("value").GetString());
    }

    [Theory]
    [InlineData("Standard_D2s_v7", "NVMe")]
    [InlineData("Standard_D2as_v6", "NVMe")]
    [InlineData("Standard_D2s_v5", "SCSI")]
    [InlineData("Standard_B2ms", "SCSI")]
    public void Disk_controller_follows_the_vm_generation(string size, string expected) =>
        Assert.Equal(expected, LabDeployment.DiskControllerType(size));

    [Fact]
    public void Template_never_creates_a_public_ip()
    {
        var template = LabDeployment.Template();
        Assert.DoesNotContain("Microsoft.Network/publicIPAddresses", template);
        Assert.DoesNotContain("publicIPAddress\"", template);
    }

    [Fact]
    public void Template_is_ubuntu_with_sql_server_installed_on_first_boot()
    {
        var template = LabDeployment.Template();
        Assert.Contains("0001-com-ubuntu-server-jammy", template);
        Assert.Contains("linuxConfiguration", template);
        Assert.Contains("mssql-server-2022", template);
        Assert.Contains("ALTER LOGIN sa DISABLE", template);
        // Secrets are placeholders in the embedded script, substituted only from secure parameters at deploy time.
        Assert.Contains("__SA_PASSWORD__", template);
        Assert.DoesNotContain("Microsoft.SqlVirtualMachine", template);
    }

    [Fact]
    public void Every_lab_secret_is_cleaned_up()
    {
        var names = LabDeployment.SecretNames(NewLab().Id);
        Assert.Equal(3, names.Count);
        Assert.Contains(LabDeployment.SaSecretName(NewLab().Id), names);
    }

    [Fact]
    public void Operation_ids_round_trip_and_reject_foreign_resource_groups()
    {
        var id = new AzureOperationId(AzureOperationId.Deploy, "rg-sqllab-perf-01234567", "lab-abc").ToString();
        Assert.Equal(new AzureOperationId(AzureOperationId.Deploy, "rg-sqllab-perf-01234567", "lab-abc"), AzureOperationId.Parse(id));
        Assert.Throws<ArgumentException>(() => AzureOperationId.Parse("rg-delete|rg-sqllab-platform-dev"));
        Assert.Throws<FormatException>(() => AzureOperationId.Parse("shell|rg-sqllab-perf-01234567|rm"));
    }

    [Fact]
    public void Deployment_outputs_and_nested_errors_are_parsed()
    {
        var outputs = BinaryData.FromString("""{"vmResourceId":{"type":"String","value":"/subscriptions/x/vm"}}""");
        Assert.Equal("/subscriptions/x/vm", AzureLabInfrastructure.ReadOutput(outputs, "vmResourceId"));
        Assert.Null(AzureLabInfrastructure.ReadOutput(outputs, "missing"));

        var (code, message) = AzureLabInfrastructure.InnermostError("""
            {"code":"DeploymentFailed","message":"At least one resource failed",
             "details":[{"code":"Conflict","message":"x","details":[{"code":"SkuNotAvailable","message":"Standard_D2s_v7 is not available"}]}]}
            """);
        Assert.Equal("SkuNotAvailable", code);
        Assert.Contains("not available", message);
    }
}

public class PasswordGeneratorTests
{
    [Fact]
    public void Passwords_meet_complexity_and_are_connection_string_safe()
    {
        for (var i = 0; i < 200; i++)
        {
            var p = PasswordGenerator.Create();
            Assert.Equal(24, p.Length);
            Assert.Contains(p, char.IsUpper);
            Assert.Contains(p, char.IsLower);
            Assert.Contains(p, char.IsDigit);
            Assert.Contains(p, c => "!@#$%^*-_+".Contains(c));
            Assert.DoesNotContain(p, c => ";='\"{}".Contains(c));
        }

        Assert.NotEqual(PasswordGenerator.Create(), PasswordGenerator.Create());
    }
}

public class OrphanReconcilerTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Only_old_groups_without_a_live_lab_are_orphans()
    {
        var live = Guid.NewGuid();
        var deleted = Guid.NewGuid();
        var unknown = Guid.NewGuid();
        var failed = Guid.NewGuid();
        var groups = new List<LabResourceGroup>
        {
            new("rg-sqllab-live-00000001", live, Now.AddHours(-5)),
            new("rg-sqllab-deleted-00000002", deleted, Now.AddHours(-5)),
            new("rg-sqllab-unknown-00000003", unknown, Now.AddHours(-5)),
            new("rg-sqllab-young-00000004", Guid.NewGuid(), Now.AddMinutes(-10)),
            new("rg-sqllab-untagged-00000005", null, null),
            new("rg-sqllab-failed-00000006", failed, Now.AddHours(-5)),
        };
        var states = new Dictionary<Guid, LabState> { [live] = LabState.Ready, [deleted] = LabState.Deleted, [failed] = LabState.Failed };

        var orphans = OrphanReconciler.FindOrphans(groups, states, Now, TimeSpan.FromHours(1)).Select(o => o.Name).ToList();

        Assert.Equal(["rg-sqllab-deleted-00000002", "rg-sqllab-unknown-00000003", "rg-sqllab-untagged-00000005"], orphans);
    }
}

public class ControlDbMigratorTests
{
    [Fact]
    public void Sid_matches_sql_server_uniqueidentifier_to_varbinary()
    {
        // SELECT CAST(CAST('6f9619ff-8b86-d011-b42d-00c04fc964ff' AS uniqueidentifier) AS varbinary(16))
        //   = 0xFF19966F868B11D0B42D00C04FC964FF
        var sid = SqlServerLab.Worker.Services.ControlDbMigrator.SqlSid(Guid.Parse("6f9619ff-8b86-d011-b42d-00c04fc964ff"));
        Assert.Equal("FF19966F868B11D0B42D00C04FC964FF", Convert.ToHexString(sid));
    }
}
