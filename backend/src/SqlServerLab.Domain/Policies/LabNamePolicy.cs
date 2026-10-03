using System.Text.RegularExpressions;

namespace SqlServerLab.Domain.Policies;

public static partial class LabNamePolicy
{
    public const string Description =
        "3-30 characters: lowercase letters, digits, and hyphens; starts with a letter and ends with a letter or digit.";

    public static bool IsValid(string? name) => name is not null && NamePattern().IsMatch(name);

    [GeneratedRegex("^[a-z][a-z0-9-]{1,28}[a-z0-9]$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 100)]
    private static partial Regex NamePattern();
}
