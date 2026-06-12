namespace TerraformApi.Domain.Models.Sync;

/// <summary>
/// Identifies an API within a Terraform file by
/// (apim_resource_group_name, api_name). Uses resolved values for equality
/// when available, otherwise the raw (possibly interpolated) text.
/// </summary>
public sealed record ApimApiGroupKey
{
    /// <summary>Structural representation — as written in HCL (with ${...} or literal).</summary>
    public required string ApimResourceGroupNameRaw { get; init; }

    public required string ApiNameRaw { get; init; }

    /// <summary>Resolved values (when a variable context was passed).</summary>
    public string? ApimResourceGroupNameResolved { get; init; }

    public string? ApiNameResolved { get; init; }

    public bool Equals(ApimApiGroupKey? other)
    {
        if (other is null)
            return false;
        var thisRg = ApimResourceGroupNameResolved ?? ApimResourceGroupNameRaw;
        var thisApi = ApiNameResolved ?? ApiNameRaw;
        var otherRg = other.ApimResourceGroupNameResolved ?? other.ApimResourceGroupNameRaw;
        var otherApi = other.ApiNameResolved ?? other.ApiNameRaw;
        return string.Equals(thisRg, otherRg, StringComparison.OrdinalIgnoreCase)
            && string.Equals(thisApi, otherApi, StringComparison.OrdinalIgnoreCase);
    }

    public override int GetHashCode()
    {
        var rg = ApimResourceGroupNameResolved ?? ApimResourceGroupNameRaw;
        var api = ApiNameResolved ?? ApiNameRaw;
        return HashCode.Combine(
            rg.ToLowerInvariant(),
            api.ToLowerInvariant());
    }
}
