namespace Meziantou.RenovateConfig.Tests;

internal sealed record PullRequestInfo(
    string Title,
    IReadOnlyList<string> Labels,
    IReadOnlyList<PackageUpdateInfo> PackageUpdates,
    IReadOnlyList<string> Commits,
    bool Merged);

internal sealed record PackageUpdateInfo(string? Package, string? Type, string? Update);
