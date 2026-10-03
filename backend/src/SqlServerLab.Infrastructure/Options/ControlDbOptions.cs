using System.ComponentModel.DataAnnotations;

namespace SqlServerLab.Infrastructure.Options;

public sealed class ControlDbOptions
{
    public const string SectionName = "ControlDb";

    /// <summary><c>Sqlite</c> for local development and tests, <c>SqlServer</c> for hosted (Azure SQL).</summary>
    [Required]
    [RegularExpression("^(Sqlite|SqlServer)$")]
    public string Provider { get; set; } = "Sqlite";

    /// <summary>Supplied via environment/Key Vault in hosted environments; never committed for SQL Server.</summary>
    [Required]
    public string ConnectionString { get; set; } = string.Empty;
}

public sealed class InfrastructureOptions
{
    public const string SectionName = "Infrastructure";

    /// <summary><c>Local</c> (simulated) or <c>Azure</c> (Milestone 3).</summary>
    [Required]
    [RegularExpression("^(Local|Azure)$")]
    public string Mode { get; set; } = "Local";
}

public sealed class LocalSimulationOptions
{
    public const string SectionName = "LocalSimulation";

    [Range(0, 600)] public double ProvisionSeconds { get; set; } = 8;
    [Range(0, 600)] public double StartSeconds { get; set; } = 4;
    [Range(0, 600)] public double DeallocateSeconds { get; set; } = 3;
    [Range(0, 600)] public double DeleteSeconds { get; set; } = 4;
    [Range(0, 600)] public double SqlReadySeconds { get; set; } = 3;
}
