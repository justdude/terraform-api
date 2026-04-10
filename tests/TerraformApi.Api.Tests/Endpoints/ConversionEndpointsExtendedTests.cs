using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace TerraformApi.Api.Tests.Endpoints;

/// <summary>
/// Extended integration tests for conversion endpoints covering
/// CORS policy generation, multi-operation specs, warnings, and
/// edge cases that the basic suite doesn't hit.
/// </summary>
public class ConversionEndpointsExtendedTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    // Base settings reused across tests
    private static object BaseSettings(string openApiJson) => new
    {
        openApiJson,
        environment = "dev",
        apiGroupName = "test-group",
        stageGroupName = "rg-apim-dev",
        apimName = "apim-test",
        apiPathPrefix = "app",
        apiPathSuffix = "api",
        apiGatewayHost = "gw.test.com",
        backendServicePath = "svc",
        includeCorsPolicy = false
    };

    private const string MultiPathSpec = """
        {
          "openapi": "3.0.1",
          "info": { "title": "CRUD API", "version": "1.0.0" },
          "paths": {
            "/items": {
              "get": { "operationId": "listItems", "summary": "List Items",
                       "responses": { "200": { "description": "OK" } } },
              "post": { "operationId": "createItem", "summary": "Create Item",
                        "responses": { "201": { "description": "Created" } } }
            },
            "/items/{id}": {
              "get": { "operationId": "getItem", "summary": "Get Item",
                       "responses": { "200": { "description": "OK" } } },
              "put": { "operationId": "updateItem", "summary": "Update Item",
                       "responses": { "200": { "description": "OK" } } },
              "delete": { "operationId": "deleteItem", "summary": "Delete Item",
                          "responses": { "204": { "description": "No Content" } } }
            }
          }
        }
        """;

    public ConversionEndpointsExtendedTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient();
    }

    // ---- Convert extended tests ----

    [Fact]
    public async Task Convert_MultipleOperations_SummaryCountCorrect()
    {
        var response = await _client.PostAsJsonAsync("/api/convert", BaseSettings(MultiPathSpec));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await Deserialize<ConvertResponseDto>(response);

        Assert.True(result.Success);
        Assert.Equal(5, result.Summary?.OperationCount);
    }

    [Fact]
    public async Task Convert_WithCorsPolicy_PolicyInOutput()
    {
        var request = new
        {
            openApiJson = MultiPathSpec,
            environment = "dev",
            apiGroupName = "test-group",
            stageGroupName = "rg-apim-dev",
            apimName = "apim-test",
            apiPathPrefix = "app",
            apiPathSuffix = "api",
            apiGatewayHost = "gw.test.com",
            backendServicePath = "svc",
            includeCorsPolicy = true,
            frontendHost = "portal",
            companyDomain = "company.com",
            localDevHost = "localhost",
            localDevPort = "3000"
        };

        var response = await _client.PostAsJsonAsync("/api/convert", request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await Deserialize<ConvertResponseDto>(response);

        Assert.True(result.Success);
        Assert.Contains("policy = <<XML", result.TerraformConfig);
        Assert.Contains("<cors", result.TerraformConfig);
        Assert.Contains("portal.dev.company.com", result.TerraformConfig);
    }

    [Fact]
    public async Task Convert_CustomApiName_UsedInOutput()
    {
        var request = new
        {
            openApiJson = MultiPathSpec,
            environment = "prod",
            apiGroupName = "prod-group",
            stageGroupName = "rg-apim-prod",
            apimName = "apim-prod",
            apiPathPrefix = "app",
            apiPathSuffix = "api",
            apiGatewayHost = "api.company.com",
            backendServicePath = "svc",
            includeCorsPolicy = false,
            apiName = "my-custom-api"
        };

        var response = await _client.PostAsJsonAsync("/api/convert", request);
        var result = await Deserialize<ConvertResponseDto>(response);

        Assert.True(result.Success);
        Assert.Contains("my-custom-api-prod", result.TerraformConfig);
    }

    [Fact]
    public async Task Convert_WithProductId_IncludedInOutput()
    {
        var request = new
        {
            openApiJson = MultiPathSpec,
            environment = "dev",
            apiGroupName = "test-group",
            stageGroupName = "rg-apim-dev",
            apimName = "apim-test",
            apiPathPrefix = "app",
            apiPathSuffix = "api",
            apiGatewayHost = "gw.test.com",
            backendServicePath = "svc",
            includeCorsPolicy = false,
            productId = "my-product-id"
        };

        var response = await _client.PostAsJsonAsync("/api/convert", request);
        var result = await Deserialize<ConvertResponseDto>(response);

        Assert.True(result.Success);
        Assert.Contains("\"my-product-id\"", result.TerraformConfig);
    }

    [Fact]
    public async Task Convert_OutputContainsAllOperations()
    {
        var response = await _client.PostAsJsonAsync("/api/convert", BaseSettings(MultiPathSpec));
        var result = await Deserialize<ConvertResponseDto>(response);

        Assert.True(result.Success);
        // All 5 operation IDs should appear in the terraform output
        var ops = result.Summary?.Operations ?? [];
        Assert.All(ops, op => Assert.Contains(op.OperationId, result.TerraformConfig));
    }

    [Fact]
    public async Task Convert_SubscriptionRequired_ReflectedInOutput()
    {
        var request = new
        {
            openApiJson = MultiPathSpec,
            environment = "dev",
            apiGroupName = "test-group",
            stageGroupName = "rg-apim-dev",
            apimName = "apim-test",
            apiPathPrefix = "app",
            apiPathSuffix = "api",
            apiGatewayHost = "gw.test.com",
            backendServicePath = "svc",
            includeCorsPolicy = false,
            subscriptionRequired = true
        };

        var response = await _client.PostAsJsonAsync("/api/convert", request);
        var result = await Deserialize<ConvertResponseDto>(response);

        Assert.True(result.Success);
        Assert.Contains("subscription_required            = true", result.TerraformConfig);
    }

    // ---- Validate extended tests ----

    [Fact]
    public async Task Validate_MultipleOperations_AllReported()
    {
        var request = BaseSettings(MultiPathSpec);
        var response = await _client.PostAsJsonAsync("/api/validate", request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await Deserialize<ValidateResponseDto>(response);

        Assert.True(result.IsValid);
        Assert.Equal(5, result.Summary?.OperationCount);
    }

    [Fact]
    public async Task Validate_InvalidOpenApiJson_ReturnsBadRequest()
    {
        var request = BaseSettings("{completely broken");
        var response = await _client.PostAsJsonAsync("/api/validate", request);

        // The endpoint returns 400 when parsing fails fatally
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Validate_EmptyPaths_ReturnsValidWithZeroOps()
    {
        var emptySpec = """
            {
              "openapi": "3.0.1",
              "info": { "title": "Empty", "version": "1.0.0" },
              "paths": {}
            }
            """;

        var request = BaseSettings(emptySpec);
        var response = await _client.PostAsJsonAsync("/api/validate", request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await Deserialize<ValidateResponseDto>(response);

        Assert.True(result.IsValid);
        Assert.Equal(0, result.Summary?.OperationCount ?? 0);
    }

    // ---- Update extended tests ----

    [Fact]
    public async Task Update_PreservesCustomOperation_NotInNewSpec()
    {
        // Convert a spec with 2 operations
        var twoOpSpec = """
            {
              "openapi": "3.0.1",
              "info": { "title": "Two Op API", "version": "1.0.0" },
              "paths": {
                "/items": {
                  "get": { "operationId": "listItems", "summary": "List",
                            "responses": { "200": { "description": "OK" } } }
                },
                "/health": {
                  "get": { "operationId": "healthCheck", "summary": "Health",
                            "responses": { "200": { "description": "OK" } } }
                }
              }
            }
            """;

        var convertRequest = BaseSettings(twoOpSpec);
        var convertResp = await _client.PostAsJsonAsync("/api/convert", convertRequest);
        var convertResult = await Deserialize<ConvertResponseDto>(convertResp);
        Assert.True(convertResult.Success);

        // Now update with spec that has only 1 operation (healthCheck removed from spec)
        var oneOpSpec = """
            {
              "openapi": "3.0.1",
              "info": { "title": "Two Op API", "version": "2.0.0" },
              "paths": {
                "/items": {
                  "get": { "operationId": "listItems", "summary": "List",
                            "responses": { "200": { "description": "OK" } } }
                }
              }
            }
            """;

        var updateRequest = new
        {
            openApiJson = oneOpSpec,
            existingTerraform = convertResult.TerraformConfig,
            environment = "dev",
            apiGroupName = "test-group",
            stageGroupName = "rg-apim-dev",
            apimName = "apim-test",
            apiPathPrefix = "app",
            apiPathSuffix = "api",
            apiGatewayHost = "gw.test.com",
            backendServicePath = "svc",
            includeCorsPolicy = false
        };

        var updateResp = await _client.PostAsJsonAsync("/api/convert/update", updateRequest);
        var updateResult = await Deserialize<ConvertResponseDto>(updateResp);

        Assert.True(updateResult.Success);
        // healthCheck was only in old spec, should be preserved in merged output
        Assert.Contains("healthcheck", updateResult.TerraformConfig.ToLowerInvariant());
    }

    [Fact]
    public async Task Update_MissingExistingTerraform_ReturnsBadRequest()
    {
        var request = new
        {
            openApiJson = MultiPathSpec,
            existingTerraform = "",
            environment = "dev",
            apiGroupName = "test-group",
            stageGroupName = "rg-apim-dev",
            apimName = "apim-test",
            apiPathPrefix = "app",
            apiPathSuffix = "api",
            apiGatewayHost = "gw.test.com",
            backendServicePath = "svc"
        };

        var response = await _client.PostAsJsonAsync("/api/convert/update", request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ---- Shared helpers ----

    private static async Task<T> Deserialize<T>(HttpResponseMessage response)
    {
        var content = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<T>(content, JsonOptions)!;
    }

    private sealed class ConvertResponseDto
    {
        public bool Success { get; set; }
        public string TerraformConfig { get; set; } = "";
        public List<string> Warnings { get; set; } = [];
        public List<string> Errors { get; set; } = [];
        public ApiSummaryDto? Summary { get; set; }
    }

    private sealed class ValidateResponseDto
    {
        public bool IsValid { get; set; }
        public List<string> Errors { get; set; } = [];
        public ApiSummaryDto? Summary { get; set; }
    }

    private sealed class ApiSummaryDto
    {
        public string ApiName { get; set; } = "";
        public int OperationCount { get; set; }
        public List<OperationDto> Operations { get; set; } = [];
    }

    private sealed class OperationDto
    {
        public string OperationId { get; set; } = "";
        public string Method { get; set; } = "";
        public string UrlTemplate { get; set; } = "";
    }
}
