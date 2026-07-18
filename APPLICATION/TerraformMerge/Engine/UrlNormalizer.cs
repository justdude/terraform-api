using System.Text.RegularExpressions;

namespace TerraformMerge.Engine;

/// <summary>
/// URL-template normalization used by the similarity scorer. Two forms:
/// <see cref="Normalize"/> keeps parameter names (syntactic clean-up only);
/// <see cref="Canonical"/> additionally collapses <c>{id}</c> / <c>:id</c> to
/// <c>{}</c> so routes that differ only by parameter naming still align.
/// </summary>
public static partial class UrlNormalizer
{
    [GeneratedRegex(@"\{[^/{}]*\}")]
    private static partial Regex BraceParam();

    [GeneratedRegex(@":([^/]+)")]
    private static partial Regex ColonParam();

    [GeneratedRegex("/{2,}")]
    private static partial Regex DoubleSlash();

    /// <summary>Trims, collapses duplicate slashes, and removes surrounding slashes. Case preserved.</summary>
    public static string Normalize(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return "";

        var value = url.Trim();

        // Unify :id → {id} so both param syntaxes compare equal.
        value = ColonParam().Replace(value, "{$1}");

        var query = value.IndexOfAny(['?', '#']);
        var path = query >= 0 ? value[..query] : value;
        var suffix = query >= 0 ? value[query..] : "";

        path = DoubleSlash().Replace(path, "/");
        path = path.Trim('/');

        return path + suffix;
    }

    /// <summary>As <see cref="Normalize"/>, then replaces every parameter segment with <c>{}</c>.</summary>
    public static string Canonical(string? url)
    {
        var normalized = Normalize(url);
        return BraceParam().Replace(normalized, "{}");
    }

    /// <summary>Path segments of the normalized URL (empty segments removed).</summary>
    public static IReadOnlyList<string> Segments(string? url) =>
        Normalize(url).Split('/', StringSplitOptions.RemoveEmptyEntries);
}
