using Azure;
using Azure.Core;
using Azure.Security.KeyVault.Secrets;
using Microsoft.Extensions.Options;
using SqlServerLab.Application.Abstractions;

namespace SqlServerLab.Infrastructure.Azure;

/// <summary>Stores generated lab secrets in Key Vault. Values never leave the worker/API process.</summary>
public sealed class KeyVaultSecretProvider(TokenCredential credential, IOptions<AzureOptions> options) : ISecretProvider
{
    private readonly SecretClient _client = new(new Uri(options.Value.KeyVaultUri), credential);

    public async Task<string> GetOrCreateSecretAsync(string name, CancellationToken cancellationToken)
    {
        try
        {
            return (await _client.GetSecretAsync(name, cancellationToken: cancellationToken)).Value.Value;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            var secret = new KeyVaultSecret(name, PasswordGenerator.Create()) { Properties = { ContentType = "password" } };
            return (await _client.SetSecretAsync(secret, cancellationToken)).Value.Value;
        }
    }

    public async Task DeleteSecretAsync(string name, CancellationToken cancellationToken)
    {
        try
        {
            var operation = await _client.StartDeleteSecretAsync(name, cancellationToken);
            await operation.WaitForCompletionAsync(cancellationToken);
            await _client.PurgeDeletedSecretAsync(name, cancellationToken);
        }
        catch (RequestFailedException ex) when (ex.Status is 404 or 409)
        {
            // Already gone (or purge pending): nothing to clean up.
        }
    }
}
