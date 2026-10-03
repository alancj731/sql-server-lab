using System.Text.RegularExpressions;
using SqlServerLab.Application.Abstractions;

namespace SqlServerLab.Api.Http;

public sealed class HttpCorrelationContext(IHttpContextAccessor accessor) : ICorrelationContext
{
    public string CorrelationId =>
        accessor.HttpContext?.Items[CorrelationMiddleware.ItemKey] as string ?? Guid.NewGuid().ToString("N");
}

/// <summary>Accepts a well-formed X-Correlation-ID or generates one; echoes it and adds it to the log scope.</summary>
public sealed partial class CorrelationMiddleware(RequestDelegate next, ILogger<CorrelationMiddleware> logger)
{
    public const string HeaderName = "X-Correlation-ID";
    public const string ItemKey = "CorrelationId";

    public async Task InvokeAsync(HttpContext context)
    {
        string? incoming = context.Request.Headers[HeaderName];
        var id = incoming is not null && SafeId().IsMatch(incoming) ? incoming : Guid.NewGuid().ToString("N");
        context.Items[ItemKey] = id;
        context.Response.Headers[HeaderName] = id;
        using (logger.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = id }))
        {
            await next(context);
        }
    }

    [GeneratedRegex("^[A-Za-z0-9-]{8,64}$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 100)]
    private static partial Regex SafeId();
}

public sealed class SecurityHeadersMiddleware(RequestDelegate next)
{
    public Task InvokeAsync(HttpContext context)
    {
        var headers = context.Response.Headers;
        headers.XContentTypeOptions = "nosniff";
        headers.XFrameOptions = "DENY";
        headers["Referrer-Policy"] = "no-referrer";
        headers["Cross-Origin-Opener-Policy"] = "same-origin";
        if (context.Request.Path.StartsWithSegments("/api"))
        {
            headers.ContentSecurityPolicy = "default-src 'none'; frame-ancestors 'none'";
            headers.CacheControl = "no-store";
        }
        else
        {
            // SPA: own origin, Google Fonts, and Entra sign-in endpoints for MSAL token requests.
            headers.ContentSecurityPolicy =
                "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline' https://fonts.googleapis.com; " +
                "font-src 'self' https://fonts.gstatic.com; img-src 'self' data:; " +
                "connect-src 'self' https://login.microsoftonline.com wss:; frame-src https://login.microsoftonline.com; " +
                "frame-ancestors 'none'; base-uri 'self'; form-action 'self'";
        }

        return next(context);
    }
}
