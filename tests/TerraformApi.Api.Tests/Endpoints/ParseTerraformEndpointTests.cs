using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace TerraformApi.Api.Tests.Endpoints;

/// <summary>
/// Integration tests for the POST /api/parse-terraform-operations endpoint.
/// </summary>
public class ParseTerraformEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private const string FullTerraform = """
        test-api-group = {
          product = []
          api = [
            {
                apim_resource_group_name         = "rg-apim-dev"
                apim_name                        = "apim-company-dev"
                name                             = "test-api-dev"
                display_name                     = "Test API - dev"
                path                             = "myapp.dev/v1/api"
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
                display_name             = "Get Users"
                method                   = "GET"
                url_template             = "users"
                status_code              = "200"
                description              = "Returns all users"
            },
            {
                operation_id             = "create-user-dev"
                apim_resource_group_name = "rg-apim-dev"
                apim_name                = "apim-company-dev"
                api_name                 = "test-api-dev"
                display_name             = "Create User"
                method                   = "POST"
                url_template             = "users"
                status_code              = "201"
                description              = "Creates a new user"
            },
            {
                operation_id             = "get-user-by-id-dev"
                apim_resource_group_name = "rg-apim-dev"
                apim_name                = "apim-company-dev"
                api_name                 = "test-api-dev"
                display_name             = "Get User By ID"
                method                   = "GET"
                url_template             = "users/{userId}"
                status_code              = "200"
                description              = "Returns a specific user"
            },
          ]
        }
        """;

    public ParseTerraformEndpointTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task ParseTerraformOperations_ValidInput_Returns200WithOperations()
    {
        var response = await _client.PostAsJsonAsync("/api/parse-terraform-operations",
            new { terraform = FullTerraform });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);

        Assert.True(doc.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal(3, doc.RootElement.GetProperty("totalOperations").GetInt32());
    }

    [Fact]
    public async Task ParseTerraformOperations_ValidInput_ReturnsApiInfo()
    {
        var response = await _client.PostAsJsonAsync("/api/parse-terraform-operations",
            new { terraform = FullTerraform });

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);

        var api = doc.RootElement.GetProperty("api");
        Assert.Equal("Test API - dev", api.GetProperty("title").GetString());
        Assert.Equal("test-api-dev", api.GetProperty("name").GetString());
        Assert.Equal("terraform", api.GetProperty("source").GetString());
    }

    [Fact]
    public async Task ParseTerraformOperations_ValidInput_OperationsHaveCorrectFields()
    {
        var response = await _client.PostAsJsonAsync("/api/parse-terraform-operations",
            new { terraform = FullTerraform });

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);

        var firstOp = doc.RootElement.GetProperty("operations")[0];
        Assert.Equal("GET", firstOp.GetProperty("method").GetString());
        Assert.Equal("users", firstOp.GetProperty("urlTemplate").GetString());
        Assert.Equal("/users", firstOp.GetProperty("path").GetString());
        Assert.Equal("get-users-dev", firstOp.GetProperty("operationId").GetString());
    }

    [Fact]
    public async Task ParseTerraformOperations_PathParameters_Extracted()
    {
        var response = await _client.PostAsJsonAsync("/api/parse-terraform-operations",
            new { terraform = FullTerraform });

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);

        var getUserOp = doc.RootElement.GetProperty("operations")
            .EnumerateArray()
            .First(o => o.GetProperty("operationId").GetString() == "get-user-by-id-dev");

        var pathParam = getUserOp.GetProperty("parameters")[0];
        Assert.Equal("userId", pathParam.GetProperty("name").GetString());
        Assert.Equal("path", pathParam.GetProperty("in").GetString());
        Assert.True(pathParam.GetProperty("required").GetBoolean());
    }

    [Fact]
    public async Task ParseTerraformOperations_EmptyInput_Returns400()
    {
        var response = await _client.PostAsJsonAsync("/api/parse-terraform-operations",
            new { terraform = "" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ParseTerraformOperations_NoOperations_ReturnsZeroCount()
    {
        var terraform = """
            test = {
              product = []
              api = [
                {
                    name         = "my-api-dev"
                    display_name = "My API"
                },
              ]
              api_operations = [
              ]
            }
            """;

        var response = await _client.PostAsJsonAsync("/api/parse-terraform-operations",
            new { terraform });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);

        Assert.True(doc.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal(0, doc.RootElement.GetProperty("totalOperations").GetInt32());
    }
}
