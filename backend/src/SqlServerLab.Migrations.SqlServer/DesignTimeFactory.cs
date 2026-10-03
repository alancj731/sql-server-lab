using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using SqlServerLab.Infrastructure;
using SqlServerLab.Infrastructure.Persistence;

namespace SqlServerLab.Migrations.SqlServer;

/// <summary>Used by <c>dotnet ef</c> only; generates SQL Server migrations without a live server.</summary>
public sealed class DesignTimeFactory : IDesignTimeDbContextFactory<ControlDbContext>
{
    public ControlDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<ControlDbContext>()
            .UseSqlServer("Server=design-time;Database=controlplane;Integrated Security=false", sql => sql.MigrationsAssembly(DependencyInjection.SqlServerMigrationsAssembly))
            .Options);
}
