using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace SqlServerLab.Infrastructure.Persistence;

public static class ControlDbInitializer
{
    /// <summary>
    /// Applies migrations. Called automatically only in local/dev mode; hosted environments run migrations as an
    /// explicit deployment step (see docs/operations/deploy.md).
    /// </summary>
    public static async Task MigrateAsync(ControlDbContext db, CancellationToken cancellationToken = default)
    {
        if (db.Database.IsSqlite())
        {
            var dataSource = new SqliteConnectionStringBuilder(db.Database.GetConnectionString()).DataSource;
            var directory = Path.GetDirectoryName(Path.GetFullPath(dataSource));
            if (dataSource != ":memory:" && !string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            // WAL lets the API and worker processes read and write the same file concurrently.
            await db.Database.OpenConnectionAsync(cancellationToken);
            try
            {
                await db.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;", cancellationToken);
            }
            finally
            {
                await db.Database.CloseConnectionAsync();
            }
        }

        await db.Database.MigrateAsync(cancellationToken);
    }
}
