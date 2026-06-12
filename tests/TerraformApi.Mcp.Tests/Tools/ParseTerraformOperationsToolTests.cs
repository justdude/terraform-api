using System.Text.Json;
using TerraformApi.Application.Services;
using TerraformApi.Mcp.Tools;

namespace TerraformApi.Mcp.Tests.Tools;

/// <summary>
/// Tests for the ParseTerraformOperationsTool MCP tool.
/// Uses a real TerraformOperationsParserService (no mocking) for end-to-end testing.
/// </summary>
public class ParseTerraformOperationsToolTests
{
    private readonly TerraformOperationsParserService _parser = new();

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

                request = [
                  {
                    header = [
                      {
                        name        = "Authorization"
                        required    = true
                        type        = "string"
                        description = "Bearer token"
                      }
                    ]
                    query_parameter = [
                      {
                        name        = "limit"
                        required    = false
                        type        = "integer"
                        description = "Max results"
                      }
                    ]
                  }
                ]

                response = [
                  {
                    status_code  = 200
                    description  = "OK"
                  },
                  {
                    status_code  = 400
                    description  = "Bad Request"
                  }
                ]
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

                request = [
                  {
                    representation = [
                      {
                        content_type = "application/json"
                      }
                    ]
                  }
                ]
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

    [Fact]
    public void ParseOperationsCore_EmptyInput_ReturnsError()
    {
        var result = ParseTerraformOperationsTool.ParseOperationsCore(_parser, "");
        using var doc = JsonDocument.Parse(result);

        Assert.True(doc.RootElement.TryGetProperty("error", out _));
    }

    [Fact]
    public void ParseOperationsCore_ValidTerraform_ReturnsAllOperations()
    {
        var result = ParseTerraformOperationsTool.ParseOperationsCore(_parser, FullTerraform);
        using var doc = JsonDocument.Parse(result);

        Assert.Equal(3, doc.RootElement.GetProperty("totalOperations").GetInt32());
    }

    [Fact]
    public void ParseOperationsCore_ValidTerraform_ContainsApiInfo()
    {
        var result = ParseTerraformOperationsTool.ParseOperationsCore(_parser, FullTerraform);
        using var doc = JsonDocument.Parse(result);

        var api = doc.RootElement.GetProperty("api");
        Assert.Equal("Test API - dev", api.GetProperty("title").GetString());
        Assert.Equal("test-api-dev", api.GetProperty("name").GetString());
        Assert.Equal("myapp.dev/v1/api", api.GetProperty("path").GetString());
        Assert.Equal("terraform", api.GetProperty("source").GetString());
        Assert.Equal("dev", api.GetProperty("environment").GetString());
    }

    [Fact]
    public void ParseOperationsCore_OperationsHaveCorrectMethods()
    {
        var result = ParseTerraformOperationsTool.ParseOperationsCore(_parser, FullTerraform);
        using var doc = JsonDocument.Parse(result);

        var ops = doc.RootElement.GetProperty("operations");
        var methods = ops.EnumerateArray()
            .Select(o => o.GetProperty("method").GetString())
            .OrderBy(m => m)
            .ToList();

        Assert.Contains("GET", methods);
        Assert.Contains("POST", methods);
    }

    [Fact]
    public void ParseOperationsCore_OperationsHaveUrlTemplates()
    {
        var result = ParseTerraformOperationsTool.ParseOperationsCore(_parser, FullTerraform);
        using var doc = JsonDocument.Parse(result);

        var ops = doc.RootElement.GetProperty("operations");
        var urls = ops.EnumerateArray()
            .Select(o => o.GetProperty("urlTemplate").GetString())
            .ToList();

        Assert.Contains("users", urls);
        Assert.Contains("users/{userId}", urls);
    }

    [Fact]
    public void ParseOperationsCore_OperationsHavePaths()
    {
        var result = ParseTerraformOperationsTool.ParseOperationsCore(_parser, FullTerraform);
        using var doc = JsonDocument.Parse(result);

        var ops = doc.RootElement.GetProperty("operations");
        var paths = ops.EnumerateArray()
            .Select(o => o.GetProperty("path").GetString())
            .ToList();

        Assert.Contains("/users", paths);
        Assert.Contains("/users/{userId}", paths);
    }

