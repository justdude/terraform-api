using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace TerraformApi.Api.Tests.Endpoints;

/// <summary>
/// Integration tests for the POST /api/transform-environment endpoint.
/// </summary>
public class TransformEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private const string DevTerraform = """
        test-api-group = {
          product = []
          api = [
            {
                apim_resource_group_name         = "rg-apim-dev"
                apim_name                        = "apim-company-dev"
                name                             = "test-api-dev"
                display_name                     = "Test API - dev"
                path                             = "app.dev/v1/api"
                service_url                      = "https://api-dev.company.com/my-service/"
                protocols                        = ["https"]
                revision                         = "1"
                soap_pass_through                = false
                subscription_required            = false
                product_id                       = null
                subscription_key_parameter_names = null
            },
          ]

          api_operations = [
            {
                operation_id             = "get-users-dev"
                apim_resource_group_name = "rg-apim-dev"
                apim_name                = "apim-company-dev"
                api_name                 = "test-api-dev"
                display_name             = "GET users"
                method                   = "GET"
                url_template             = "users"
                status_code              = "200"
                description              = ""
            },
          ]
        }
        """;

    public TransformEndpointTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task TransformEnvironment_ValidRequest_ReturnsOk()
    {
        var request = new
        {
            sourceTerraform = DevTerraform,
            targetEnvironment = "staging",
            targetStageGroupName = "rg-apim-staging",
            targetApimName = "apim-company-staging",
            targetApiGatewayHost = "api-staging.company.com"
        };

        var response = await _client.PostAsJsonAsync("/api/transform-environment", request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);

        Assert.True(doc.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal("dev", doc.RootElement.GetProperty("detectedSourceEnvironment").GetString());

        var terraform = doc.RootElement.GetProperty("transformedTerraform").GetString()!;
        Assert.Contains("rg-apim-staging", terraform);
        Assert.Contains("apim-company-staging", terraform);
        Assert.Contains("api-staging.company.com", terraform);
        Assert.Contains("test-api-staging", terraform);
    }

    [Fact]
    public async Task TransformEnvironment_WithExistingTarget_ReturnsMergedResult()
    {
        var stagingTerraform = """
            test-api-group = {
              product = []
              api = [
                {
                    apim_resource_group_name = "rg-apim-staging"
                    apim_name                = "apim-company-staging"
                    name                     = "test-api-staging"
                    display_name             = "Test API - staging"
                    path                     = "app.staging/v1/api"
                    service_url              = "https://api-staging.company.com/my-service/"
                    subscription_required    = false
                },
              ]

              api_operations = [
                {
                    operation_id             = "get-users-staging"
                    apim_resource_group_name = "rg-apim-staging"
                    apim_name                = "apim-company-staging"
                    api_name                 = "test-api-staging"
                    display_name             = "GET users"
                    method                   = "GET"
                    url_template             = "users"
                    status_code              = "200"
                    description              = ""
                },
                {
                    operation_id             = "custom-op-staging"
                    apim_resource_group_name = "rg-apim-staging"
                    apim_name                = "apim-company-staging"
                    api_name                 = "test-api-staging"
                    display_name             = "GET health"
                    method                   = "GET"
                    url_template             = "health"
                    status_code              = "200"
                    description              = ""
                },
              ]
            }
            """;

        var request = new
        {
            sourceTerraform = DevTerraform,
            targetEnvironment = "staging",
            targetStageGroupName = "rg-apim-staging",
            targetApimName = "apim-company-staging",
            targetApiGatewayHost = "api-staging.company.com",
            existingTargetTerraform = stagingTerraform
        };

        var response = await _client.PostAsJsonAsync("/api/transform-environment", request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);

        Assert.True(doc.RootElement.GetProperty("success").GetBoolean());

        var summary = doc.RootElement.GetProperty("summary");
        Assert.True(summary.GetProperty("totalOperations").GetInt32() >= 1);

        // custom-op-staging should be preserved in the output
        var terraform = doc.RootElement.GetProperty("transformedTerraform").GetString()!;
        Assert.Contains("custom-op-staging", terraform);
    }

    [Fact]
    public async Task TransformEnvironment_EmptySource_ReturnsBadRequest()
    {
        var request = new
        {
            sourceTerraform = "",
            targetEnvironment = "staging",
            targetStageGroupName = "rg-apim-staging",
            targetApimName = "apim-company-staging",
            targetApiGatewayHost = "api-staging.company.com"
        };

        var response = await _client.PostAsJsonAsync("/api/transform-environment", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task TransformEnvironment_SameEnvironment_ReturnsBadRequest()
    {
        var request = new
        {
            sourceTerraform = DevTerraform,
            targetEnvironment = "dev",
            targetStageGroupName = "rg-apim-dev",
            targetApimName = "apim-company-dev",
            targetApiGatewayHost = "api-dev.company.com"
        };

        var response = await _client.PostAsJsonAsync("/api/transform-environment", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task TransformEnvironment_WithSubscriptionOverride_AppliesOverride()
    {
        var request = new
        {
            sourceTerraform = DevTerraform,
            targetEnvironment = "prod",
            targetStageGroupName = "rg-apim-prod",
            targetApimName = "apim-company-prod",
            targetApiGatewayHost = "api.company.com",
            targetSubscriptionRequired = true
        };

        var response = await _client.PostAsJsonAsync("/api/transform-environment", request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);

        var terraform = doc.RootElement.GetProperty("transformedTerraform").GetString()!;
        Assert.Contains("subscription_required            = true", terraform);
    }

    [Fact]
    public async Task TransformEnvironment_ExplicitSourceEnv_UsesProvided()
    {
        var request = new
        {
            sourceTerraform = DevTerraform,
            sourceEnvironment = "dev",
            targetEnvironment = "staging",
            targetStageGroupName = "rg-apim-staging",
            targetApimName = "apim-company-staging",
            targetApiGatewayHost = "api-staging.company.com"
        };

        var response = await _client.PostAsJsonAsync("/api/transform-environment", request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);

        Assert.Equal("dev", doc.RootElement.GetProperty("detectedSourceEnvironment").GetString());
    }
}
