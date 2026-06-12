namespace TerraformApi.Domain.Models;

/// <summary>
/// Placeholder tags substituted for APIM settings the caller did not provide.
/// The idea: a missing setting never blocks generation — the output carries a
/// recognizable tag (e.g. <c>{api-group}</c>) the user replaces later, and the
/// generated file starts with a comment explaining every tag that was used.
/// </summary>
public static class ApimPlaceholders
{
    public const string Environment = "{environment}";
    public const string ApiGroupName = "{api-group}";
    public const string StageGroupName = "{stage-group-name}";
    public const string ApimName = "{apim-name}";
    public const string ApiPathPrefix = "{api-path-prefix}";
    public const string ApiPathSuffix = "{api-path-suffix}";
    public const string ApiGatewayHost = "{api-gateway-host}";
    public const string BackendServicePath = "{backend-service-path}";
    public const string ProductId = "{product-id}";
    public const string ProductDisplayName = "{product-display-name}";

    /// <summary>Tag → human explanation used in the generated header comment.</summary>
    public static readonly IReadOnlyDictionary<string, string> Descriptions =
        new Dictionary<string, string>
        {
            [Environment] = "target environment name (e.g. dev, staging, prod)",
            [ApiGroupName] = "Terraform variable group name for the API (e.g. my-api-group)",
            [StageGroupName] = "Azure resource group of the APIM instance (e.g. rg-apim-dev)",
            [ApimName] = "Azure APIM instance name (e.g. apim-company-dev)",
            [ApiPathPrefix] = "path prefix for the API (e.g. myapp)",
            [ApiPathSuffix] = "path suffix for the API (e.g. api)",
            [ApiGatewayHost] = "API gateway hostname (e.g. api.dev.company.com)",
            [BackendServicePath] = "backend service path segment (e.g. my-service)",
            [ProductId] = "APIM product identifier (e.g. my-product)",
            [ProductDisplayName] = "product display name shown in the APIM portal"
        };

    /// <summary>True when the value contains any known placeholder tag.</summary>
    public static bool ContainsPlaceholder(string? value) =>
        value is not null && Descriptions.Keys.Any(value.Contains);

    /// <summary>
    /// Fills every missing required setting with its placeholder tag.
    /// Returns the normalized settings plus the list of tags that were applied.
    /// Idempotent: already-normalized settings produce no new tags.
    /// </summary>
    public static (ConversionSettings Settings, List<string> DefaultedTags) Normalize(ConversionSettings settings)
    {
        var tags = new List<string>();

        string Fill(string? value, string tag)
        {
            if (!string.IsNullOrWhiteSpace(value))
                return value;
            tags.Add(tag);
            return tag;
        }

        var normalized = settings with
        {
            Environment = Fill(settings.Environment, Environment),
            ApiGroupName = Fill(settings.ApiGroupName, ApiGroupName),
            StageGroupName = Fill(settings.StageGroupName, StageGroupName),
            ApimName = Fill(settings.ApimName, ApimName),
            ApiPathPrefix = Fill(settings.ApiPathPrefix, ApiPathPrefix),
            ApiPathSuffix = Fill(settings.ApiPathSuffix, ApiPathSuffix),
            ApiGatewayHost = Fill(settings.ApiGatewayHost, ApiGatewayHost),
            BackendServicePath = Fill(settings.BackendServicePath, BackendServicePath)
        };

        return (normalized, tags);
    }

    /// <summary>
    /// Builds the explanatory comment block placed before a generated Terraform
    /// file when placeholder tags were used.
    /// </summary>
    public static string BuildHeaderComment(IReadOnlyCollection<string> defaultedTags)
    {
        if (defaultedTags.Count == 0)
            return "";

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("# ============================================================================");
        sb.AppendLine("# GENERATED WITH PLACEHOLDER TAGS - replace them before applying:");
        foreach (var tag in defaultedTags.Distinct())
        {
            var description = Descriptions.GetValueOrDefault(tag, "value not provided");
            sb.AppendLine($"#   {tag,-24} - {description}");
        }
        sb.AppendLine("# ============================================================================");
        return sb.ToString();
    }
}
