using Azure;
using Azure.ResourceManager;
using Azure.ResourceManager.Network;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using SqlServerLab.Application.Abstractions;
using SqlServerLab.Domain.Labs;

namespace SqlServerLab.Infrastructure.Azure;

/// <summary>
/// Connects to a lab's SQL Server over the private VNet with the lab login from Key Vault. Only fixed,
/// allow-listed statements are executed.
/// </summary>
public sealed class SqlServerLabExecutor(ArmClient arm, ISecretProvider secrets, IOptions<AzureOptions> options) : ISqlLabExecutor
{
    public async Task<bool> PingAsync(Lab lab, CancellationToken cancellationToken)
    {
        var ip = await PrivateIpAsync(lab, cancellationToken);
        if (ip is null)
        {
            return false;
        }

        var builder = new SqlConnectionStringBuilder
        {
            DataSource = $"tcp:{ip},1433",
            UserID = LabDeployment.SqlLogin,
            Password = await secrets.GetOrCreateSecretAsync(LabDeployment.SqlSecretName(lab.Id), cancellationToken),
            Encrypt = SqlConnectionEncryptOption.Mandatory,
            // Lab VMs use SQL Server's self-signed certificate and are reachable only inside the private VNet.
            TrustServerCertificate = true,
            ConnectTimeout = 10,
            ApplicationName = $"sqllab-health-{lab.Id:N}",
            Pooling = false,
        };

        try
        {
            await using var connection = new SqlConnection(builder.ConnectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = new SqlCommand("SELECT 1", connection) { CommandTimeout = 10 };
            return (int)(await command.ExecuteScalarAsync(cancellationToken) ?? 0) == 1;
        }
        catch (SqlException)
        {
            return false;
        }
    }

    private async Task<string?> PrivateIpAsync(Lab lab, CancellationToken cancellationToken)
    {
        LabDeployment.EnsureLabResourceGroup(lab.ResourceGroupName);
        var id = NetworkInterfaceResource.CreateResourceIdentifier(options.Value.SubscriptionId, lab.ResourceGroupName, LabDeployment.NicName(lab.Id));
        try
        {
            var nic = await arm.GetNetworkInterfaceResource(id).GetAsync(cancellationToken: cancellationToken);
            return nic.Value.Data.IPConfigurations.FirstOrDefault()?.PrivateIPAddress;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }
}