    [Fact]
    public void ParseOperationsCore_OperationsHaveOperationIds()
    {
        var result = ParseTerraformOperationsTool.ParseOperationsCore(_parser, FullTerraform);
        using var doc = JsonDocument.Parse(result);

        var ops = doc.RootElement.GetProperty("operations");
        var ids = ops.EnumerateArray()
            .Select(o => o.GetProperty("operationId").GetString())
            .ToList();

        Assert.Contains("get-users-dev", ids);
        Assert.Contains("create-user-dev", ids);
        Assert.Contains("get-user-by-id-dev", ids);
    }

    [Fact]
    public void ParseOperationsCore_PathParametersIncluded()
    {
        var result = ParseTerraformOperationsTool.ParseOperationsCore(_parser, FullTerraform);
        using var doc = JsonDocument.Parse(result);

        var getUserOp = doc.RootElement.GetProperty("operations")
            .EnumerateArray()
            .First(o => o.GetProperty("operationId").GetString() == "get-user-by-id-dev");

        var parameters = getUserOp.GetProperty("parameters");
        var pathParam = parameters.EnumerateArray()
            .First(p => p.GetProperty("in").GetString() == "path");

        Assert.Equal("userId", pathParam.GetProperty("name").GetString());
        Assert.True(pathParam.GetProperty("required").GetBoolean());
    }

    [Fact]
    public void ParseOperationsCore_HeaderParametersIncluded()
    {
        var result = ParseTerraformOperationsTool.ParseOperationsCore(_parser, FullTerraform);
        using var doc = JsonDocument.Parse(result);

        var getUsersOp = doc.RootElement.GetProperty("operations")
            .EnumerateArray()
            .First(o => o.GetProperty("operationId").GetString() == "get-users-dev");

        var parameters = getUsersOp.GetProperty("parameters");
        var headerParam = parameters.EnumerateArray()
            .First(p => p.GetProperty("in").GetString() == "header");

        Assert.Equal("Authorization", headerParam.GetProperty("name").GetString());
        Assert.True(headerParam.GetProperty("required").GetBoolean());
    }

    [Fact]
    public void ParseOperationsCore_QueryParametersIncluded()
    {
        var result = ParseTerraformOperationsTool.ParseOperationsCore(_parser, FullTerraform);
        using var doc = JsonDocument.Parse(result);

        var getUsersOp = doc.RootElement.GetProperty("operations")
            .EnumerateArray()
            .First(o => o.GetProperty("operationId").GetString() == "get-users-dev");

        var parameters = getUsersOp.GetProperty("parameters");
        var queryParam = parameters.EnumerateArray()
            .First(p => p.GetProperty("in").GetString() == "query");

        Assert.Equal("limit", queryParam.GetProperty("name").GetString());
        Assert.Equal("integer", queryParam.GetProperty("type").GetString());
    }

    [Fact]
    public void ParseOperationsCore_ResponseCodesIncluded()
    {
        var result = ParseTerraformOperationsTool.ParseOperationsCore(_parser, FullTerraform);
        using var doc = JsonDocument.Parse(result);

        var getUsersOp = doc.RootElement.GetProperty("operations")
            .EnumerateArray()
            .First(o => o.GetProperty("operationId").GetString() == "get-users-dev");

        var codes = getUsersOp.GetProperty("responseCodes").EnumerateArray()
            .Select(c => c.GetInt32())
            .ToList();

        Assert.Contains(200, codes);
        Assert.Contains(400, codes);
    }

    [Fact]
    public void ParseOperationsCore_RequestBodyContentTypesIncluded()
    {
        var result = ParseTerraformOperationsTool.ParseOperationsCore(_parser, FullTerraform);
        using var doc = JsonDocument.Parse(result);

        var createOp = doc.RootElement.GetProperty("operations")
            .EnumerateArray()
            .First(o => o.GetProperty("operationId").GetString() == "create-user-dev");

        var contentTypes = createOp.GetProperty("requestBodyContentTypes");
        Assert.Contains("application/json", contentTypes.EnumerateArray().Select(t => t.GetString()));
    }

