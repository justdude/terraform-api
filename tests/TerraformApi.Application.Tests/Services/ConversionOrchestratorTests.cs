using TerraformApi.Application.Services;
using TerraformApi.Domain.Models;

namespace TerraformApi.Application.Tests.Services;

public class ConversionOrchestratorTests
{
    private readonly ConversionOrchestratorService _orchestrator;

    private static readonly ConversionSettings ValidSettings = new()
    {
        Environment = "dev",
        ApiGroupName = "pet-store-group",
        StageGroupName = "rg-apim-dev",
        ApimName = "apim-company-dev",
        ApiPathPrefix = "petstore",
        ApiPathSuffix = "api",
        ApiGatewayHost = "api.company.com",
        BackendServicePath = "pet-service",
        IncludeCorsPolicy = false
    };

    private const string ValidOpenApi = """
        {
          "openapi": "3.0.1",
          "info": { "title": "Pet Store", "version": "1.0.0" },
          "paths": {
            "/pets": {
              "get": {
                "operationId": "listPets",
                "summary": "List all pets",
                "responses": { "200": { "description": "OK" } }
              }
            }
          }
        }
        """;

    public ConversionOrchestratorTests()
    {
        var validator = new ApimNamingValidatorService();
        var parser = new OpenApiParserService(validator);
        var generator = new TerraformGeneratorService();
        var merger = new TerraformMergerService(generator);
        _orchestrator = new ConversionOrchestratorService(parser, generator, merger, validator);
    }

    [Fact]
    public void Convert_ValidInput_ReturnsSuccess()
    {
        var result = _orchestrator.Convert(ValidOpenApi, ValidSettings);

        Assert.True(result.Success);
        Assert.NotEmpty(result.TerraformConfig);
        Assert.Empty(result.Errors);
        Assert.NotNull(result.Configuration);
    }

    [Fact]
    public void Convert_ValidInput_GeneratesCorrectTerraform()
    {
        var result = _orchestrator.Convert(ValidOpenApi, ValidSettings);

        Assert.Contains("pet-store-group = {", result.TerraformConfig);
        Assert.Contains("pet-store-dev", result.TerraformConfig);
        Assert.Contains("rg-apim-dev", result.TerraformConfig);
        Assert.Contains("apim-company-dev", result.TerraformConfig);
        Assert.Contains("api_operations = [", result.TerraformConfig);
    }

    [Fact]
    public void Convert_InvalidOpenApi_ReturnsErrors()
    {
        var result = _orchestrator.Convert("{invalid json!!", ValidSettings);

        Assert.False(result.Success);
        Assert.NotEmpty(result.Errors);
    }

    [Fact]
    public void Convert_InvalidResourceGroup_ReturnsErrors()
    {
        var settings = ValidSettings with { StageGroupName = "invalid name with spaces!" };

        var result = _orchestrator.Convert(ValidOpenApi, settings);

        Assert.False(result.Success);
        Assert.Contains(result.Errors, e => e.Contains("StageGroupName"));
    }

    [Fact]
    public void Update_ValidInput_ReturnsSuccess()
    {
        var initialResult = _orchestrator.Convert(ValidOpenApi, ValidSettings);
        Assert.True(initialResult.Success);

        var updatedOpenApi = """
            {
              "openapi": "3.0.1",
              "info": { "title": "Pet Store", "version": "2.0.0" },
              "paths": {
                "/pets": {
                  "get": {
                    "operationId": "listPets",
                    "summary": "List all pets v2",
                    "responses": { "200": { "description": "OK" } }
                  },
                  "post": {
                    "operationId": "createPet",
                    "summary": "Create a pet",
                    "responses": { "201": { "description": "Created" } }
                  }
                }
              }
            }
            """;

        var updateResult = _orchestrator.Update(
            updatedOpenApi,
            initialResult.TerraformConfig,
            ValidSettings);

        Assert.True(updateResult.Success);
        Assert.NotEmpty(updateResult.TerraformConfig);
        Assert.Contains("createpet", updateResult.TerraformConfig.ToLowerInvariant());
    }

    [Fact]
    public void Convert_WithCorsPolicy_IncludesPolicy()
    {
        var settings = ValidSettings with
        {
            IncludeCorsPolicy = true,
            FrontendHost = "portal",
            CompanyDomain = "company.com",
            LocalDevHost = "localhost",
            LocalDevPort = "3000"
        };

        var result = _orchestrator.Convert(ValidOpenApi, settings);

        Assert.True(result.Success);
        Assert.Contains("policy = <<XML", result.TerraformConfig);
        Assert.Contains("<cors", result.TerraformConfig);
    }

    [Fact]
    public void Convert_MultipleOperations_AllPresent()
    {
        var openApi = """
            {
              "openapi": "3.0.1",
              "info": { "title": "User API", "version": "1.0.0" },
              "paths": {
                "/users": {
                  "get": {
                    "operationId": "getUsers",
                    "summary": "Get Users",
                    "responses": { "200": { "description": "OK" } }
                  },
                  "post": {
                    "operationId": "createUser",
                    "summary": "Create User",
                    "responses": { "201": { "description": "Created" } }
                  }
                },
                "/users/{id}": {
                  "get": {
                    "operationId": "getUserById",
                    "summary": "Get User by ID",
                    "responses": { "200": { "description": "OK" } }
                  },
                  "put": {
                    "operationId": "updateUser",
                    "summary": "Update User",
                    "responses": { "200": { "description": "OK" } }
                  },
                  "delete": {
                    "operationId": "deleteUser",
                    "summary": "Delete User",
                    "responses": { "204": { "description": "No Content" } }
                  }
                }
              }
            }
            """;

        var result = _orchestrator.Convert(openApi, ValidSettings);

        Assert.True(result.Success);
        Assert.Equal(5, result.Configuration!.ApiOperations.Count);
        Assert.Contains(result.Configuration.ApiOperations, o => o.Method == "GET");
        Assert.Contains(result.Configuration.ApiOperations, o => o.Method == "POST");
        Assert.Contains(result.Configuration.ApiOperations, o => o.Method == "PUT");
        Assert.Contains(result.Configuration.ApiOperations, o => o.Method == "DELETE");
    }
}
