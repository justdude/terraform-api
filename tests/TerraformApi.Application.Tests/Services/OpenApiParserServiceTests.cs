using TerraformApi.Application.Services;
using TerraformApi.Domain.Models;

namespace TerraformApi.Application.Tests.Services;

public class OpenApiParserServiceTests
{
    private readonly OpenApiParserService _parser;
    private readonly ApimNamingValidatorService _validator = new();

    private static readonly ConversionSettings DefaultSettings = new()
    {
        Environment = "dev",
        ApiGroupName = "test-api-group",
        StageGroupName = "rg-apim-dev",
        ApimName = "apim-test-dev",
        ApiPathPrefix = "testapp",
        ApiPathSuffix = "api",
        ApiGatewayHost = "api.test.com",
        BackendServicePath = "test-service",
        Revision = "1",
        IncludeCorsPolicy = false
    };

    private const string SimpleOpenApi = """
        {
          "openapi": "3.0.1",
          "info": {
            "title": "Pet Store",
            "version": "1.0.0"
          },
          "paths": {
            "/pets": {
              "get": {
                "operationId": "listPets",
                "summary": "List all pets",
                "responses": {
                  "200": {
                    "description": "A list of pets"
                  }
                }
              },
              "post": {
                "operationId": "createPet",
                "summary": "Create a pet",
                "requestBody": {
                  "content": {
                    "application/json": {
                      "schema": {
                        "type": "object"
                      }
                    }
                  }
                },
                "responses": {
                  "201": {
                    "description": "Pet created"
                  }
                }
              }
            },
            "/pets/{petId}": {
              "get": {
                "operationId": "getPet",
                "summary": "Get a pet by ID",
                "parameters": [
                  {
                    "name": "petId",
                    "in": "path",
                    "required": true,
                    "schema": { "type": "string" }
                  }
                ],
                "responses": {
                  "200": {
                    "description": "A pet",
                    "content": {
                      "application/json": {
                        "schema": { "type": "object" }
                      }
                    }
                  }
                }
              }
            }
          }
        }
        """;

    public OpenApiParserServiceTests()
    {
        _parser = new OpenApiParserService(_validator);
    }

    [Fact]
    public void Parse_SimpleOpenApi_ReturnsCorrectApiName()
    {
        var result = _parser.Parse(SimpleOpenApi, DefaultSettings);

        Assert.Equal("test-api-group", result.ApiGroupName);
        Assert.Equal("pet-store-dev", result.Api.Name);
        Assert.Equal("Pet Store - dev", result.Api.DisplayName);
    }

    [Fact]
    public void Parse_SimpleOpenApi_ReturnsCorrectApiSettings()
    {
        var result = _parser.Parse(SimpleOpenApi, DefaultSettings);

        Assert.Equal("rg-apim-dev", result.Api.ApimResourceGroupName);
        Assert.Equal("apim-test-dev", result.Api.ApimName);
        Assert.Contains("testapp", result.Api.Path);
        Assert.Equal("https://api.test.com/v1/test-service/", result.Api.ServiceUrl);
        Assert.Equal(["https"], result.Api.Protocols);
        Assert.Equal("1", result.Api.Revision);
        Assert.False(result.Api.SoapPassThrough);
        Assert.False(result.Api.SubscriptionRequired);
    }

    [Fact]
    public void Parse_SimpleOpenApi_ExtractsAllOperations()
    {
        var result = _parser.Parse(SimpleOpenApi, DefaultSettings);

        Assert.Equal(3, result.ApiOperations.Count);

        var getOps = result.ApiOperations.Where(o => o.Method == "GET").ToList();
        Assert.Equal(2, getOps.Count);

        var postOps = result.ApiOperations.Where(o => o.Method == "POST").ToList();
        Assert.Single(postOps);
    }

    [Fact]
    public void Parse_SimpleOpenApi_OperationsHaveCorrectProperties()
    {
        var result = _parser.Parse(SimpleOpenApi, DefaultSettings);

        var listPets = result.ApiOperations.First(o => o.DisplayName == "List all pets");
        Assert.Equal("GET", listPets.Method);
        Assert.Equal("pets", listPets.UrlTemplate);
        Assert.Equal(200, listPets.StatusCode);
        Assert.Equal("pet-store-dev", listPets.ApiName);

        var createPet = result.ApiOperations.First(o => o.DisplayName == "Create a pet");
        Assert.Equal("POST", createPet.Method);
        Assert.Equal(201, createPet.StatusCode);
    }

