using System.Reflection;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Azure.Monitor.OpenTelemetry.AspNetCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using SqlServerLab.Api.Auth;
using SqlServerLab.Api.Endpoints;
using SqlServerLab.Api.Http;
using SqlServerLab.Api.Realtime;
using SqlServerLab.Application.Abstractions;
using SqlServerLab.Application.Labs;
using SqlServerLab.Application.Options;
using SqlServerLab.Domain.Policies;
using SqlServerLab.Infrastructure;
using SqlServerLab.Infrastructure.Options;
using SqlServerLab.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);
var configuration = builder.Configuration;

// Build-time OpenAPI generation boots the host briefly; skip side effects (migrations, background polling).
var isOpenApiGeneration = Assembly.GetEntryAssembly()?.GetName().Name == "GetDocument.Insider";

// Control plane, application services, request context.
builder.Services.AddControlPlane(configuration);
builder.Services.AddOptions<LabLimitsOptions>().Bind(configuration.GetSection(LabLimitsOptions.SectionName)).ValidateDataAnnotations().ValidateOnStart();
builder.Services.AddOptions<AuthOptions>().Bind(configuration.GetSection(AuthOptions.SectionName)).ValidateDataAnnotations().ValidateOnStart();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, HttpCurrentUser>();
builder.Services.AddScoped<ICorrelationContext, HttpCorrelationContext>();
builder.Services.AddScoped<LabService>();
builder.Services.AddSingleton(sp =>
{
    var infra = sp.GetRequiredService<IOptions<InfrastructureOptions>>().Value;
    var auth = sp.GetRequiredService<IOptions<AuthOptions>>().Value;
    var limits = sp.GetRequiredService<IOptions<LabLimitsOptions>>().Value;
    return new EnvironmentDto(infra.Mode, sp.GetRequiredService<ILabInfrastructure>().IsSimulated, auth.Mode,
        limits.EffectiveRegions(), ExpiryPolicy.MinInitialHours, ExpiryPolicy.MaxInitialHours, ExpiryPolicy.MaxExtensionHours,
        limits.MaxActiveLabsPerUser);
});

// Authentication and authorization.
var authMode = configuration[$"{AuthOptions.SectionName}:Mode"] ?? "Entra";
if (authMode == "Development")
{
    if (!builder.Environment.IsDevelopment() && !builder.Environment.IsEnvironment("Testing"))
    {
        throw new InvalidOperationException("Authentication:Mode=Development is only allowed in the Development or Testing environment.");
    }

    builder.Services.AddAuthentication(DevelopmentAuthenticationHandler.SchemeName)
        .AddScheme<AuthenticationSchemeOptions, DevelopmentAuthenticationHandler>(DevelopmentAuthenticationHandler.SchemeName, null);
}
else
{
    builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddJwtBearer(options =>
        {
            var audience = configuration[$"{AuthOptions.SectionName}:Audience"];
            options.Authority = configuration[$"{AuthOptions.SectionName}:Authority"];
            options.MapInboundClaims = false;
            // v2 tokens carry the API client ID as audience; v1 tokens carry the api:// identifier URI.
            options.TokenValidationParameters.ValidAudiences = [audience, $"api://{audience}"];
            options.TokenValidationParameters.RoleClaimType = "roles";
            options.TokenValidationParameters.NameClaimType = "name";
            options.Events = new JwtBearerEvents
            {
                // Browsers cannot set headers on WebSocket requests; SignalR sends the token in the query string.
                OnMessageReceived = context =>
                {
                    var token = context.Request.Query["access_token"];
                    if (!string.IsNullOrEmpty(token) && context.HttpContext.Request.Path.StartsWithSegments("/hubs"))
                    {
                        context.Token = token;
                    }

                    return Task.CompletedTask;
                },
            };
        });
}

