using Azure.Core;
using Azure.Identity;
using Azure.ResourceManager;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SqlServerLab.Application.Abstractions;
using SqlServerLab.Application.Jobs;
using SqlServerLab.Infrastructure.Azure;
using SqlServerLab.Infrastructure.Jobs;
using SqlServerLab.Infrastructure.Options;
using SqlServerLab.Infrastructure.Persistence;
using SqlServerLab.Infrastructure.Simulation;

namespace SqlServerLab.Infrastructure;

public static class DependencyInjection
{
    /// <summary>SQL Server migrations live in their own assembly; SQLite migrations stay in this one.</summary>
    public const string SqlServerMigrationsAssembly = "SqlServerLab.Migrations.SqlServer";

    /// <summary>Registers the control database, clock, job store, and the infrastructure provider for the configured mode.</summary>
    public static IServiceCollection AddControlPlane(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<ControlDbOptions>().Bind(configuration.GetSection(ControlDbOptions.SectionName)).ValidateDataAnnotations().ValidateOnStart();
        services.AddOptions<InfrastructureOptions>().Bind(configuration.GetSection(InfrastructureOptions.SectionName)).ValidateDataAnnotations().ValidateOnStart();
        services.AddOptions<LocalSimulationOptions>().Bind(configuration.GetSection(LocalSimulationOptions.SectionName)).ValidateDataAnnotations().ValidateOnStart();

        var db = configuration.GetSection(ControlDbOptions.SectionName).Get<ControlDbOptions>() ?? new ControlDbOptions();
        services.AddDbContextFactory<ControlDbContext>(options =>
        {
            if (db.Provider == "SqlServer")
            {
                options.UseSqlServer(db.ConnectionString, sql => sql.MigrationsAssembly(SqlServerMigrationsAssembly).EnableRetryOnFailure());
            }
            else
            {
                options.UseSqlite(db.ConnectionString);
            }
        });

        // Singleton factory for workers/background services; one context per request/scope for application services.
        services.AddScoped(sp => sp.GetRequiredService<IDbContextFactory<ControlDbContext>>().CreateDbContext());
        services.AddScoped<IControlDb>(sp => sp.GetRequiredService<ControlDbContext>());

        services.AddSingleton<IClock, SystemClock>();
        services.AddScoped<JobService>();
        services.AddSingleton<JobLeaseStore>();

        var mode = configuration.GetSection(InfrastructureOptions.SectionName).Get<InfrastructureOptions>()?.Mode ?? "Local";
        if (mode == "Azure")
        {
            services.AddOptions<AzureOptions>().Bind(configuration.GetSection(AzureOptions.SectionName)).ValidateDataAnnotations().ValidateOnStart();
            services.AddSingleton<TokenCredential>(sp =>
            {
                var clientId = sp.GetRequiredService<IOptions<AzureOptions>>().Value.ManagedIdentityClientId;
                // In Azure: the app's user-assigned managed identity. Locally: Azure CLI sign-in.
                return new DefaultAzureCredential(new DefaultAzureCredentialOptions { ManagedIdentityClientId = string.IsNullOrEmpty(clientId) ? null : clientId });
            });
            services.AddSingleton(sp => new ArmClient(
                sp.GetRequiredService<TokenCredential>(), sp.GetRequiredService<IOptions<AzureOptions>>().Value.SubscriptionId));
            services.AddSingleton<ISecretProvider, KeyVaultSecretProvider>();
            services.AddSingleton<ISqlLabExecutor, SqlServerLabExecutor>();
            services.AddSingleton<ILabInfrastructure, AzureLabInfrastructure>();
            services.AddSingleton<ILabResourceInventory, AzureLabResourceInventory>();
        }
        else
        {
            services.AddSingleton<ILabInfrastructure, LocalLabInfrastructure>();
            services.AddSingleton<ISqlLabExecutor, FakeSqlLabExecutor>();
            services.AddSingleton<ISecretProvider, InMemorySecretProvider>();
            services.AddSingleton<ILabResourceInventory, NoLabResourceInventory>();
        }

        // Real implementations arrive in Milestones 7–9.
        services.AddSingleton<IMetricProvider, FakeMetricProvider>();
        services.AddSingleton<IBackupProvider, FakeBackupProvider>();
        services.AddSingleton<IPatchProvider, FakePatchProvider>();
        return services;
    }
}
