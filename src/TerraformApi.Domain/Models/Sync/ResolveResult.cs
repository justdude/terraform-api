namespace TerraformApi.Domain.Models.Sync;

/// <summary>
/// Result of resolving Terraform interpolations against a variable context.
/// </summary>
public sealed record ResolveResult
{
    /// <summary>The template with all known variables substituted.</summary>
    public required string Value { get; init; }

    /// <summary>Expressions that could not be resolved (left as ${...} in Value).</summary>
    public List<string> UnresolvedExpressions { get; init; } = [];

    public bool HasUnresolvedExpressions => UnresolvedExpressions.Count > 0;
}
