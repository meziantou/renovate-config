using Markdig.Renderers.Normalize;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace Meziantou.RenovateConfig.Tests;

internal static class MarkdownExtensions
{
    public static string InnerText(this MarkdownObject value)
    {
        return string.Join(" ", value.Descendants<LiteralInline>().Select(ToNormalizedString));
    }

    private static string ToNormalizedString(MarkdownObject value)
    {
        using var writer = new StringWriter();
        var renderer = new NormalizeRenderer(writer);
        renderer.Render(value);
        return writer.ToString();
    }
}
