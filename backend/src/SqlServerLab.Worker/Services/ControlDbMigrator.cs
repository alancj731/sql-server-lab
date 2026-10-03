using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using SqlServerLab.Infrastructure;
using SqlServerLab.Infrastructure.Persistence;

namespace SqlServerLab.Worker.Services;

/// <summary>
/// <c>--migrate</c> mode: applies control-database migrations and, on Azure SQL, grants the application's managed
/// identity a least-privileged database user. Runs as the migrator identity (the SQL Entra admin), then exits.
/// </summary>
public static partial class ControlDbMigrator
{
    public static async Task<int> RunAsync(IConfiguration configuration, ILogger logger, CancellationToken cancellationToken = default)
    {
        var provider = configuration["ControlDb:Provider"] ?? "Sqlite";
        var connectionString = configuration["ControlDb:ConnectionString"];
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            LogMissingConnection(logger);
            return 2;
        }

        var builder = new DbContextOptionsBuilder<ControlDbContext>();
        if (provider == "SqlServer")
        {
            builder.UseSqlServer(connectionString, sql => sql.MigrationsAssembly(DependencyInjection.SqlServerMigrationsAssembly).EnableRetryOnFailure());
        }
        else
        {
            builder.UseSqlite(connectionString);
        }

        await using var db = new ControlDbContext(builder.Options);
        await ControlDbInitializer.MigrateAsync(db, cancellationToken);
        LogMigrated(logger, provider);

        var appIdentity = configuration["Migrate:AppIdentityName"];
        var appClientId = configuration["Migrate:AppIdentityClientId"];
        if (provider == "SqlServer" && !string.IsNullOrEmpty(appIdentity))
        {
            if (!IdentityName().IsMatch(appIdentity) || !Guid.TryParse(appClientId, out var clientId))
            {
                LogInvalidIdentity(logger);
                return 3;
            }

            // CREATE USER ... WITH SID (the managed identity's client ID) avoids an Entra lookup, so the SQL server
            // needs no Directory Readers role. The name is validated and parameterised; QUOTENAME guards the DDL, and
            // the SID is hex produced from a parsed GUID.
            var sid = "0x" + Convert.ToHexString(SqlSid(clientId));
            await db.Database.ExecuteSqlRawAsync("""
                DECLARE @q nvarchar(300) = QUOTENAME({0});
                IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = {0})
                    EXEC(N'CREATE USER ' + @q + N' WITH SID = ' + {1} + N', TYPE = E');
                EXEC(N'ALTER ROLE db_datareader ADD MEMBER ' + @q);
                EXEC(N'ALTER ROLE db_datawriter ADD MEMBER ' + @q);
                """, [appIdentity, sid], cancellationToken);
            LogGranted(logger, appIdentity);
        }

        return 0;
    }

    /// <summary>Byte layout SQL Server uses for CAST(uniqueidentifier AS varbinary(16)); matches .NET's Guid.ToByteArray.</summary>
    internal static byte[] SqlSid(Guid clientId) => clientId.ToByteArray();

    [GeneratedRegex("^[A-Za-z0-9_-]{1,128}$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 100)]
    private static partial Regex IdentityName();

    [LoggerMessage(Level = LogLevel.Error, Message = "ControlDb:ConnectionString is not configured")]
    private static partial void LogMissingConnection(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Control database migrated ({Provider})")]
    private static partial void LogMigrated(ILogger logger, string provider);

    [LoggerMessage(Level = LogLevel.Error, Message = "Migrate:AppIdentityName or Migrate:AppIdentityClientId is invalid")]
    private static partial void LogInvalidIdentity(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Granted database access to {Identity}")]
    private static partial void LogGranted(ILogger logger, string identity);
}
