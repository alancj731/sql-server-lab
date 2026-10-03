using System.ComponentModel.DataAnnotations;

namespace SqlServerLab.Api.Auth;

public sealed class AuthOptions
{
    public const string SectionName = "Authentication";

    /// <summary><c>Development</c> (header-based, local only) or <c>Entra</c> (JWT bearer).</summary>
    [Required]
    [RegularExpression("^(Development|Entra)$")]
    public string Mode { get; set; } = "Entra";

    /// <summary>Development mode only: identity used when no X-Dev-User header is sent. Leave empty to require the header.</summary>
    public string? DefaultDevUser { get; set; }

    /// <summary>Development mode only: grant the admin role to the default user.</summary>
    public bool DefaultDevUserIsAdmin { get; set; }

    public string? Authority { get; set; }

    /// <summary>Client ID of the API app registration (token audience).</summary>
    public string? Audience { get; set; }

    public string? TenantId { get; set; }

    /// <summary>Client ID of the SPA app registration. Public identifier, served to the browser via /config.json.</summary>
    public string? SpaClientId { get; set; }

    /// <summary>Delegated scope the SPA requests, e.g. api://{audience}/access_as_user.</summary>
    public string? ApiScope { get; set; }
}

public static class LabRoles
{
    public const string Admin = "LabAdmin";
}

public static class LabPolicies
{
    public const string User = "LabUser";
    public const string Admin = "LabAdmin";
}
