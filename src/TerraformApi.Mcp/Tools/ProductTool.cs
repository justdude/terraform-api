using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using ModelContextProtocol.Server;
using TerraformApi.Domain.Interfaces;
using TerraformApi.Domain.Models;

namespace TerraformApi.Mcp.Tools;

/// <summary>
/// MCP tool that generates a standalone APIM product Terraform block.
/// Mirrors POST /api/generate-product — both call the same Application service.
/// </summary>
[McpServerToolType]
public static class ProductTool
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    [McpServerTool(Name = "generate_apim_product")]
    [Description("Generates a standalone APIM 'product = [ ... ]' Terraform block. " +
                 "All settings are optional: anything not provided is generated with a placeholder tag " +
                 "(e.g. {product-id}, {apim-name}) you replace later — the output starts with a comment " +
                 "explaining each tag used.")]
    public static string GenerateProduct(
        IApimProductGenerator productGenerator,
        [Description("Optional product identifier. Defaults to the {product-id} placeholder tag.")] string? productId = null,
        [Description("Optional product display name. Defaults to the {product-display-name} placeholder tag.")] string? displayName = null,
        [Description("Optional APIM resource group name. Defaults to the {stage-group-name} placeholder tag.")] string? apimResourceGroupName = null,
        [Description("Optional APIM instance name. Defaults to the {apim-name} placeholder tag.")] string? apimName = null,
        [Description("Optional product description.")] string? description = null,
        [Description("Whether a subscription is required (default: false)")] bool subscriptionRequired = false,
        [Description("Whether subscription approval is required (default: false)")] bool approvalRequired = false,
        [Description("Whether the product is published (default: true)")] bool published = true,
        [Description("Optional limit on simultaneous subscriptions.")] int? subscriptionsLimit = null)
    {
        var result = productGenerator.Generate(new ApimProductRequest
        {
            ProductId = productId,
            DisplayName = displayName,
            ApimResourceGroupName = apimResourceGroupName,
            ApimName = apimName,
            Description = description,
            SubscriptionRequired = subscriptionRequired,
            ApprovalRequired = approvalRequired,
            Published = published,
            SubscriptionsLimit = subscriptionsLimit
        });

        return JsonSerializer.Serialize(result, JsonOptions);
    }
}
