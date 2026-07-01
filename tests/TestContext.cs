using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Markdig;
using Markdig.Extensions.Tables;
using Markdig.Syntax;
using Meziantou.Framework;
using Meziantou.Framework.InlineSnapshotTesting;
using Octokit;

namespace Meziantou.RenovateConfig.Tests;

internal sealed class TestContext : IAsyncDisposable
{
    private const string InitialCommitMessage = "Initialize Renovate system test";
    private static readonly SemaphoreSlim CleanupLock = new(1, 1);

    private readonly ITestOutputHelper _output;
    private readonly TemporaryDirectory _repositoryDirectory;
    private readonly GitHubClient _github;
    private readonly string _token;
    private bool _successful;

    private TestContext(ITestOutputHelper output, TemporaryDirectory repositoryDirectory, TemporaryBaseBranchName baseBranch, GitHubClient github, string token)
    {
        _output = output;
        _repositoryDirectory = repositoryDirectory;
        BaseBranch = baseBranch;
        _github = github;
        _token = token;
    }

    public TemporaryBaseBranchName BaseBranch { get; }

    public static async Task<TestContext> CreateAsync(ITestOutputHelper output, bool automerge = false, bool ignoreMinimumReleaseAge = false)
    {
        var token = Environment.GetEnvironmentVariable("RENOVATE_TEST_TOKEN");
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new InvalidOperationException("RENOVATE_TEST_TOKEN is required to run live Renovate system tests.");
        }

        var github = new GitHubClient(new ProductHeaderValue("meziantou-renovate-config-tests"))
        {
            Credentials = new Octokit.Credentials(token),
        };

        await CleanupLock.WaitAsync();
        try
        {
            await CleanupExpiredBranches(output, github);
        }
        finally
        {
            CleanupLock.Release();
        }

        var repositoryDirectory = TemporaryDirectory.Create();
        var baseBranch = TemporaryBaseBranchName.Create();
        output.WriteLine($"Test base branch: {baseBranch.Name}");

        await ExecuteCommand(output, "git", ["-C", repositoryDirectory.FullPath, "init", $"--initial-branch={baseBranch.Name}"]);
        File.WriteAllText(Path.Combine(repositoryDirectory.FullPath, ".git", "test-global-config"), string.Empty);
        WriteRenovateConfig(repositoryDirectory.FullPath, automerge, ignoreMinimumReleaseAge);

