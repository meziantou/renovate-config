namespace Meziantou.RenovateConfig.Tests;

public sealed class SystemTests(ITestOutputHelper output)
{
    [Fact]
    [Trait("Category", "Live")]
    public async Task DefaultPresetGroupsConfiguredDependenciesAndAppliesReplacements()
    {
        await using var context = await TestContext.CreateAsync(output, ignoreMinimumReleaseAge: true);
        context.AddFile("project.csproj",
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <PackageReference Include="Meziantou.Framework.FullPath" Version="1.0.0" />
                <PackageReference Include="Meziantou.Framework.TemporaryDirectory" Version="1.0.0" />
                <PackageReference Include="Meziantou.ProjectConfiguration" Version="1.0.0" />
                <PackageReference Include="XUnitToFluentAssertionsAnalyzer" Version="1.0.0" />
              </ItemGroup>
            </Project>
            """);
        context.AddFile("Dockerfile",
            """
            FROM mcr.microsoft.com/dotnet/sdk:8.0.100
            FROM mcr.microsoft.com/dotnet/aspnet:8.0.0
            """);

        await context.PushAsync();
        await context.RunRenovateAsync();

        await context.AssertPackagesSharePullRequestAsync("Meziantou.Framework.FullPath", "Meziantou.Framework.TemporaryDirectory");
        await context.AssertPackagesSharePullRequestAsync("mcr.microsoft.com/dotnet/sdk", "mcr.microsoft.com/dotnet/aspnet");
        await context.AssertPullRequestTitleContainsAsync("Meziantou.DotNet.CodingStandard");
        await context.AssertPullRequestTitleContainsAsync("Meziantou.FluentAssertionsAnalyzers");
        await context.AssertOpenPullRequestsHaveExpectedMetadataAsync();
        context.MarkSuccessful();
    }

    [Fact]
    [Trait("Category", "Live")]
    public async Task DefaultPresetPinsRangedDependenciesTogether()
    {
        await using var context = await TestContext.CreateAsync(output, ignoreMinimumReleaseAge: true);
        context.AddFile("package.json",
            """
            {
              "dependencies": {
                "@azure/msal-browser": "^3.13.0",
                "@azure/msal-react": "~2.0.15"
              }
            }
            """);

        await context.PushAsync();
        await context.RunRenovateAsync();

        await context.AssertPackagesSharePullRequestAsync("@azure/msal-browser", "@azure/msal-react");
        await context.AssertOpenPullRequestsHaveExpectedMetadataAsync();
        context.MarkSuccessful();
    }

    [Fact]
    [Trait("Category", "Live")]
    public async Task CustomRegexManagersDetectDependencies()
    {
        await using var context = await TestContext.CreateAsync(output, ignoreMinimumReleaseAge: true);
        context.AddFile("package.nuspec",
            """
            <package>
              <metadata>
                <dependencies>
                  <dependency id="XUnitToFluentAssertionsAnalyzer" version="1.0.0" />
                </dependencies>
              </metadata>
            </package>
            """);
        context.AddFile("Container.cs",
            """
            internal static class Container
            {
                public const string Image = "ghcr.io/meziantou/meziantou-git-hub-actions-tracing:1.0.0";
                public static readonly object RedisImage = ImageSource.FromRegistry("redis:8.2");
            }
            """);
        context.AddFile("install.sh",
            """
            curl -L https://github.com/astral-sh/uv/releases/download/0.1.0/uv-installer.sh
            """);

        await context.PushAsync();
        await context.RunRenovateAsync();

        await context.AssertPackagesDetectedAsync(
            "XUnitToFluentAssertionsAnalyzer",
            "ghcr.io/meziantou/meziantou-git-hub-actions-tracing",
            "redis",
            "astral-sh/uv");
        await context.AssertOpenPullRequestsHaveExpectedMetadataAsync();
        context.MarkSuccessful();
    }

    [Fact]
    [Trait("Category", "Live")]
    public async Task AutomergePresetMergesEligibleUpdate()
    {
        await using var context = await TestContext.CreateAsync(output, automerge: true);
        context.AddFile("project.csproj",
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <PackageReference Include="Meziantou.Framework.FullPath" Version="1.0.0" />
              </ItemGroup>
            </Project>
            """);

        await context.PushAsync();
        await context.AssertAutomergeCompletedAsync("Version=\"1.0.0\"");
        context.MarkSuccessful();
    }
}
