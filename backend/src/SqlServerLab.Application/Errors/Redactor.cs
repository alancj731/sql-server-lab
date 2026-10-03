using System.Text.RegularExpressions;

namespace SqlServerLab.Application.Errors;

/// <summary>Removes secret-looking material before text is persisted, logged, or returned.</summary>
public static partial class Redactor
{
    public const string Mask = "***";
    private const int MaxLength = 500;

    public static string? Sanitize(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text;
        }

        var result = KeyValueSecret().Replace(text, m => $"{m.Groups["key"].Value}={Mask}");
        result = SasSignature().Replace(result, m => $"{m.Groups["key"].Value}={Mask}");
        result = BearerToken().Replace(result, $"Bearer {Mask}");
        return result.Length > MaxLength ? result[..MaxLength] + "…" : result;
    }

    [GeneratedRegex(
        @"(?<key>password|pwd|user\s?id|uid|accountkey|sharedaccesskey|client_?secret|secret|access_?token|token)\s*=\s*(""[^""]*""|'[^']*'|[^;&\s]+)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 200)]
    private static partial Regex KeyValueSecret();

    [GeneratedRegex(@"(?<key>sig|se|sp|sv|skoid|sktid)=[^&\s""']+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 200)]
    private static partial Regex SasSignature();

    [GeneratedRegex(@"Bearer\s+[A-Za-z0-9\-_\.=]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 200)]
    private static partial Regex BearerToken();
}
