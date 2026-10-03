using SqlServerLab.Domain.Jobs;
using SqlServerLab.Domain.Policies;

namespace SqlServerLab.Domain.Tests;

public class LabJobPolicyTests
{
    [Theory]
    [InlineData(LabJobType.DeleteLab, false)]
    [InlineData(LabJobType.InstallPatches, false)]
    [InlineData(LabJobType.ProvisionLab, true)]
    [InlineData(LabJobType.StartVm, true)]
    public void Auto_retry_is_disabled_for_ambiguous_destructive_operations(LabJobType type, bool expected)
    {
        Assert.Equal(expected, LabJobPolicy.AutoRetryAllowed(type));
        Assert.Equal(expected ? 5 : 1, LabJobPolicy.MaxAttempts(type));
    }

    [Theory]
    [InlineData(ErrorCategory.Transient, 1, true)]
    [InlineData(ErrorCategory.Transient, 4, true)]
    [InlineData(ErrorCategory.Transient, 5, false)]
    [InlineData(ErrorCategory.Permanent, 1, false)]
    [InlineData(ErrorCategory.Quota, 1, false)]
    [InlineData(ErrorCategory.Authorization, 1, false)]
    [InlineData(ErrorCategory.Validation, 1, false)]
    [InlineData(ErrorCategory.Cancellation, 1, false)]
    public void Only_transient_errors_are_retried_within_the_attempt_budget(ErrorCategory category, int attempt, bool expected) =>
        Assert.Equal(expected, LabJobPolicy.ShouldRetry(LabJobType.ProvisionLab, category, attempt));

    [Fact]
    public void Delete_is_never_retried_automatically() =>
        Assert.False(LabJobPolicy.ShouldRetry(LabJobType.DeleteLab, ErrorCategory.Transient, 1));

    [Fact]
    public void Only_metric_collection_is_non_mutating()
    {
        foreach (var type in Enum.GetValues<LabJobType>())
        {
            Assert.Equal(type != LabJobType.CollectMetrics, LabJobPolicy.IsMutating(type));
        }
    }

    [Fact]
    public void Backoff_is_jittered_within_an_exponential_capped_ceiling()
    {
        var random = new Random(42);
        for (var attempt = 1; attempt <= 20; attempt++)
        {
            var ceiling = LabJobPolicy.BackoffCeiling(attempt);
            for (var i = 0; i < 50; i++)
            {
                var delay = LabJobPolicy.Backoff(attempt, random);
                Assert.InRange(delay, TimeSpan.Zero, ceiling);
            }
        }

        Assert.Equal(TimeSpan.FromSeconds(2), LabJobPolicy.BackoffCeiling(1));
        Assert.Equal(TimeSpan.FromSeconds(4), LabJobPolicy.BackoffCeiling(2));
        Assert.Equal(TimeSpan.FromMinutes(2), LabJobPolicy.BackoffCeiling(30));
        Assert.Throws<ArgumentOutOfRangeException>(() => LabJobPolicy.Backoff(0, random));
    }

    [Fact]
    public void Backoff_values_vary()
    {
        var random = new Random(7);
        var values = Enumerable.Range(0, 20).Select(_ => LabJobPolicy.Backoff(3, random)).Distinct().Count();
        Assert.True(values > 10);
    }
}

public class NamingAndExpiryPolicyTests
{
    [Theory]
    [InlineData("perf-lab-1", true)]
    [InlineData("abc", true)]
    [InlineData("ab", false)]
    [InlineData("1lab", false)]
    [InlineData("lab-", false)]
    [InlineData("Lab", false)]
    [InlineData("lab_1", false)]
    [InlineData("lab;drop", false)]
    [InlineData("a234567890123456789012345678901", false)]
    [InlineData(null, false)]
    public void Lab_names_are_restricted(string? name, bool valid) => Assert.Equal(valid, LabNamePolicy.IsValid(name));

    [Theory]
    [InlineData("eastus", true)]
    [InlineData("EastUS", false)]
    [InlineData("moon-1", false)]
    public void Regions_are_allow_listed(string region, bool valid) => Assert.Equal(valid, RegionAllowList.IsAllowed(region));

    [Fact]
    public void Resource_group_name_is_derived_from_validated_name()
    {
        var id = Guid.Parse("0123456789abcdef0123456789abcdef");
        Assert.Equal("rg-sqllab-perf-01234567", ResourceNaming.ResourceGroupName("perf", id));
        Assert.Throws<ArgumentException>(() => ResourceNaming.ResourceGroupName("../evil", id));
    }

    [Fact]
    public void Extension_starts_from_later_of_now_and_expiry_and_is_capped()
    {
        var created = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var expiry = created.AddHours(4);

        Assert.Equal(created.AddHours(6), ExpiryPolicy.Extend(created, expiry, created.AddHours(1), 2));
        Assert.Equal(created.AddHours(7), ExpiryPolicy.Extend(created, expiry, created.AddHours(5), 2));
        Assert.Equal(created.AddHours(24), ExpiryPolicy.Extend(created, created.AddHours(23), created.AddHours(22), 4));
        Assert.Throws<ArgumentOutOfRangeException>(() => ExpiryPolicy.Extend(created, expiry, created, 5));
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(8, true)]
    [InlineData(9, false)]
    public void Initial_ttl_bounds(int hours, bool valid) => Assert.Equal(valid, ExpiryPolicy.IsValidInitialTtl(hours));
}