    [Fact]
    public void ParseOperationsCore_OutputKeysMatchFetchOperationsExactly()
    {
        // Parse equivalent data through both tools and verify JSON key sets are identical
        var openApiFetcher = new OpenApiOperationsFetcherService();
        var openApiSpec = """
            {
              "openapi": "3.0.1",
              "info": { "title": "Test API", "version": "1.0.0", "description": "A test API" },
              "paths": {
                "/users": {
                  "get": {
                    "operationId": "getUsers",
                    "summary": "Get Users",
                    "tags": ["users"],
                    "parameters": [
                      { "name": "limit", "in": "query", "required": false, "schema": { "type": "integer" }, "description": "Max results" }
                    ],
                    "responses": {
                      "200": { "description": "OK" },
                      "400": { "description": "Bad Request" }
                    }
                  },
                  "post": {
                    "operationId": "createUser",
                    "summary": "Create User",
                    "requestBody": { "content": { "application/json": { "schema": { "type": "object" } } } },
                    "responses": { "201": { "description": "Created" } }
                  }
                },
                "/users/{userId}": {
                  "get": {
                    "operationId": "getUserById",
                    "summary": "Get User By ID",
                    "parameters": [
                      { "name": "userId", "in": "path", "required": true, "schema": { "type": "string" } }
                    ],
                    "responses": { "200": { "description": "OK" } }
                  }
                }
              }
            }
            """;

        var openApiResult = FetchOperationsTool.ParseAndFormat(openApiFetcher, openApiSpec, "https://example.com/api.json");
        var terraformResult = ParseTerraformOperationsTool.ParseOperationsCore(_parser, FullTerraform);

        using var openApiDoc = JsonDocument.Parse(openApiResult);
        using var terraformDoc = JsonDocument.Parse(terraformResult);

        // Verify top-level keys are identical
        var openApiKeys = openApiDoc.RootElement.EnumerateObject().Select(p => p.Name).OrderBy(k => k).ToList();
        var terraformKeys = terraformDoc.RootElement.EnumerateObject().Select(p => p.Name).OrderBy(k => k).ToList();
        Assert.Equal(openApiKeys, terraformKeys);

        // Verify api object keys are identical
        var openApiApiKeys = openApiDoc.RootElement.GetProperty("api")
            .EnumerateObject().Select(p => p.Name).OrderBy(k => k).ToList();
        var tfApiKeys = terraformDoc.RootElement.GetProperty("api")
            .EnumerateObject().Select(p => p.Name).OrderBy(k => k).ToList();
        Assert.Equal(openApiApiKeys, tfApiKeys);

        // Verify operation keys are identical (use operations with parameters for full coverage)
        var openApiOpWithParams = openApiDoc.RootElement.GetProperty("operations").EnumerateArray()
            .First(o => o.TryGetProperty("parameters", out _));
        var tfOpWithParams = terraformDoc.RootElement.GetProperty("operations").EnumerateArray()
            .First(o => o.TryGetProperty("parameters", out _));

        var openApiOpKeys = openApiOpWithParams.EnumerateObject().Select(p => p.Name).OrderBy(k => k).ToList();
        var tfOpKeys = tfOpWithParams.EnumerateObject().Select(p => p.Name).OrderBy(k => k).ToList();
        Assert.Equal(openApiOpKeys, tfOpKeys);

        // Verify parameter keys are identical
        var openApiParamKeys = openApiOpWithParams.GetProperty("parameters")[0]
            .EnumerateObject().Select(p => p.Name).OrderBy(k => k).ToList();
        var tfParamKeys = tfOpWithParams.GetProperty("parameters")[0]
            .EnumerateObject().Select(p => p.Name).OrderBy(k => k).ToList();
        Assert.Equal(openApiParamKeys, tfParamKeys);
    }
}
