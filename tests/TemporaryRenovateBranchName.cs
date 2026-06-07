using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace Meziantou.RenovateConfig.Tests;

internal sealed class TemporaryRenovateBranchName : TemporaryBranchName
{
    private const string BranchPrefix = "tests/renovate/";

    public TemporaryRenovateBranchName(TemporaryBaseBranchName baseBranch)
        : this(baseBranch.Date, baseBranch.Id)
    {
    }

    private TemporaryRenovateBranchName(DateTimeOffset date, string id)
    {
        Date = date;
        Id = id;
        Prefix = $"{BranchPrefix}{date.ToString(DateFormat, CultureInfo.InvariantCulture)}-{id}-";
    }

    public override DateTimeOffset Date { get; }

    public string Id { get; }

    public string Prefix { get; }

    public static bool TryParse(string value, [NotNullWhen(true)] out TemporaryRenovateBranchName? result)
    {
        if (!TryParseDateAndId(value, BranchPrefix, out var date, out var id, out var suffix) || string.IsNullOrEmpty(suffix))
        {
            result = null;
            return false;
        }

        result = new TemporaryRenovateBranchName(date.Value, id);
        return true;
    }
}
