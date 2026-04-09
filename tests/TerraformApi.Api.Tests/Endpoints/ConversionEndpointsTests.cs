using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace TerraformApi.Api.Tests.Endpoints;

public class ConversionEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private const string ValidOpenApi = """
        {
          "openapi": "3.0.1",
          "info": { "title": "Test API", "version": "1.0.0" },
          "paths": {
            "/items": {
              "get": {
                "operationId": "getItems",
                "summary": "Get Items",
                "responses": { "200": { "description": "OK" } }
              }
            }
          }
        }
        """;

    public ConversionEndpointsTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task HealthCheck_ReturnsOk()
    {
        var response = await _client.GetAsync("/api/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Convert_ValidRequest_ReturnsOk()
    {
        var request = new
        {
            openApiJson = ValidOpenApi,
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

        var response = await _client.PostAsJsonAsync("/api/convert", request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var content = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<ConvertResponseDto>(content, JsonOptions);

        Assert.NotNull(result);
        Assert.True(result.Success);
        Assert.Contains("test-group = {", result.TerraformConfig);
        Assert.NotNull(result.Summary);
        Assert.True(result.Summary.OperationCount > 0);
    }

    [Fact]
    public async Task Convert_EmptyOpenApi_ReturnsBadRequest()
    {
        var request = new
        {
            openApiJson = "",
            environment = "dev",
            apiGroupName = "test",
            stageGroupName = "rg",
            apimName = "apim",
            apiPathPrefix = "app",
            apiPathSuffix = "api",
            apiGatewayHost = "gw",
            backendServicePath = "svc"
        };

        var response = await _client.PostAsJsonAsync("/api/convert", request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Convert_InvalidResourceGroup_ReturnsBadRequest()
    {
        var request = new
        {
            openApiJson = ValidOpenApi,
            environment = "dev",
            apiGroupName = "test",
            stageGroupName = "invalid group name!",
            apimName = "apim",
            apiPathPrefix = "app",
            apiPathSuffix = "api",
            apiGatewayHost = "gw",
            backendServicePath = "svc"
        };

        var response = await _client.PostAsJsonAsync("/api/convert", request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Update_ValidRequest_ReturnsOk()
    {
        // First convert to get existing terraform
        var convertRequest = new
        {
            openApiJson = ValidOpenApi,
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

        var convertResponse = await _client.PostAsJsonAsync("/api/convert", convertRequest);
        var convertContent = await convertResponse.Content.ReadAsStringAsync();
        var convertResult = JsonSerializer.Deserialize<ConvertResponseDto>(convertContent, JsonOptions);

        // Now update with additional operation
        var updatedOpenApi = """
            {
              "openapi": "3.0.1",
              "info": { "title": "Test API", "version": "2.0.0" },
              "paths": {
                "/items": {
                  "get": {
                    "operationId": "getItems",
                    "summary": "Get Items",
                    "responses": { "200": { "description": "OK" } }
                  },
                  "post": {
                    "operationId": "createItem",
                    "summary": "Create Item",
                    "responses": { "201": { "description": "Created" } }
                  }
                }
              }
            }
            """;

        var updateRequest = new
        {
            openApiJson = updatedOpenApi,
            existingTerraform = convertResult!.TerraformConfig,
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

        var updateResponse = await _client.PostAsJsonAsync("/api/convert/update", updateRequest);
        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);

        var updateContent = await updateResponse.Content.ReadAsStringAsync();
        var updateResult = JsonSerializer.Deserialize<ConvertResponseDto>(updateContent, JsonOptions);

        Assert.NotNull(updateResult);
        Assert.True(updateResult.Success);
        Assert.True(updateResult.Summary!.OperationCount >= 2);
    }

    [Fact]
    public async Task Validate_ValidOpenApi_ReturnsValid()
    {
        var request = new
        {
            openApiJson = ValidOpenApi,
            environment = "dev",
            apiGroupName = "test",
            stageGroupName = "rg",
            apimName = "apim",
            apiPathPrefix = "app",
            apiPathSuffix = "api",
            apiGatewayHost = "gw",
            backendServicePath = "svc"
        };

        var response = await _client.PostAsJsonAsync("/api/validate", request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var content = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<ValidateResponseDto>(content, JsonOptions);

        Assert.NotNull(result);
        Assert.True(result.IsValid);
        Assert.NotNull(result.Summary);
    }

    [Fact]
    public async Task FallbackToIndexHtml_ReturnsHtml()
    {
        var response = await _client.GetAsync("/");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("OpenAPI", content);
    }

    // DTO classes for deserialization
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
    }
}
