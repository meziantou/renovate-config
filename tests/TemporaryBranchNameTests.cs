using Meziantou.Framework.InlineSnapshotTesting;

namespace Meziantou.RenovateConfig.Tests;

public sealed class TemporaryBranchNameTests
{
    [Fact]
    public void BaseBranchRoundTrips()
    {
        var branch = TemporaryBaseBranchName.Create();

        Assert.True(TemporaryBaseBranchName.TryParse(branch.Name, out var parsed));
        Assert.Equal(branch.Name, parsed.Name);
    }

    [Fact]
    public void RenovateBranchRoundTrips()
    {
        var branch = new TemporaryRenovateBranchName(TemporaryBaseBranchName.Create());

        Assert.True(TemporaryRenovateBranchName.TryParse(branch.Prefix + "update-package", out var parsed));
        Assert.Equal(branch.Prefix, parsed.Prefix);
    }

    [Theory]
    [InlineData("main")]
    [InlineData("tests/base/not-a-date-id")]
    [InlineData("tests/base/20260607010101-not-a-guid")]
    [InlineData("tests/base/20260607010101-aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa-extra")]
    [InlineData("tests/renovate/20260607010101-aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [InlineData("tests/renovate/20260607010101-aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa-")]
    [InlineData("tests/other/20260607010101-aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa-extra")]
    public void UnrecognizedBranchesAreNotParsed(string branchName)
    {
        Assert.False(TemporaryBaseBranchName.TryParse(branchName, out _));
        Assert.False(TemporaryRenovateBranchName.TryParse(branchName, out _));
    }

    [Fact]
    public void BranchExpiresAfterTwentyFourHours()
    {
        Assert.True(TemporaryBaseBranchName.TryParse("tests/base/20260607010101-aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", out var branch));
        var createdAt = branch.Date;

        Assert.False(branch.HasExpired(createdAt + TimeSpan.FromHours(24) - TimeSpan.FromTicks(1)));
        Assert.True(branch.HasExpired(createdAt + TimeSpan.FromHours(24)));
    }

    [Theory]
    [InlineData("tests/base/20260607010101-aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", true)]
    [InlineData("tests/renovate/20260607010101-aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa-update-package", true)]
    [InlineData("tests/base/20260607010101-bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb", false)]
    [InlineData("tests/renovate/20260607010101-bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb-update-package", false)]
    [InlineData("tests/renovate/20260607010101-aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", false)]
    [InlineData("main", false)]
    public void CleanupOnlyMatchesStrictBranchesFromTheSameRun(string candidate, bool expected)
    {
        Assert.True(TemporaryBaseBranchName.TryParse("tests/base/20260607010101-aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", out var branch));

        Assert.Equal(expected, branch.IsPartOfTestRun(candidate));
    }

    [Fact]
    public void CleanupSafetySnapshot()
    {
        Assert.True(TemporaryBaseBranchName.TryParse("tests/base/20260607010101-aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", out var branch));
        var candidates = new[]
        {
            "tests/base/20260607010101-aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
            "tests/renovate/20260607010101-aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa-update-package",
            "tests/renovate/20260607010101-bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb-update-package",
            "tests/renovate/20260607010101-aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
            "main",
        };

        var result = string.Join('\n', candidates.Select(candidate => $"{candidate}: {branch.IsPartOfTestRun(candidate)}"));
        InlineSnapshot.Validate(result,
            """
            tests/base/20260607010101-aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa: True
            tests/renovate/20260607010101-aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa-update-package: True
            tests/renovate/20260607010101-bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb-update-package: False
            tests/renovate/20260607010101-aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa: False
            main: False
            """);
    }
}
