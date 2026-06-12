using System.Text.Json;
using TerraformApi.Application.Services;
using TerraformApi.Mcp.Tools;

namespace TerraformApi.Mcp.Tests.Tools;

/// <summary>Tests for the generate_apim_product MCP tool.</summary>
public class ProductToolTests
{
    private readonly ApimProductGeneratorService _generator = new(
        new TerraformGeneratorService(), new ApimNamingValidatorService());

    [Fact]
    public void GenerateProduct_FullParameters_ReturnsBlock()
    {
        var result = ProductTool.GenerateProduct(
            _generator,
            productId: "my-product",
            displayName: "My Product",
            apimResourceGroupName: "rg-apim-dev",
            apimName: "apim-company-dev",
            subscriptionRequired: true);

        using var doc = JsonDocument.Parse(result);
        Assert.True(doc.RootElement.GetProperty("success").GetBoolean());
        Assert.Contains("product = [", doc.RootElement.GetProperty("terraformConfig").GetString());
    }

    [Fact]
    public void GenerateProduct_NoParameters_PlaceholderTags()
    {
        var result = ProductTool.GenerateProduct(_generator);

        using var doc = JsonDocument.Parse(result);
        Assert.True(doc.RootElement.GetProperty("success").GetBoolean());

        var hcl = doc.RootElement.GetProperty("terraformConfig").GetString()!;
        Assert.Contains("{product-id}", hcl);
        Assert.Contains("{apim-name}", hcl);
        Assert.Contains("GENERATED WITH PLACEHOLDER TAGS", hcl);
        Assert.Equal(4, doc.RootElement.GetProperty("defaultedTags").GetArrayLength());
    }

    [Fact]
    public void GenerateProduct_InvalidId_ReturnsError()
    {
        var result = ProductTool.GenerateProduct(_generator, productId: "bad id!!");

        using var doc = JsonDocument.Parse(result);
        Assert.False(doc.RootElement.GetProperty("success").GetBoolean());
    }
}
