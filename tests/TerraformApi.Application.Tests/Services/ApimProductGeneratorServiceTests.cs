using TerraformApi.Application.Services;
using TerraformApi.Domain.Models;

namespace TerraformApi.Application.Tests.Services;

/// <summary>Standalone product block generation with placeholder defaults.</summary>
public class ApimProductGeneratorServiceTests
{
    private readonly ApimProductGeneratorService _generator = new(
        new TerraformGeneratorService(), new ApimNamingValidatorService());

    [Fact]
    public void Generate_FullRequest_ProducesProductBlock()
    {
        var result = _generator.Generate(new ApimProductRequest
        {
            ProductId = "my-product",
            DisplayName = "My Product",
            ApimResourceGroupName = "rg-apim-dev",
            ApimName = "apim-company-dev",
            Description = "Demo product",
            SubscriptionRequired = true,
            ApprovalRequired = false,
            SubscriptionsLimit = 10
        });

        Assert.True(result.Success);
        Assert.Empty(result.DefaultedTags);
        Assert.Contains("product = [", result.TerraformConfig);
        Assert.Contains("product_id               = \"my-product\"", result.TerraformConfig);
        Assert.Contains("display_name             = \"My Product\"", result.TerraformConfig);
        Assert.Contains("subscription_required    = true", result.TerraformConfig);
        Assert.Contains("subscriptions_limit      = 10", result.TerraformConfig);
        Assert.DoesNotContain("GENERATED WITH PLACEHOLDER TAGS", result.TerraformConfig);
    }

    [Fact]
    public void Generate_EmptyRequest_AllTagsAndHeader()
    {
        var result = _generator.Generate(new ApimProductRequest());

        Assert.True(result.Success);
        Assert.Equal(4, result.DefaultedTags.Count);
        Assert.Contains("{product-id}", result.DefaultedTags);
        Assert.Contains("{product-display-name}", result.DefaultedTags);
        Assert.Contains("{stage-group-name}", result.DefaultedTags);
        Assert.Contains("{apim-name}", result.DefaultedTags);

        Assert.Contains("GENERATED WITH PLACEHOLDER TAGS", result.TerraformConfig);
        Assert.Contains("product_id               = \"{product-id}\"", result.TerraformConfig);
        Assert.Contains("apim_name                = \"{apim-name}\"", result.TerraformConfig);
    }

    [Fact]
    public void Generate_InvalidProductId_Fails()
    {
        var result = _generator.Generate(new ApimProductRequest { ProductId = "bad product id!!" });

        Assert.False(result.Success);
        Assert.NotEmpty(result.Errors);
    }

    [Fact]
    public void Generate_PublishedDefaultsTrue()
    {
        var result = _generator.Generate(new ApimProductRequest { ProductId = "p1", DisplayName = "P1" });

        Assert.Contains("published                = true", result.TerraformConfig);
    }
}
