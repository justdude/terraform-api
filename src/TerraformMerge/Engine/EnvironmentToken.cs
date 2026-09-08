using System.Text.RegularExpressions;

namespace TerraformMerge.Engine;

/// <summary>
/// Recognizes and rewrites the environment token inside a value —
/// <c>rg-apim-dev</c>, <c>orders.dev/v1/api</c>, <c>list-orders-dev</c>,
/// <c>Orders API - dev</c>.
///
/// A token only counts as a whole <b>segment</b>: it must start the value or
/// follow one of <c>- . _ /</c> or whitespace, and must end the value or be
/// followed by one of the same. That is what keeps <c>development</c> from
/// being read as <c>dev</c>, and leaves <c>devices</c> or <c>latest</c>
/// (<c>test</c>) untouched.
/// </summary>
public static class EnvironmentToken
{
    /// <summary>
    /// Environment names recognized when detecting a value's environment. The
    /// pattern tries them longest-first, so <c>pre-prod</c> wins over the
    /// <c>prod</c> segment inside it.
    /// </summary>
    public static IReadOnlyList<string> Known { get; } =
    [
        "dev", "qa", "test", "uat", "staging", "stage", "stg", "sit", "preprod",
        "pre-prod", "prod", "production", "development", "sandbox", "demo", "perf", "local"
    ];

    private const string Before = @"(?<=^|[-._/\s])";
    private const string After = @"(?=[-._/\s]|$)";

    private static readonly Regex AnyKnown = new(
        Before + "(?:" + string.Join("|", Known.OrderByDescending(k => k.Length).Select(Regex.Escape)) + ")" + After,
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>
    /// The first known environment token appearing as a segment of
    /// <paramref name="value"/>, lower-cased; null when there is none.
    /// </summary>
    public static string? Find(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return null;

        var match = AnyKnown.Match(value);
        return match.Success ? match.Value.ToLowerInvariant() : null;
    }

    /// <summary>True when <paramref name="token"/> appears as a segment of <paramref name="value"/>.</summary>
    public static bool Contains(string? value, string token) =>
        !string.IsNullOrEmpty(value) && !string.IsNullOrEmpty(token) &&
        Regex.IsMatch(value, Before + Regex.Escape(token) + After, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>
    /// Replaces every <paramref name="from"/> segment in <paramref name="value"/>
    /// with <paramref name="to"/>, keeping the casing the value used
    /// (<c>DEV</c> → <c>QA</c>, <c>Dev</c> → <c>Qa</c>, <c>dev</c> → <c>qa</c>).
    /// Returns the value unchanged when the token is absent.
    /// </summary>
    public static string Replace(string value, string from, string to)
    {
        if (string.IsNullOrEmpty(value) || string.IsNullOrEmpty(from) || to is null)
            return value;

        return Regex.Replace(
            value,
            Before + Regex.Escape(from) + After,
            match => MatchCase(match.Value, to),
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    /// <summary>Casts <paramref name="replacement"/> into the casing style of the text it replaces.</summary>
    private static string MatchCase(string matched, string replacement)
    {
        if (matched.All(c => !char.IsLetter(c) || char.IsUpper(c)))
            return replacement.ToUpperInvariant();

        if (char.IsUpper(matched[0]) && replacement.Length > 0)
            return char.ToUpperInvariant(replacement[0]) + replacement[1..];

        return replacement;
    }
}
