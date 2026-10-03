using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace SqlServerLab.Infrastructure.Persistence;

/// <summary>Used by <c>dotnet ef</c> only; generates SQLite migrations without starting the API.</summary>
public sealed class DesignTimeFactory : IDesignTimeDbContextFactory<ControlDbContext>
{
    public ControlDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<ControlDbContext>().UseSqlite("Data Source=design-time.db").Options);
}
