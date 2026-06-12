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
}
