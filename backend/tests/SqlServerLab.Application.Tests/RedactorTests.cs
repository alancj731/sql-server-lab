using SqlServerLab.Application.Errors;

namespace SqlServerLab.Application.Tests;

public class RedactorTests
{
    [Theory]
    [InlineData("Server=x;User ID=sa;Password=Sup3r$ecret;", "Sup3r$ecret")]
    [InlineData("login failed pwd='hunter2' for user", "hunter2")]
    [InlineData("https://acct.blob.core.windows.net/c/b.bak?sv=2024&sig=AbC%2Bd&se=2026", "AbC%2Bd")]
    [InlineData("AccountKey=abc123==;EndpointSuffix=core", "abc123==")]
    [InlineData("Authorization: Bearer eyJhbGciOi.payload.sig", "eyJhbGciOi")]
    [InlineData("client_secret=s3cr3t&grant_type=x", "s3cr3t")]
    public void Secrets_are_masked(string input, string secret)
    {
        var output = Redactor.Sanitize(input);
        Assert.DoesNotContain(secret, output);
        Assert.Contains(Redactor.Mask, output);
    }

    [Fact]
    public void Plain_text_is_unchanged_and_long_text_truncated()
    {
        Assert.Equal("VM image unavailable", Redactor.Sanitize("VM image unavailable"));
        Assert.Null(Redactor.Sanitize(null));
        Assert.True(Redactor.Sanitize(new string('x', 2000))!.Length <= 501);
    }
}