        return new TestContext(output, repositoryDirectory, baseBranch, github, token);
    }

    public void AddFile(string path, string content)
    {
        _repositoryDirectory.CreateTextFile(path, content);
    }

    public async Task PushAsync()
    {
        await ExecuteCommand(_output, "git", ["-C", _repositoryDirectory.FullPath, "add", "."]);
        await ExecuteCommand(_output, "git",
        [
            "-C", _repositoryDirectory.FullPath,
            "-c", "user.email=renovate-tests@meziantou.net",
            "-c", "user.name=Renovate system tests",
            "-c", "commit.gpgsign=false",
            "commit", "--message", InitialCommitMessage,
        ]);

        var remote = $"https://x-access-token:{_token}@github.com/{TestRepository.FullName}.git";
        await ExecuteCommand(_output, "git", ["-C", _repositoryDirectory.FullPath, "push", remote, $"{BaseBranch.Name}:{BaseBranch.Name}"]);
    }

    public async Task RunRenovateAsync()
    {
        var renovateBranch = new TemporaryRenovateBranchName(BaseBranch);
        await ExecuteCommand(
            _output,
            "npx",
            ["--no-install", "renovate", TestRepository.FullName, "--base-dir", _repositoryDirectory.FullPath],
            [
                new("LOG_LEVEL", "debug"),
                new("RENOVATE_TOKEN", _token),
                new("RENOVATE_BASE_BRANCHES", JsonSerializer.Serialize(new[] { BaseBranch.Name })),
                new("RENOVATE_BRANCH_PREFIX", renovateBranch.Prefix),
                new("RENOVATE_USE_BASE_BRANCH_CONFIG", "merge"),
                new("RENOVATE_INHERIT_CONFIG", "false"),
                new("RENOVATE_ONBOARDING", "false"),
                new("RENOVATE_REQUIRE_CONFIG", "optional"),
                new("RENOVATE_RECREATE_WHEN", "always"),
                new("RENOVATE_PR_CREATION", "immediate"),
                new("RENOVATE_PR_HOURLY_LIMIT", "0"),
                new("RENOVATE_PR_CONCURRENT_LIMIT", "0"),
                new("RENOVATE_BRANCH_CONCURRENT_LIMIT", "0"),
                new("RENOVATE_PRUNE_STALE_BRANCHES", "false"),
                new("RENOVATE_LABELS", """["renovate-test"]"""),
                new("GIT_CONFIG_COUNT", "1"),
                new("GIT_CONFIG_KEY_0", "commit.gpgsign"),
                new("GIT_CONFIG_VALUE_0", "false"),
                new("GIT_CONFIG_GLOBAL", Path.Combine(_repositoryDirectory.FullPath, ".git", "test-global-config")),
                new("GIT_CONFIG_NOSYSTEM", "1"),
                new("HOME", _repositoryDirectory.FullPath),
                new("XDG_CONFIG_HOME", Path.Combine(_repositoryDirectory.FullPath, ".config")),
            ]);
    }

    public async Task<IReadOnlyList<PullRequestInfo>> GetPullRequestsAsync(ItemStateFilter state = ItemStateFilter.All)
    {
        var pullRequests = await _github.PullRequest.GetAllForRepository(
            TestRepository.Owner,
            TestRepository.Name,
            new PullRequestRequest { Base = BaseBranch.Name, State = state });

        var pipeline = new MarkdownPipelineBuilder().UseAdvancedExtensions().Build();
        var results = new List<PullRequestInfo>(pullRequests.Count);

        foreach (var pullRequest in pullRequests.OrderBy(static item => item.Title, StringComparer.Ordinal))
        {
            var commits = await _github.PullRequest.Commits(TestRepository.Owner, TestRepository.Name, pullRequest.Number);
            var merged = await _github.PullRequest.Merged(TestRepository.Owner, TestRepository.Name, pullRequest.Number);
            var updates = new List<PackageUpdateInfo>();
            var markdown = Markdown.Parse(pullRequest.Body ?? string.Empty, pipeline);
            var table = markdown.OfType<Table>().FirstOrDefault();
            if (table is not null)
            {
                var rows = table.OfType<TableRow>().ToArray();
                if (rows.Length > 0)
                {
                    var headers = rows[0].Select(static cell => cell.InnerText()).ToArray();
                    foreach (var row in rows.Skip(1))
                    {
                        updates.Add(new PackageUpdateInfo(
                            GetCell("Package")?.InnerText().Replace("(source)", string.Empty, StringComparison.Ordinal).Trim(),
                            GetCell("Type")?.InnerText().Trim(),
                            ScrubVersions(GetCell("Update")?.InnerText().Trim())));

                        MarkdownObject? GetCell(string header)
                        {
                            var index = Array.IndexOf(headers, header);
                            return index >= 0 && index < row.Count ? row[index] : null;
                        }
                    }
                }
            }

            results.Add(new PullRequestInfo(
                ScrubVersions(pullRequest.Title)!,
                pullRequest.Labels.Select(static label => label.Name).Order(StringComparer.Ordinal).ToArray(),
                updates.OrderBy(static update => update.Package, StringComparer.OrdinalIgnoreCase).ThenBy(static update => update.Type, StringComparer.Ordinal).ToArray(),
                commits.Select(static commit => ScrubVersions(commit.Commit.Message)!).ToArray(),
                merged));
        }

        return results;
    }

    [InlineSnapshotAssertion(nameof(expected))]
    [SuppressMessage("Usage", "CA1801:Review unused parameters", Justification = "Caller info is forwarded to InlineSnapshot")]
    public async Task AssertPullRequestsAsync(string? expected = null, [CallerFilePath] string? filePath = null, [CallerLineNumber] int lineNumber = -1)
    {
        var pullRequests = await GetPullRequestsAsync();
        InlineSnapshot.Validate(pullRequests, expected, filePath, lineNumber);
    }

    public async Task AssertPackagesDetectedAsync(params string[] expectedPackages)
    {
        var pullRequests = await GetPullRequestsAsync();
        var actual = pullRequests
            .SelectMany(static pullRequest => pullRequest.PackageUpdates)
            .Select(static update => update.Package)
            .Where(static package => package is not null)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        foreach (var expectedPackage in expectedPackages)
        {
            Assert.Contains(expectedPackage, actual, StringComparer.OrdinalIgnoreCase);
        }
    }

    public async Task AssertPackagesSharePullRequestAsync(params string[] expectedPackages)
    {
        var pullRequests = await GetPullRequestsAsync();
        Assert.Contains(pullRequests, pullRequest =>
        {
            var packages = pullRequest.PackageUpdates.Select(static update => update.Package).ToHashSet(StringComparer.OrdinalIgnoreCase);
            return expectedPackages.All(packages.Contains);
        });
    }

    public async Task AssertPullRequestTitleContainsAsync(string value)
    {
        var pullRequests = await GetPullRequestsAsync();
        Assert.Contains(pullRequests, pullRequest => pullRequest.Title.Contains(value, StringComparison.OrdinalIgnoreCase));
    }

    public async Task AssertOpenPullRequestsHaveExpectedMetadataAsync()
    {
        var pullRequests = await GetPullRequestsAsync(ItemStateFilter.Open);
        Assert.NotEmpty(pullRequests);
        Assert.All(pullRequests, static pullRequest =>
        {
            Assert.Contains("renovate-test", pullRequest.Labels);
            Assert.NotEmpty(pullRequest.PackageUpdates);
            Assert.NotEmpty(pullRequest.Commits);
            Assert.False(pullRequest.Merged);
        });
    }

    public async Task AssertAutomergeCompletedAsync(string obsoleteFileContent)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            await RunRenovateAsync();

            var contents = await _github.Repository.Content.GetAllContentsByRef(TestRepository.Owner, TestRepository.Name, "project.csproj", BaseBranch.Name);
            var content = contents.Single().Content;
            if (!content.Contains(obsoleteFileContent, StringComparison.Ordinal))
            {
                var openPullRequests = await GetPullRequestsAsync(ItemStateFilter.Open);
                Assert.Empty(openPullRequests);
                var pullRequests = await GetPullRequestsAsync();
                Assert.Contains(pullRequests, static pullRequest => pullRequest.Merged && pullRequest.Commits.Count > 0);
                return;
            }

            await Task.Delay(TimeSpan.FromSeconds(5));
        }

        Assert.Fail($"Renovate did not merge an update that replaces '{obsoleteFileContent}' after five runs.");
    }

    public void MarkSuccessful()
    {
        _successful = true;
    }

    public async ValueTask DisposeAsync()
    {
        await _repositoryDirectory.DisposeAsync();
        if (_successful)
        {
            await CleanupTestRun(_output, _github, BaseBranch);
        }
        else
        {
            _output.WriteLine($"Keeping failed test artifacts for {BaseBranch.Name}");
        }
    }

    private static void WriteRenovateConfig(string destination, bool automerge, bool ignoreMinimumReleaseAge)
    {
        var root = GetGitRoot();
        var config = JsonNode.Parse(File.ReadAllText(Path.Combine(root, "default.json")))!.AsObject();
        config["schedule"] = new JsonArray("at any time");

        if (automerge)
        {
            var automergeConfig = JsonNode.Parse(File.ReadAllText(Path.Combine(root, "default-automerge.json")))!.AsObject();
            var packageRules = config["packageRules"]!.AsArray();
            foreach (var rule in automergeConfig["packageRules"]!.AsArray())
            {
                packageRules.Add(rule!.DeepClone());
            }

            config["ignoreTests"] = true;
            config["platformAutomerge"] = false;
        }

        if (ignoreMinimumReleaseAge)
        {
            config["packageRules"]!.AsArray().Add(JsonNode.Parse("""{"matchPackageNames":["/.*/"],"minimumReleaseAge":null}"""));
        }

        File.WriteAllText(Path.Combine(destination, "renovate.json"), config.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }

    private static string GetGitRoot()
    {
        var current = AppContext.BaseDirectory;
        while (current is not null)
        {
            if (Directory.Exists(Path.Combine(current, ".git")) || File.Exists(Path.Combine(current, ".git")))
            {
                return current;
            }

            current = Directory.GetParent(current)?.FullName;
        }

        throw new InvalidOperationException("Git repository root not found.");
    }

    private static async Task CleanupExpiredBranches(ITestOutputHelper output, GitHubClient github)
    {
        var branches = await github.Repository.Branch.GetAll(TestRepository.Owner, TestRepository.Name);
        var expiredBases = branches
            .Select(static branch => TemporaryBaseBranchName.TryParse(branch.Name, out var parsed) ? parsed : null)
            .Where(static branch => branch is not null && branch.HasExpired(DateTimeOffset.UtcNow))
            .ToArray();

        foreach (var baseBranch in expiredBases)
        {
            await CleanupTestRun(output, github, baseBranch!);
        }

        foreach (var branch in branches)
        {
            if (TemporaryRenovateBranchName.TryParse(branch.Name, out var renovateBranch) && renovateBranch.HasExpired(DateTimeOffset.UtcNow))
            {
                await DeleteBranch(output, github, branch.Name);
            }
        }
    }

    private static async Task CleanupTestRun(ITestOutputHelper output, GitHubClient github, TemporaryBaseBranchName baseBranch)
    {
        var pullRequests = await github.PullRequest.GetAllForRepository(
            TestRepository.Owner,
            TestRepository.Name,
            new PullRequestRequest { Base = baseBranch.Name, State = ItemStateFilter.Open });

        foreach (var pullRequest in pullRequests)
        {
            await github.PullRequest.Update(TestRepository.Owner, TestRepository.Name, pullRequest.Number, new PullRequestUpdate { State = ItemState.Closed });
        }

        var branches = await github.Repository.Branch.GetAll(TestRepository.Owner, TestRepository.Name);
        foreach (var branch in branches)
        {
            if (baseBranch.IsPartOfTestRun(branch.Name))
            {
                await DeleteBranch(output, github, branch.Name);
            }
        }
    }

    private static async Task DeleteBranch(ITestOutputHelper output, GitHubClient github, string branchName)
    {
        output.WriteLine($"Deleting test branch {branchName}");
        try
        {
            await github.Git.Reference.Delete(TestRepository.Owner, TestRepository.Name, "heads/" + branchName);
        }
        catch (NotFoundException)
        {
            // A Renovate run can delete its branch before cleanup observes it.
        }
    }

    private static string? ScrubVersions(string? value)
    {
        return value is null
            ? null
            : Regex.Replace(value, @"(?<![\w])v?\d+(?:\.\d+)+(?:[-+][\w.-]+)?", "version", RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));
    }

    private static async Task ExecuteCommand(ITestOutputHelper output, string executable, string[] arguments, KeyValuePair<string, string>[]? environmentVariables = null)
    {
        var result = await ProcessWrapper.Create(executable)
            .WithArguments(arguments)
            .WithEnvironmentVariables(builder =>
            {
                foreach (var pair in environmentVariables ?? [])
                {
                    builder.Set(pair.Key, pair.Value);
                }
            })
            .AddOutputStream(OutputTarget.ToTextDelegate(line => output.WriteLine($"[stdout] {line}")))
            .AddErrorStream(OutputTarget.ToTextDelegate(line => output.WriteLine($"[stderr] {line}")))
            .WithValidation(ProcessValidationMode.None)
            .ExecuteBufferedAsync(Xunit.TestContext.Current.CancellationToken);

        if (result.ExitCode != 0)
        {
            Assert.Fail($"Command '{executable} {string.Join(' ', arguments)}' exited with code {result.ExitCode}. Output:\n{result}");
        }
    }
}
