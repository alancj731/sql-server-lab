using System.Security.Claims;
using SqlServerLab.Application.Abstractions;

namespace SqlServerLab.Api.Auth;

public sealed class HttpCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    private ClaimsPrincipal Principal =>
        accessor.HttpContext?.User ?? throw new InvalidOperationException("No HTTP context.");

    // Entra tokens carry the stable object ID in "oid"; development identities use NameIdentifier.
    public string UserId =>
        Principal.FindFirstValue("oid")
        ?? Principal.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? throw new InvalidOperationException("The caller has no user identifier.");

    public string DisplayName =>
        Principal.FindFirstValue("name") ?? Principal.FindFirstValue("preferred_username") ?? Principal.FindFirstValue(ClaimTypes.Name) ?? UserId;

    public bool IsAdmin => Principal.IsInRole(LabRoles.Admin);
}
