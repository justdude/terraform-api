namespace TerraformApi.Domain.Models.Hcl;

/// <summary>
/// A single <c>key = value</c> assignment.
/// </summary>
public sealed record HclAssignment : HclObjectItem
{
    /// <summary>The assignment key, e.g. "operation_id". For quoted keys, without the quotes.</summary>
    public required string Key { get; init; }

    /// <summary>The assigned value.</summary>
    public required HclValue Value { get; init; }

    /// <summary>True if the key was quoted in the source: <c>"${api_group_name}" = { ... }</c>.</summary>
    public bool KeyIsQuoted { get; init; }
}
