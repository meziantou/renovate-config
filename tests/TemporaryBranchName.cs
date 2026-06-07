namespace Meziantou.RenovateConfig.Tests;

internal abstract class TemporaryBranchName
{
    internal const string DateFormat = "yyyyMMddHHmmss";
    internal static readonly TimeSpan MaximumAge = TimeSpan.FromHours(24);

    public abstract DateTimeOffset Date { get; }

    public bool HasExpired(DateTimeOffset now)
    {
        return Date + MaximumAge <= now;
    }

    protected static bool TryParseDateAndId(string value, string prefix, [NotNullWhen(true)] out DateTimeOffset? date, [NotNullWhen(true)] out string? id, out string? suffix)
    {
        date = null;
        id = null;
        suffix = null;

        if (!value.StartsWith(prefix, StringComparison.Ordinal))
        {
            return false;
        }

        var parts = value[prefix.Length..].Split('-', 3, StringSplitOptions.None);
        if (parts.Length < 2 ||
            !DateTimeOffset.TryParseExact(parts[0], DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsedDate) ||
            !Guid.TryParseExact(parts[1], "N", out _))
        {
            return false;
        }

        date = parsedDate;
        id = parts[1];
        suffix = parts.Length == 3 ? parts[2] : null;
        return true;
    }
}
