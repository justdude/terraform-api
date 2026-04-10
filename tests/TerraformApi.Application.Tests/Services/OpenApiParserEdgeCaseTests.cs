using TerraformApi.Application.Services;
using TerraformApi.Domain.Models;

namespace TerraformApi.Application.Tests.Services;

/// <summary>
/// Edge-case tests for the OpenAPI parser covering unusual specs,
/// multiple HTTP methods, path parameters, and missing fields.
/// </summary>
public class OpenApiParserEdgeCaseTests
{
    private readonly OpenApiParserService _parser;
    private readonly ApimNamingValidatorService _validator = new();

    private static readonly ConversionSettings DefaultSettings = new()
    {
        Environment = "dev",
        ApiGroupName = "test-group",
        StageGroupName = "rg-apim-dev",
        ApimName = "apim-test",
        ApiPathPrefix = "app",
        ApiPathSuffix = "api",
        ApiGatewayHost = "gw.test.com",
        BackendServicePath = "svc",
        IncludeCorsPolicy = false
    };

    public OpenApiParserEdgeCaseTests()
    {
        _parser = new OpenApiParserService(_validator);
    }

    [Fact]
    public void Parse_OperationWithoutOperationId_GeneratesId()
    {
        // When operationId is absent, the parser should auto-generate one
        var spec = """
            {
              "openapi": "3.0.1",
              "info": { "title": "No OpId", "version": "1.0.0" },
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

        var result = _parser.Parse(spec, DefaultSettings);
        var op = result.ApiOperations.First();

        Assert.NotNull(op.OperationId);
        Assert.NotEmpty(op.OperationId);
        Assert.Contains("dev", op.OperationId);
    }

    [Fact]
    public void Parse_AllHttpMethods_AreExtracted()
    {
        var spec = """
            {
              "openapi": "3.0.1",
              "info": { "title": "Full CRUD", "version": "1.0.0" },
              "paths": {
                "/items/{id}": {
                  "get": {
                    "operationId": "getItem",
                    "summary": "Get",
                    "responses": { "200": { "description": "OK" } }
                  },
                  "put": {
                    "operationId": "updateItem",
                    "summary": "Update",
                    "responses": { "200": { "description": "OK" } }
                  },
                  "delete": {
                    "operationId": "deleteItem",
                    "summary": "Delete",
                    "responses": { "204": { "description": "No Content" } }
                  },
                  "patch": {
                    "operationId": "patchItem",
                    "summary": "Patch",
                    "responses": { "200": { "description": "OK" } }
                  }
                }
              }
            }
            """;

        var result = _parser.Parse(spec, DefaultSettings);

        Assert.Equal(4, result.ApiOperations.Count);
        Assert.Contains(result.ApiOperations, o => o.Method == "GET");
        Assert.Contains(result.ApiOperations, o => o.Method == "PUT");
        Assert.Contains(result.ApiOperations, o => o.Method == "DELETE");
        Assert.Contains(result.ApiOperations, o => o.Method == "PATCH");
    }

    [Fact]
    public void Parse_PathParameters_PreservedInTemplate()
    {
        var spec = """
            {
              "openapi": "3.0.1",
              "info": { "title": "Params", "version": "1.0.0" },
              "paths": {
                "/users/{userId}/orders/{orderId}": {
                  "get": {
                    "operationId": "getUserOrder",
                    "summary": "Get order",
                    "responses": { "200": { "description": "OK" } }
                  }
                }
              }
            }
            """;

        var result = _parser.Parse(spec, DefaultSettings);
        var op = result.ApiOperations.First();

        Assert.Equal("users/{userId}/orders/{orderId}", op.UrlTemplate);
    }

    [Fact]
    public void Parse_MultipleResponseCodes_UsesFirstSuccess()
    {
        var spec = """
            {
              "openapi": "3.0.1",
              "info": { "title": "Multi Response", "version": "1.0.0" },
              "paths": {
                "/create": {
                  "post": {
                    "operationId": "create",
                    "summary": "Create",
                    "responses": {
                      "201": { "description": "Created" },
                      "400": { "description": "Bad Request" },
                      "500": { "description": "Error" }
                    }
                  }
                }
              }
            }
            """;

        var result = _parser.Parse(spec, DefaultSettings);
        var op = result.ApiOperations.First();

        Assert.Equal(201, op.StatusCode);
    }

    [Fact]
    public void Parse_RequestBodyWithMultipleContentTypes_AllExtracted()
    {
        var spec = """
            {
              "openapi": "3.0.1",
              "info": { "title": "Multi Content", "version": "1.0.0" },
              "paths": {
                "/upload": {
                  "post": {
                    "operationId": "upload",
                    "summary": "Upload",
                    "requestBody": {
                      "content": {
                        "application/json": { "schema": { "type": "object" } },
                        "multipart/form-data": { "schema": { "type": "object" } }
                      }
                    },
                    "responses": { "200": { "description": "OK" } }
                  }
                }
              }
            }
            """;

        var result = _parser.Parse(spec, DefaultSettings);
        var op = result.ApiOperations.First();

        Assert.Single(op.Requests);
        Assert.Equal(2, op.Requests[0].Representations.Count);
        Assert.Contains(op.Requests[0].Representations, r => r.ContentType == "application/json");
        Assert.Contains(op.Requests[0].Representations, r => r.ContentType == "multipart/form-data");
    }

    [Fact]
    public void Parse_NoCorsOrigins_ReturnsEmptyPolicy()
    {
        // CORS enabled but no origins configured -> empty policy
        var settings = DefaultSettings with
        {
            IncludeCorsPolicy = true,
            FrontendHost = null,
            CompanyDomain = null,
            LocalDevHost = null,
            LocalDevPort = null
        };

        var spec = """
            {
              "openapi": "3.0.1",
              "info": { "title": "NoCors", "version": "1.0.0" },
              "paths": {
                "/test": {
                  "get": {
                    "operationId": "test",
                    "summary": "Test",
                    "responses": { "200": { "description": "OK" } }
                  }
                }
              }
            }
            """;

        var result = _parser.Parse(spec, settings);

        // With no origins, the policy string should be empty
        Assert.True(string.IsNullOrEmpty(result.Api.Policy));
    }

    [Fact]
    public void Parse_CustomApiDisplayName_UsesProvidedDisplayName()
    {
        var settings = DefaultSettings with { ApiDisplayName = "My Custom Display" };

        var spec = """
            {
              "openapi": "3.0.1",
              "info": { "title": "Original Title", "version": "1.0.0" },
              "paths": {}
            }
            """;

        var result = _parser.Parse(spec, settings);

        Assert.Equal("My Custom Display - dev", result.Api.DisplayName);
    }

    [Fact]
    public void Parse_OperationWithDescription_Preserved()
    {
        var spec = """
            {
              "openapi": "3.0.1",
              "info": { "title": "Desc", "version": "1.0.0" },
              "paths": {
                "/thing": {
                  "get": {
                    "operationId": "getThing",
                    "summary": "Get Thing",
                    "description": "Returns the thing from the store.",
                    "responses": { "200": { "description": "OK" } }
                  }
                }
              }
            }
            """;

        var result = _parser.Parse(spec, DefaultSettings);
        var op = result.ApiOperations.First();

        Assert.Equal("Returns the thing from the store.", op.Description);
    }

    [Fact]
    public void Parse_OperationWithResponseRepresentations_ExtractsContentTypes()
    {
        var spec = """
            {
              "openapi": "3.0.1",
              "info": { "title": "Resp", "version": "1.0.0" },
              "paths": {
                "/data": {
                  "get": {
                    "operationId": "getData",
                    "summary": "Get data",
                    "responses": {
                      "200": {
                        "description": "OK",
                        "content": {
                          "application/json": { "schema": { "type": "object" } },
                          "application/xml": { "schema": { "type": "object" } }
                        }
                      }
                    }
                  }
                }
              }
            }
            """;

        var result = _parser.Parse(spec, DefaultSettings);
        var op = result.ApiOperations.First();

        Assert.Single(op.Responses);
        Assert.Equal(200, op.Responses[0].StatusCode);
        Assert.Equal(2, op.Responses[0].Representations.Count);
    }
}