builder.Services.AddAuthorizationBuilder()
    .AddPolicy(LabPolicies.User, p => p.RequireAuthenticatedUser())
    .AddPolicy(LabPolicies.Admin, p => p.RequireRole(LabRoles.Admin));

// HTTP surface.
builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
    o.SerializerOptions.NumberHandling = JsonNumberHandling.Strict;
});
builder.Services.AddProblemDetails(o => o.CustomizeProblemDetails = ctx =>
{
    if (ctx.HttpContext.Items[CorrelationMiddleware.ItemKey] is string correlationId)
    {
        ctx.ProblemDetails.Extensions["correlationId"] = correlationId;
    }
});
builder.Services.AddExceptionHandler<AppExceptionHandler>();
builder.Services.AddOpenApi(o => o.AddDocumentTransformer((doc, _, _) =>
{
    doc.Info.Title = "SQL Server Performance Lab API";
    doc.Info.Version = "v1";
    return Task.CompletedTask;
}));
builder.Services.AddSignalR().AddJsonProtocol(o => o.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
if (!isOpenApiGeneration)
{
    builder.Services.AddHostedService<JobProgressBroadcaster>();
}

builder.Services.AddHealthChecks().AddDbContextCheck<ControlDbContext>("control-db", tags: ["ready"]);

var mutationsPerMinute = configuration.GetValue("RateLimiting:MutationsPerMinute", 30);
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.AddPolicy(LabEndpoints.MutationRateLimit, context => RateLimitPartition.GetFixedWindowLimiter(
        context.User.Identity?.Name ?? context.Connection.RemoteIpAddress?.ToString() ?? "anonymous",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = mutationsPerMinute, Window = TimeSpan.FromMinutes(1) }));
});

var allowedOrigins = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod().AllowCredentials()));

// Observability: OTLP export only when an endpoint is configured.
var otel = builder.Services.AddOpenTelemetry()
    .WithTracing(t => t.AddAspNetCoreInstrumentation().AddHttpClientInstrumentation())
    .WithMetrics(m => m.AddAspNetCoreInstrumentation());
if (!string.IsNullOrEmpty(configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]))
{
    otel.UseOtlpExporter();
}

if (!string.IsNullOrEmpty(configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"]))
{
    otel.UseAzureMonitor();
}

builder.Logging.AddOpenTelemetry(o => o.IncludeScopes = true);

var app = builder.Build();

if (configuration.GetValue<bool>("ControlDb:MigrateOnStartup") && !isOpenApiGeneration)
{
    await using var scope = app.Services.CreateAsyncScope();
    await ControlDbInitializer.MigrateAsync(scope.ServiceProvider.GetRequiredService<ControlDbContext>());
}

app.UseMiddleware<CorrelationMiddleware>();
app.UseMiddleware<SecurityHeadersMiddleware>();
app.UseExceptionHandler();
app.UseStatusCodePages();
if (!app.Environment.IsDevelopment() && !app.Environment.IsEnvironment("Testing"))
{
    app.UseHsts();
    app.UseHttpsRedirection();
}

// The Angular build is served from wwwroot in the container image: one origin, no CORS.
app.UseDefaultFiles();
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = ctx =>
    {
        // Hashed bundles are immutable; index.html must always be revalidated.
        ctx.Context.Response.Headers.CacheControl = ctx.File.Name == "index.html" ? "no-cache" : "public, max-age=31536000, immutable";
    },
});
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

var api = app.MapGroup("/api/v1");
api.MapLabEndpoints();
api.MapJobEndpoints();
api.MapEnvironmentEndpoints();
app.MapClientConfig();
app.MapHub<LabsHub>("/hubs/labs");
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = c => c.Tags.Contains("ready") });

// Client-side routes fall back to the SPA; API and hub paths never do.
app.MapFallbackToFile("{*path:regex(^(?!api/|hubs/|health/|openapi/).*$)}", "index.html");

await app.RunAsync();

/// <summary>Entry point marker for WebApplicationFactory.</summary>
public partial class Program;
