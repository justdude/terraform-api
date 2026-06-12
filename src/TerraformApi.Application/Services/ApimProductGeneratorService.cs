using TerraformApi.Domain.Interfaces;
using TerraformApi.Domain.Models;

namespace TerraformApi.Application.Services;

/// <summary>
/// Generates a standalone APIM product Terraform block. Missing settings are
/// filled with placeholder tags and explained in a header comment — the same
/// contract as the conversion endpoints. Shared by the HTTP API and the MCP tool.
/// </summary>
public sealed class ApimProductGeneratorService : IApimProductGenerator
{
    private readonly ITerraformGenerator _generator;
    private readonly IApimNamingValidator _namingValidator;

    public ApimProductGeneratorService(ITerraformGenerator generator, IApimNamingValidator namingValidator)
    {
        _generator = generator;
        _namingValidator = namingValidator;
    }

    /// <inheritdoc />
    public ProductGenerationResult Generate(ApimProductRequest request)
    {
        var defaultedTags = new List<string>();

        string Fill(string? value, string tag)
        {
            if (!string.IsNullOrWhiteSpace(value))
                return value;
            defaultedTags.Add(tag);
            return tag;
        }

        var productId = Fill(request.ProductId, ApimPlaceholders.ProductId);

        // Validate only real values — placeholder tags are replaced later.
        if (!ApimPlaceholders.ContainsPlaceholder(productId))
        {
            var idResult = _namingValidator.ValidateApiName(productId);
            if (!idResult.IsValid)
            {
                return new ProductGenerationResult
                {
                    Success = false,
                    Errors = idResult.Errors.Select(e => $"ProductId: {e}").ToList()
                };
            }
        }

        var product = new ApimProduct
        {
            ApimResourceGroupName = Fill(request.ApimResourceGroupName, ApimPlaceholders.StageGroupName),
            ApimName = Fill(request.ApimName, ApimPlaceholders.ApimName),
            ProductId = productId,
            DisplayName = Fill(request.DisplayName, ApimPlaceholders.ProductDisplayName),
            Description = request.Description ?? "",
            SubscriptionRequired = request.SubscriptionRequired,
            ApprovalRequired = request.ApprovalRequired,
            Published = request.Published,
            SubscriptionsLimit = request.SubscriptionsLimit
        };

        var terraform = ApimPlaceholders.BuildHeaderComment(defaultedTags)
                        + _generator.GenerateProductBlock(product);

        return new ProductGenerationResult
        {
            Success = true,
            TerraformConfig = terraform,
            DefaultedTags = defaultedTags
        };
    }
}
