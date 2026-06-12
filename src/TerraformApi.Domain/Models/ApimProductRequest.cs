namespace TerraformApi.Domain.Models;

/// <summary>
/// Input for standalone APIM product block generation. All identity settings
/// are optional — missing values are generated with placeholder tags
/// (see <see cref="ApimPlaceholders"/>) the caller replaces later.
/// </summary>
public sealed record ApimProductRequest
{
    /// <summary>APIM resource group. Defaults to the {stage-group-name} tag.</summary>
    public string? ApimResourceGroupName { get; init; }

    /// <summary>APIM instance name. Defaults to the {apim-name} tag.</summary>
    public string? ApimName { get; init; }

    /// <summary>Product identifier. Defaults to the {product-id} tag.</summary>
    public string? ProductId { get; init; }

    /// <summary>Display name. Defaults to the {product-display-name} tag.</summary>
    public string? DisplayName { get; init; }

    public string? Description { get; init; }
    public bool SubscriptionRequired { get; init; }
    public bool ApprovalRequired { get; init; }
    public bool Published { get; init; } = true;
    public int? SubscriptionsLimit { get; init; }
}

/// <summary>Result of standalone product block generation.</summary>
public sealed record ProductGenerationResult
{
    public required bool Success { get; init; }

    /// <summary>The generated <c>product = [ ... ]</c> Terraform block.</summary>
    public string TerraformConfig { get; init; } = "";

    /// <summary>Placeholder tags that were used for missing settings.</summary>
    public List<string> DefaultedTags { get; init; } = [];

    public List<string> Errors { get; init; } = [];
}
