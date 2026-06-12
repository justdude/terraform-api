namespace TerraformApi.Domain.Models.Hcl;

/// <summary>
/// An HCL object value: <c>{ key = value ... }</c>.
/// Items include both assignments and preserved comments, in source order.
/// </summary>
public sealed record HclObject : HclValue
{
    /// <summary>Object body items in source order (assignments and comments).</summary>
    public List<HclObjectItem> Items { get; init; } = [];

    /// <summary>Convenience filter returning only the assignments.</summary>
    public IEnumerable<HclAssignment> Assignments => Items.OfType<HclAssignment>();

    /// <summary>Returns the value assigned to <paramref name="key"/>, or null if absent.</summary>
    public HclValue? Get(string key) =>
        Assignments.FirstOrDefault(a => a.Key == key)?.Value;

    /// <summary>Returns the assignment for <paramref name="key"/>, or null if absent.</summary>
    public HclAssignment? GetAssignment(string key) =>
        Assignments.FirstOrDefault(a => a.Key == key);
}
