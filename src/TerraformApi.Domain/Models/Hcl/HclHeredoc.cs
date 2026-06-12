namespace TerraformApi.Domain.Models.Hcl;

/// <summary>
/// A heredoc value: <c>&lt;&lt;XML ... XML</c> or <c>&lt;&lt;-XML ... XML</c> (indented).
/// Content is stored without the marker lines.
/// </summary>
public sealed record HclHeredoc : HclValue
{
    /// <summary>The heredoc marker, e.g. "XML".</summary>
    public required string Marker { get; init; }

    /// <summary>
    /// The heredoc body without the marker lines and without a trailing newline.
    /// For indented heredocs the common leading whitespace has been removed.
    /// </summary>
    public required string Content { get; init; }

    /// <summary>True for the <c>&lt;&lt;-</c> variant.</summary>
    public bool Indented { get; init; }
}
