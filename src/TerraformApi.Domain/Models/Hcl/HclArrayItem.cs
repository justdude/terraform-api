namespace TerraformApi.Domain.Models.Hcl;

/// <summary>
/// A single array element together with the comments that immediately precede it.
/// </summary>
public sealed record HclArrayItem : HclNode
{
    /// <summary>Comments on the lines immediately before this element.</summary>
    public List<HclComment> LeadingComments { get; init; } = [];

    /// <summary>The element value.</summary>
    public required HclValue Value { get; init; }

    /// <summary>
    /// Number of blank lines that preceded this element in the source. Recorded
    /// by the parser and re-emitted by the writer's canonical (slow) path so a
    /// re-rendered array keeps its spacing. Zero for elements created programmatically.
    /// </summary>
    public int BlankLinesBefore { get; set; }
}
