namespace Meziantou.RenovateConfig.Tests;

internal sealed class TemporaryBaseBranchName : TemporaryBranchName
{
    private const string Prefix = "tests/base/";

    private TemporaryBaseBranchName(DateTimeOffset date, string id)
    {
        Date = date;
        Id = id;
        Name = $"{Prefix}{date.ToString(DateFormat, CultureInfo.InvariantCulture)}-{id}";
    }

    public override DateTimeOffset Date { get; }

    public string Id { get; }

    public string Name { get; }

    public bool IsPartOfTestRun(string branchName)
    {
        return string.Equals(Name, branchName, StringComparison.Ordinal) ||
            (TemporaryRenovateBranchName.TryParse(branchName, out var renovateBranch) && string.Equals(Id, renovateBranch.Id, StringComparison.Ordinal));
    }

    public static TemporaryBaseBranchName Create()
    {
        return new TemporaryBaseBranchName(DateTimeOffset.UtcNow, Guid.NewGuid().ToString("N"));
    }

    public static bool TryParse(string value, [NotNullWhen(true)] out TemporaryBaseBranchName? result)
    {
        if (!TryParseDateAndId(value, Prefix, out var date, out var id, out var suffix) || suffix is not null)
        {
            result = null;
            return false;
        }

        result = new TemporaryBaseBranchName(date.Value, id);
        return true;
    }
}
