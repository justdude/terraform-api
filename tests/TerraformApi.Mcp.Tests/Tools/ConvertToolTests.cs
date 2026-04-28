using TerraformApi.Application.Services;
using TerraformApi.Domain.Interfaces;
using TerraformApi.Mcp.Tools;

namespace TerraformApi.Mcp.Tests.Tools;

public class ConvertToolTests
{
    private readonly IConversionOrchestrator _orchestrator;

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
              },
              "post": {
                "operationId": "createPet",
                "summary": "Create a pet",
                "responses": { "201": { "description": "Created" } }
              }
            },
            "/pets/{id}": {
              "get": {
                "operationId": "getPet",
                "summary": "Get a pet by ID",
                "responses": { "200": { "description": "OK" } }
              }
            }
          }
        }
        """;

    public ConvertToolTests()
    {
        var validator = new ApimNamingValidatorService();
        var parser = new OpenApiParserService(validator);
        var generator = new TerraformGeneratorService();
        var merger = new TerraformMergerService(generator);
        _orchestrator = new ConversionOrchestratorService(parser, generator, merger, validator);
    }

    [Fact]
    public void Convert_ValidInput_ReturnsTerraformConfig()
    {
        var result = ConvertTool.Convert(
            _orchestrator,
            openApiJson: ValidOpenApi,
            environment: "dev",
            apiGroupName: "pet-store-group",
            stageGroupName: "rg-apim-dev",
            apimName: "apim-company-dev",
            apiPathPrefix: "petstore",
            apiPathSuffix: "api",
            apiGatewayHost: "api.dev.company.com",
            backendServicePath: "pet-service");

        Assert.Contains("pet-store-group = {", result);
        Assert.Contains("api_operations = [", result);
        Assert.Contains("rg-apim-dev", result);
        Assert.Contains("apim-company-dev", result);
    }

    [Fact]
    public void Convert_ValidInput_ContainsAllOperations()
    {
        var result = ConvertTool.Convert(
            _orchestrator,
            openApiJson: ValidOpenApi,
            environment: "dev",
            apiGroupName: "pet-store-group",
            stageGroupName: "rg-apim-dev",
            apimName: "apim-company-dev",
            apiPathPrefix: "petstore",
            apiPathSuffix: "api",
            apiGatewayHost: "api.dev.company.com",
            backendServicePath: "pet-service");

        Assert.Contains("listpets", result.ToLowerInvariant());
        Assert.Contains("createpet", result.ToLowerInvariant());
        Assert.Contains("getpet", result.ToLowerInvariant());
    }

    [Fact]
    public void Convert_ValidInput_AppendsSummaryComment()
    {
        var result = ConvertTool.Convert(
            _orchestrator,
            openApiJson: ValidOpenApi,
            environment: "dev",
            apiGroupName: "pet-store-group",
            stageGroupName: "rg-apim-dev",
            apimName: "apim-company-dev",
            apiPathPrefix: "petstore",
            apiPathSuffix: "api",
            apiGatewayHost: "api.dev.company.com",
            backendServicePath: "pet-service");

        Assert.Contains("// Summary:", result);
        Assert.Contains("3 operations", result);
    }

    [Fact]
    public void Convert_InvalidJson_ReturnsFailureMessage()
    {
        var result = ConvertTool.Convert(
            _orchestrator,
            openApiJson: "{this is not valid json!!",
            environment: "dev",
            apiGroupName: "pet-store-group",
            stageGroupName: "rg-apim-dev",
            apimName: "apim-company-dev",
            apiPathPrefix: "petstore",
            apiPathSuffix: "api",
            apiGatewayHost: "api.dev.company.com",
            backendServicePath: "pet-service");

        Assert.StartsWith("Conversion failed:", result);
    }

    [Fact]
    public void Convert_InvalidResourceGroup_ReturnsFailureMessage()
    {
        var result = ConvertTool.Convert(
            _orchestrator,
            openApiJson: ValidOpenApi,
            environment: "dev",
            apiGroupName: "pet-store-group",
            stageGroupName: "invalid group name with spaces!",
            apimName: "apim-company-dev",
            apiPathPrefix: "petstore",
            apiPathSuffix: "api",
            apiGatewayHost: "api.dev.company.com",
            backendServicePath: "pet-service");

        Assert.StartsWith("Conversion failed:", result);
        Assert.Contains("StageGroupName", result);
    }

    [Fact]
    public void Convert_WithCorsPolicy_IncludesCorsBlock()
    {
        var result = ConvertTool.Convert(
            _orchestrator,
            openApiJson: ValidOpenApi,
            environment: "dev",
            apiGroupName: "pet-store-group",
            stageGroupName: "rg-apim-dev",
            apimName: "apim-company-dev",
            apiPathPrefix: "petstore",
            apiPathSuffix: "api",
            apiGatewayHost: "api.dev.company.com",
            backendServicePath: "pet-service",
            includeCorsPolicy: true,
            frontendHost: "portal",
            companyDomain: "company.com",
            localDevHost: "localhost",
            localDevPort: "3000");

        Assert.Contains("policy = <<XML", result);
        Assert.Contains("<cors", result);
        Assert.Contains("portal.dev.company.com", result);
    }

    [Fact]
    public void Convert_WithCustomApiName_UsesCustomName()
    {
        var result = ConvertTool.Convert(
            _orchestrator,
            openApiJson: ValidOpenApi,
            environment: "dev",
            apiGroupName: "pet-store-group",
            stageGroupName: "rg-apim-dev",
            apimName: "apim-company-dev",
            apiPathPrefix: "petstore",
            apiPathSuffix: "api",
            apiGatewayHost: "api.dev.company.com",
            backendServicePath: "pet-service",
            apiName: "custom-api-name");

        Assert.Contains("custom-api-name", result);
    }

    [Fact]
    public void Convert_WithProductGeneration_IncludesProductBlock()
    {
        var result = ConvertTool.Convert(
            _orchestrator,
            openApiJson: ValidOpenApi,
            environment: "dev",
            apiGroupName: "pet-store-group",
            stageGroupName: "rg-apim-dev",
            apimName: "apim-company-dev",
            apiPathPrefix: "petstore",
            apiPathSuffix: "api",
            apiGatewayHost: "api.dev.company.com",
            backendServicePath: "pet-service",
            generateProduct: true,
            productDisplayName: "Pet Store Product",
            productDescription: "Access to the Pet Store API");

        Assert.Contains("product = [", result);
        Assert.Contains("Pet Store Product", result);
        Assert.Contains("Access to the Pet Store API", result);
    }

    [Fact]
    public void Convert_WithSubscriptionRequired_SetsFlag()
    {
        var result = ConvertTool.Convert(
            _orchestrator,
            openApiJson: ValidOpenApi,
            environment: "dev",
            apiGroupName: "pet-store-group",
            stageGroupName: "rg-apim-dev",
            apimName: "apim-company-dev",
            apiPathPrefix: "petstore",
            apiPathSuffix: "api",
            apiGatewayHost: "api.dev.company.com",
            backendServicePath: "pet-service",
            subscriptionRequired: true);

        Assert.Contains("subscription_required", result);
        Assert.Contains("true", result);
    }

    [Fact]
    public void Convert_DifferentEnvironments_ProducesDifferentOutput()
    {
        var devResult = ConvertTool.Convert(
            _orchestrator,
            openApiJson: ValidOpenApi,
            environment: "dev",
            apiGroupName: "pet-store-group",
            stageGroupName: "rg-apim-dev",
            apimName: "apim-company-dev",
            apiPathPrefix: "petstore",
            apiPathSuffix: "api",
            apiGatewayHost: "api.dev.company.com",
            backendServicePath: "pet-service");

        var prodResult = ConvertTool.Convert(
            _orchestrator,
            openApiJson: ValidOpenApi,
            environment: "prod",
            apiGroupName: "pet-store-group",
            stageGroupName: "rg-apim-prod",
            apimName: "apim-company-prod",
            apiPathPrefix: "petstore",
            apiPathSuffix: "api",
            apiGatewayHost: "api.company.com",
            backendServicePath: "pet-service");

        Assert.Contains("pet-store-dev", devResult);
        Assert.Contains("pet-store-prod", prodResult);
        Assert.DoesNotContain("pet-store-prod", devResult);
        Assert.DoesNotContain("pet-store-dev", prodResult);
    }

    [Fact]
    public void Convert_SingleOperation_ReportsOneOperation()
    {
        var singleOpApi = """
            {
              "openapi": "3.0.1",
              "info": { "title": "Simple API", "version": "1.0.0" },
              "paths": {
                "/health": {
                  "get": {
                    "operationId": "healthCheck",
                    "summary": "Health Check",
                    "responses": { "200": { "description": "OK" } }
                  }
                }
              }
            }
            """;

        var result = ConvertTool.Convert(
            _orchestrator,
            openApiJson: singleOpApi,
            environment: "dev",
            apiGroupName: "simple-group",
            stageGroupName: "rg-apim-dev",
            apimName: "apim-company-dev",
            apiPathPrefix: "simple",
            apiPathSuffix: "api",
            apiGatewayHost: "api.dev.company.com",
            backendServicePath: "simple-service");

        Assert.Contains("1 operations", result);
        Assert.Contains("healthcheck", result.ToLowerInvariant());
    }
}