    [Fact]
    public void Parse_WithCustomApiName_UsesProvidedName()
    {
        var settings = DefaultSettings with { ApiName = "custom-api" };

        var result = _parser.Parse(SimpleOpenApi, settings);

        Assert.Equal("custom-api-dev", result.Api.Name);
    }

    [Fact]
    public void Parse_WithCorsPolicy_GeneratesPolicy()
    {
        var settings = DefaultSettings with
        {
            IncludeCorsPolicy = true,
            FrontendHost = "portal",
            CompanyDomain = "test.com",
            LocalDevHost = "localhost",
            LocalDevPort = "3000"
        };

        var result = _parser.Parse(SimpleOpenApi, settings);

        Assert.NotNull(result.Api.Policy);
        Assert.Contains("<cors", result.Api.Policy);
        Assert.Contains("portal.dev.test.com", result.Api.Policy);
        Assert.Contains("localhost:3000", result.Api.Policy);
    }

    [Fact]
    public void Parse_WithSecurityScheme_AddsAuthorizationHeader()
    {
        var openApiWithSecurity = """
            {
              "openapi": "3.0.1",
              "info": { "title": "Secure API", "version": "1.0.0" },
              "paths": {
                "/data": {
                  "get": {
                    "operationId": "getData",
                    "summary": "Get data",
                    "security": [{ "bearerAuth": [] }],
                    "responses": {
                      "200": { "description": "OK" }
                    }
                  }
                }
              },
              "components": {
                "securitySchemes": {
                  "bearerAuth": {
                    "type": "http",
                    "scheme": "bearer"
                  }
                }
              }
            }
            """;

        var result = _parser.Parse(openApiWithSecurity, DefaultSettings);
        var op = result.ApiOperations.First();

        Assert.Single(op.Requests);
        Assert.Contains(op.Requests[0].Headers, h => h.Name == "Authorization");
    }

    [Fact]
    public void Parse_WithQueryParameters_ExtractsParameters()
    {
        var openApiWithParams = """
            {
              "openapi": "3.0.1",
              "info": { "title": "Params API", "version": "1.0.0" },
              "paths": {
                "/search": {
                  "get": {
                    "operationId": "search",
                    "summary": "Search",
                    "parameters": [
                      {
                        "name": "q",
                        "in": "query",
                        "required": true,
                        "schema": { "type": "string" }
                      },
                      {
                        "name": "limit",
                        "in": "query",
                        "required": false,
                        "schema": { "type": "integer" }
                      },
                      {
                        "name": "X-Request-Id",
                        "in": "header",
                        "required": false,
                        "schema": { "type": "string" }
                      }
                    ],
                    "responses": {
                      "200": { "description": "OK" }
                    }
                  }
                }
              }
            }
            """;

        var result = _parser.Parse(openApiWithParams, DefaultSettings);
        var op = result.ApiOperations.First();

        Assert.Single(op.Requests);
        Assert.Equal(2, op.Requests[0].QueryParameters.Count);
        Assert.Single(op.Requests[0].Headers);

        var qParam = op.Requests[0].QueryParameters.First(p => p.Name == "q");
        Assert.True(qParam.Required);
        Assert.Equal("string", qParam.Type);

        var limitParam = op.Requests[0].QueryParameters.First(p => p.Name == "limit");
        Assert.False(limitParam.Required);
        Assert.Equal("integer", limitParam.Type);
    }

    [Fact]
    public void Parse_InvalidJson_ThrowsInvalidOperationException()
    {
        Assert.Throws<InvalidOperationException>(
            () => _parser.Parse("not valid json {{{", DefaultSettings));
    }

    [Fact]
    public void Parse_EmptyPaths_ReturnsNoOperations()
    {
        var openApi = """
            {
              "openapi": "3.0.1",
              "info": { "title": "Empty API", "version": "1.0.0" },
              "paths": {}
            }
            """;

        var result = _parser.Parse(openApi, DefaultSettings);
        Assert.Empty(result.ApiOperations);
    }
}
