using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace SqlServerLab.Api.Auth;

/// <summary>
/// Local-only identity: <c>X-Dev-User</c> header (or <c>dev_user</c> query on hub requests, where browsers cannot set
/// headers). Startup refuses this mode outside Development/Testing environments.
/// </summary>
public sealed partial class DevelopmentAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IOptions<AuthOptions> authOptions) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "Development";
    public const string UserHeader = "X-Dev-User";
    public const string RolesHeader = "X-Dev-Roles";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        string? user = Request.Headers[UserHeader];
        string? roles = Request.Headers[RolesHeader];
        if (string.IsNullOrEmpty(user) && Request.Path.StartsWithSegments("/hubs"))
        {
            user = Request.Query["dev_user"];
            roles = Request.Query["dev_roles"];
        }

        var isDefault = false;
        if (string.IsNullOrEmpty(user))
        {
            user = authOptions.Value.DefaultDevUser;
            isDefault = true;
        }

        if (string.IsNullOrEmpty(user))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        if (!UserPattern().IsMatch(user))
        {
            return Task.FromResult(AuthenticateResult.Fail("Invalid development user."));
        }

        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, user), new(ClaimTypes.Name, user) };
        var admin = isDefault
            ? authOptions.Value.DefaultDevUserIsAdmin
            : (roles ?? string.Empty).Split(',', StringSplitOptions.TrimEntries).Contains("admin", StringComparer.OrdinalIgnoreCase);
        if (admin)
        {
            claims.Add(new Claim(ClaimTypes.Role, LabRoles.Admin));
        }

        var identity = new ClaimsIdentity(claims, SchemeName);
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
    }

    [GeneratedRegex("^[A-Za-z0-9._@-]{1,64}$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 100)]
    private static partial Regex UserPattern();
}
