namespace TerraformApi.Domain.Models.Hcl;

/// <summary>
/// A scalar literal: string (without interpolation), number, bool, or null.
/// </summary>
public sealed record HclLiteral : HclValue
{
    /// <summary>
    /// The value exactly as written in the source. For strings: the text between
    /// the quotes with escape sequences preserved verbatim.
    /// </summary>
    public required string RawValue { get; init; }

    /// <summary>The literal kind.</summary>
    public required HclLiteralKind Kind { get; init; }
}

/// <summary>Scalar literal kinds.</summary>
public enum HclLiteralKind
{
    String,
    Number,
    Bool,
    Null
}
