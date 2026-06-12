namespace TerraformApi.Domain.Models.Hcl;

/// <summary>
/// An HCL array value: <c>[ item, item, ... ]</c>.
/// Each element may carry leading comments (preserved on output).
/// </summary>
public sealed record HclArray : HclValue
{
    /// <summary>Array elements in source order.</summary>
    public List<HclArrayItem> Items { get; init; } = [];

    /// <summary>Comments after the last element, before the closing bracket.</summary>
    public List<HclComment> TrailingComments { get; init; } = [];
}
