using Azure.Monitor.OpenTelemetry.Exporter;
using Microsoft.EntityFrameworkCore;
using OpenTelemetry;
using OpenTelemetry.Logs;
using SqlServerLab.Infrastructure;
using SqlServerLab.Infrastructure.Persistence;
using SqlServerLab.Worker;
using SqlServerLab.Worker.Jobs;
using SqlServerLab.Worker.Services;

var builder = Host.CreateApplicationBuilder(args);

if (args.Contains("--migrate"))
{
    // One-shot mode used by the deployment's migration job.
    using var loggerFactory = LoggerFactory.Create(b => b.AddConsole());
    return await ControlDbMigrator.RunAsync(builder.Configuration, loggerFactory.CreateLogger("Migrate"));
}

builder.Services.AddControlPlane(builder.Configuration);
builder.Services.AddJobProcessing(builder.Configuration);
builder.Services.AddHostedService<JobRunner>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<ExpiryService>());
builder.Services.AddHostedService(sp => sp.GetRequiredService<OrphanReconciler>());

if (!string.IsNullOrEmpty(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]))
{
    builder.Logging.AddOpenTelemetry(o => o.AddOtlpExporter());
}

if (!string.IsNullOrEmpty(builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"]))
{
    builder.Services.AddOpenTelemetry().UseAzureMonitorExporter();
}

var host = builder.Build();

if (builder.Configuration.GetValue<bool>("ControlDb:MigrateOnStartup"))
{
    var factory = host.Services.GetRequiredService<IDbContextFactory<ControlDbContext>>();
    await using var db = await factory.CreateDbContextAsync();
    await ControlDbInitializer.MigrateAsync(db);
}

await host.RunAsync();
return 0;
