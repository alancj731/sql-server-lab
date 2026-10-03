using Microsoft.Extensions.Options;
using SqlServerLab.Api.Auth;

namespace SqlServerLab.Api.Endpoints;

/// <summary>Public sign-in configuration for the SPA. Contains identifiers only — never secrets.</summary>
public sealed record ClientConfigDto(string AuthMode, string? TenantId, string? ClientId, string? Authority, string? ApiScope);

public static class ClientConfigEndpoints
{
    public static IEndpointRouteBuilder MapClientConfig(this IEndpointRouteBuilder app)
    {
        app.MapGet("/config.json", (IOptions<AuthOptions> options) =>
            {
                var auth = options.Value;
                return TypedResults.Ok(auth.Mode == "Entra"
                    ? new ClientConfigDto("Entra", auth.TenantId, auth.SpaClientId, auth.Authority?.Replace("/v2.0", string.Empty, StringComparison.Ordinal), auth.ApiScope)
                    : new ClientConfigDto("Development", null, null, null, null));
            })
            .WithTags("Environment")
            .WithName("GetClientConfig")
            .AllowAnonymous();
        return app;
    }
}
