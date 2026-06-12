using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace TerraformApi.Api.Tests.Endpoints;

/// <summary>
/// Integration tests for POST /api/generate-product and the
/// placeholder-default behavior of POST /api/convert.
/// </summary>
public class ProductsEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public ProductsEndpointTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task GenerateProduct_FullRequest_ReturnsBlock()
    {
        var response = await _client.PostAsJsonAsync("/api/generate-product", new
        {
            productId = "my-product",
            displayName = "My Product",
            apimResourceGroupName = "rg-apim-dev",
            apimName = "apim-company-dev",
            subscriptionRequired = true
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(json.GetProperty("success").GetBoolean());
        Assert.Contains("product = [", json.GetProperty("terraformConfig").GetString());
        Assert.Equal(0, json.GetProperty("defaultedTags").GetArrayLength());
    }

    [Fact]
    public async Task GenerateProduct_EmptyRequest_TagsAndHeader()
    {
        var response = await _client.PostAsJsonAsync("/api/generate-product", new { });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.True(json.GetProperty("success").GetBoolean());
        Assert.Equal(4, json.GetProperty("defaultedTags").GetArrayLength());

        var hcl = json.GetProperty("terraformConfig").GetString()!;
        Assert.Contains("GENERATED WITH PLACEHOLDER TAGS", hcl);
        Assert.Contains("{product-id}", hcl);
    }

    [Fact]
    public async Task GenerateProduct_InvalidProductId_Returns400()
    {
        var response = await _client.PostAsJsonAsync("/api/generate-product", new
        {
            productId = "bad product!!"
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Convert_NoSettings_SucceedsWithPlaceholderTags()
    {
        var response = await _client.PostAsJsonAsync("/api/convert", new
        {
            openApiJson = """
                {
                  "openapi": "3.0.1",
                  "info": { "title": "Demo", "version": "1.0.0" },
                  "paths": {
                    "/x": {
                      "get": { "operationId": "getX", "summary": "X", "responses": { "200": { "description": "OK" } } }
                    }
                  }
                }
                """
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.True(json.GetProperty("success").GetBoolean());
        var hcl = json.GetProperty("terraformConfig").GetString()!;
        Assert.Contains("GENERATED WITH PLACEHOLDER TAGS", hcl);
        Assert.Contains("{api-group} = {", hcl);
        Assert.Contains("\"{stage-group-name}\"", hcl);
    }
}
