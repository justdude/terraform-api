using TerraformApi.Application.Services;
using TerraformApi.Domain.Interfaces;
using TerraformApi.Mcp.Tools;

namespace TerraformApi.Mcp.Tests.Tools;

public class ValidateToolTests
{
    private readonly IApimNamingValidator _validator;
    private readonly HttpClient _httpClient = new();

    private const string ValidOpenApi = """
        {
          "openapi": "3.0.1",
          "info": { "title": "Order API", "version": "1.0.0" },
          "paths": {
            "/orders": {
              "get": {
                "operationId": "listOrders",
                "summary": "List orders",
                "responses": { "200": { "description": "OK" } }
              },
              "post": {
                "operationId": "createOrder",
                "summary": "Create order",
                "responses": { "201": { "description": "Created" } }
              }
            },
            "/orders/{id}": {
              "get": {
                "operationId": "getOrder",
                "summary": "Get order by ID",
                "responses": { "200": { "description": "OK" } }
              }
            }
          }
        }
        """;

    public ValidateToolTests()
    {
        _validator = new ApimNamingValidatorService();
    }

    [Fact]
    public async Task Validate_ValidSpec_ReturnsValid()
    {
        var result = await ValidateTool.Validate(_httpClient, _validator, openApiJson: ValidOpenApi);

        Assert.Contains("Result: VALID", result);
        Assert.Contains("All naming rules pass", result);
    }

    [Fact]
    public async Task Validate_ValidSpec_ShowsApiTitle()
    {
        var result = await ValidateTool.Validate(_httpClient, _validator, openApiJson: ValidOpenApi);

        Assert.Contains("API Title: Order API", result);
        Assert.Contains("API Version: 1.0.0", result);
    }

    [Fact]
    public async Task Validate_ValidSpec_ListsAllOperations()
    {
        var result = await ValidateTool.Validate(_httpClient, _validator, openApiJson: ValidOpenApi);

        Assert.Contains("Operations (3):", result);
        Assert.Contains("GET", result);
        Assert.Contains("POST", result);
        Assert.Contains("/orders", result);
        Assert.Contains("/orders/{id}", result);
    }

    [Fact]
    public async Task Validate_ValidSpec_ShowsOperationIds()
    {
        var result = await ValidateTool.Validate(_httpClient, _validator, openApiJson: ValidOpenApi, environment: "dev");

        Assert.Contains("listorders-dev", result.ToLowerInvariant());
        Assert.Contains("createorder-dev", result.ToLowerInvariant());
        Assert.Contains("getorder-dev", result.ToLowerInvariant());
    }

    [Fact]
    public async Task Validate_ValidSpec_MarksOperationsAsOk()
    {
        var result = await ValidateTool.Validate(_httpClient, _validator, openApiJson: ValidOpenApi);

        Assert.Contains("[OK]", result);
        Assert.DoesNotContain("[INVALID]", result);
    }

    [Fact]
    public async Task Validate_InvalidJson_ReturnsFailedMessage()
    {
        var result = await ValidateTool.Validate(_httpClient, _validator, openApiJson: "{totally broken json!!");

        Assert.Contains("VALIDATION FAILED", result);
        Assert.Contains("Errors:", result);
    }

    [Fact]
    public async Task Validate_EmptyPaths_ReturnsValidWithZeroOperations()
    {
        var emptyPathsApi = """
            {
              "openapi": "3.0.1",
              "info": { "title": "Empty API", "version": "1.0.0" },
              "paths": {}
            }
            """;

        var result = await ValidateTool.Validate(_httpClient, _validator, openApiJson: emptyPathsApi);

        Assert.Contains("Operations (0):", result);
        Assert.Contains("Result: VALID", result);
    }

    [Fact]
    public async Task Validate_DifferentEnvironment_UsesEnvironmentInOperationIds()
    {
        var result = await ValidateTool.Validate(_httpClient, _validator, openApiJson: ValidOpenApi, environment: "staging");

        Assert.Contains("listorders-staging", result.ToLowerInvariant());
        Assert.Contains("createorder-staging", result.ToLowerInvariant());
    }

    [Fact]
    public async Task Validate_DefaultEnvironment_UsesDev()
    {
        var result = await ValidateTool.Validate(_httpClient, _validator, openApiJson: ValidOpenApi);

        Assert.Contains("listorders-dev", result.ToLowerInvariant());
    }

    [Fact]
    public async Task Validate_OperationWithoutOperationId_FallsBackToMethodAndPath()
    {
        var noOpIdApi = """
            {
              "openapi": "3.0.1",
              "info": { "title": "No OpId API", "version": "1.0.0" },
              "paths": {
                "/items": {
                  "get": {
                    "summary": "List items",
                    "responses": { "200": { "description": "OK" } }
                  }
                }
              }
            }
            """;

        var result = await ValidateTool.Validate(_httpClient, _validator, openApiJson: noOpIdApi);

        Assert.Contains("Operations (1):", result);
        Assert.Contains("GET", result);
        Assert.Contains("/items", result);
    }

    [Fact]
    public async Task Validate_MultipleHttpMethods_AllDetected()
    {
        var multiMethodApi = """
            {
              "openapi": "3.0.1",
              "info": { "title": "CRUD API", "version": "1.0.0" },
              "paths": {
                "/resources/{id}": {
                  "get": {
                    "operationId": "getResource",
                    "summary": "Get",
                    "responses": { "200": { "description": "OK" } }
                  },
                  "put": {
                    "operationId": "updateResource",
                    "summary": "Update",
                    "responses": { "200": { "description": "OK" } }
                  },
                  "delete": {
                    "operationId": "deleteResource",
                    "summary": "Delete",
                    "responses": { "204": { "description": "Deleted" } }
                  },
                  "patch": {
                    "operationId": "patchResource",
                    "summary": "Patch",
                    "responses": { "200": { "description": "OK" } }
                  }
                }
              }
            }
            """;

        var result = await ValidateTool.Validate(_httpClient, _validator, openApiJson: multiMethodApi);

        Assert.Contains("Operations (4):", result);
        Assert.Contains("GET", result);
        Assert.Contains("PUT", result);
        Assert.Contains("DELETE", result);
        Assert.Contains("PATCH", result);
    }

    [Fact]
    public async Task Validate_NeitherJsonNorUrl_ReturnsError()
    {
        var result = await ValidateTool.Validate(_httpClient, _validator);

        Assert.Contains("failed", result, StringComparison.OrdinalIgnoreCase);
    }
}
