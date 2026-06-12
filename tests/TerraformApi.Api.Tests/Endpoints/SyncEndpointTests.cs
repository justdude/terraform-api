using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace TerraformApi.Api.Tests.Endpoints;

/// <summary>
/// Integration tests for the sync engine endpoints:
/// POST /api/sync, POST /api/analyze-terraform, POST /api/apply-template-profile.
/// </summary>
public class SyncEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public SyncEndpointTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient();
    }

    private const string ExistingTerraform = """
        my-api-group = {
          product = []
          api_operations = [
            {
              operation_id             = "get-users-dev"
              apim_resource_group_name = "rg-apim-dev"
              apim_name                = "apim-company-dev"
              api_name                 = "my-api-dev"
              display_name             = "Get users"
              method                   = "GET"
              url_template             = "/users"
              status_code              = "200"
              description              = ""
            },
          ]
        }
        """;

    private const string OpenApi = """
        {
          "openapi": "3.0.1",
          "info": { "title": "User API", "version": "1.0.0" },
          "paths": {
            "/users": {
              "get": {
                "operationId": "listUsers",
                "summary": "List users",
                "responses": { "200": { "description": "OK" } }
              },
              "post": {
                "operationId": "createUser",
                "summary": "Create user",
                "responses": { "201": { "description": "Created" } }
              }
            }
          }
        }
        """;

    private static object SyncBody(string? existingTerraform = null, string? profile = null) => new
    {
        openApiJson = OpenApi,
        existingTerraform,
        templateProfileName = profile,
        environment = "dev",
        apiGroupName = "my-api-group",
        stageGroupName = "rg-apim-dev",
        apimName = "apim-company-dev",
        apiName = "my-api-dev",
        apiPathPrefix = "users",
        apiPathSuffix = "api",
        apiGatewayHost = "api.dev.company.com",
        backendServicePath = "user-service"
    };

    [Fact]
    public async Task Sync_ValidRequest_Returns200WithReport()
    {
        var response = await _client.PostAsJsonAsync("/api/sync", SyncBody(ExistingTerraform));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.True(json.GetProperty("success").GetBoolean());
        Assert.Equal(1, json.GetProperty("report").GetProperty("operationsAdded").GetInt32());

        var hcl = json.GetProperty("terraformConfig").GetString()!;
        Assert.Contains("get-users-dev", hcl);
    }

    [Fact]
    public async Task Sync_EmptyExisting_GeneratesFromScratch()
    {
        var response = await _client.PostAsJsonAsync("/api/sync", SyncBody());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(2, json.GetProperty("report").GetProperty("operationsAdded").GetInt32());
    }

    [Fact]
    public async Task Sync_MissingOpenApi_Returns400()
    {
        var response = await _client.PostAsJsonAsync("/api/sync", new
        {
            existingTerraform = ExistingTerraform,
            environment = "dev",
            apiGroupName = "g",
            stageGroupName = "rg-apim-dev",
            apimName = "apim",
            apiPathPrefix = "x",
            apiPathSuffix = "api",
            apiGatewayHost = "host",
            backendServicePath = "svc"
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Sync_UnknownProfile_Returns400()
    {
        var response = await _client.PostAsJsonAsync("/api/sync", SyncBody(ExistingTerraform, profile: "Bogus"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Sync_EnumsSerializedAsStrings()
    {
        var response = await _client.PostAsJsonAsync("/api/sync", SyncBody(ExistingTerraform));
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();

        var diffs = json.GetProperty("report").GetProperty("diffs");
        Assert.True(diffs.GetArrayLength() > 0);
        var kind = diffs[0].GetProperty("kind").GetString();
        Assert.NotNull(kind); // string, not a number
    }

    [Fact]
    public async Task Analyze_ValidTerraform_ReturnsGroupsAndProfile()
    {
        var response = await _client.PostAsJsonAsync("/api/analyze-terraform", new
        {
            terraform = ExistingTerraform
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.True(json.GetProperty("success").GetBoolean());
        Assert.Equal(1, json.GetProperty("totalOperations").GetInt32());
        Assert.Equal("MostlyLiteral",
            json.GetProperty("detectedProfile").GetProperty("confidence").GetString());
    }

    [Fact]
    public async Task Analyze_InvalidHcl_Returns400()
    {
        var response = await _client.PostAsJsonAsync("/api/analyze-terraform", new
        {
            terraform = "not { valid"
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ApplyProfile_Templatize_ReturnsChanges()
    {
        var response = await _client.PostAsJsonAsync("/api/apply-template-profile", new
        {
            existingTerraform = ExistingTerraform,
            direction = "Templatize",
            profileName = "UserExampleProfile",
            overwriteExisting = true
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.True(json.GetProperty("success").GetBoolean());
        var hcl = json.GetProperty("terraformConfig").GetString()!;
        Assert.Contains("${apim_name}", hcl);
    }

    [Fact]
    public async Task ApplyProfile_Resolve_SubstitutesVariables()
    {
        var templated = """
            g = {
              api_operations = [
                {
                  operation_id = "${operation_prefix}-${env}"
                  method       = "GET"
                  url_template = "users"
                },
              ]
            }
            """;

        var response = await _client.PostAsJsonAsync("/api/apply-template-profile", new
        {
            existingTerraform = templated,
            direction = "Resolve",
            variableContext = new Dictionary<string, string>
            {
                ["operation_prefix"] = "get-users",
                ["env"] = "dev"
            }
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains("get-users-dev", json.GetProperty("terraformConfig").GetString());
    }

    [Fact]
    public async Task ApplyProfile_BadDirection_Returns400()
    {
        var response = await _client.PostAsJsonAsync("/api/apply-template-profile", new
        {
            existingTerraform = ExistingTerraform,
            direction = "Sideways"
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
