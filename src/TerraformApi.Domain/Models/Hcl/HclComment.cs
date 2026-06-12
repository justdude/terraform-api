namespace TerraformApi.Domain.Models.Hcl;

/// <summary>
/// A comment preserved in the AST so the writer can round-trip it.
/// </summary>
public sealed record HclComment : HclObjectItem
{
    /// <summary>Comment text without the <c>#</c> / <c>//</c> / <c>/* */</c> markers.</summary>
    public required string Text { get; init; }

    /// <summary>The comment syntax used in the source (preserved on output).</summary>
    public required HclCommentKind Kind { get; init; }

    /// <summary>True when the comment is part of a leading block attached to the next item.</summary>
    public bool IsLeading { get; init; }
}

/// <summary>Comment syntax variants.</summary>
public enum HclCommentKind
{
    /// <summary><c># text</c></summary>
    LineHash,

    /// <summary><c>// text</c></summary>
    LineSlash,

    /// <summary><c>/* text */</c></summary>
    Block
}
