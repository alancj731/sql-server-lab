using System.Security.Cryptography;

namespace SqlServerLab.Infrastructure.Azure;

/// <summary>
/// Generates passwords that satisfy Windows and SQL Server complexity rules and are safe inside connection strings
/// (no quotes, semicolons, braces, or equals signs).
/// </summary>
public static class PasswordGenerator
{
    private const string Upper = "ABCDEFGHJKLMNPQRSTUVWXYZ";
    private const string Lower = "abcdefghijkmnopqrstuvwxyz";
    private const string Digits = "23456789";
    private const string Symbols = "!@#$%^*-_+";

    public static string Create(int length = 24)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(length, 12);
        var all = Upper + Lower + Digits + Symbols;
        var chars = new char[length];
        chars[0] = Pick(Upper);
        chars[1] = Pick(Lower);
        chars[2] = Pick(Digits);
        chars[3] = Pick(Symbols);
        for (var i = 4; i < length; i++)
        {
            chars[i] = Pick(all);
        }

        RandomNumberGenerator.Shuffle(chars.AsSpan());
        return new string(chars);
    }

    private static char Pick(string set) => set[RandomNumberGenerator.GetInt32(set.Length)];
}
