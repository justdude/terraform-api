using TerraformApi.Application.Services;
using TerraformApi.Domain.Interfaces;
using TerraformApi.Mcp.Tools;

namespace TerraformApi.Mcp.Tests.Tools;

public class UpdateToolTests
{
    private readonly IConversionOrchestrator _orchestrator;

    private const string InitialOpenApi = """
        {
          "openapi": "3.0.1",
          "info": { "title": "User API", "version": "1.0.0" },
          "paths": {
            "/users": {
              "get": {
                "operationId": "listUsers",
                "summary": "List users",
                "responses": { "200": { "description": "OK" } }
              }
            }
          }
        }
        """;

    private const string UpdatedOpenApi = """
        {
          "openapi": "3.0.1",
          "info": { "title": "User API", "version": "2.0.0" },
          "paths": {
            "/users": {
              "get": {
                "operationId": "listUsers",
                "summary": "List users v2",
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

    public UpdateToolTests()
    {
        var validator = new ApimNamingValidatorService();
        var parser = new OpenApiParserService(validator);
        var generator = new TerraformGeneratorService();
        var merger = new TerraformMergerService(generator);
        _orchestrator = new ConversionOrchestratorService(parser, generator, merger, validator);
    }

    private string GenerateInitialTerraform()
    {
        return ConvertTool.Convert(
            _orchestrator,
            openApiJson: InitialOpenApi,
            environment: "dev",
            apiGroupName: "user-api-group",
            stageGroupName: "rg-apim-dev",
            apimName: "apim-company-dev",
            apiPathPrefix: "users",
            apiPathSuffix: "api",
            apiGatewayHost: "api.dev.company.com",
            backendServicePath: "user-service");
    }

    [Fact]
    public void Update_ValidInput_ReturnsMergedTerraform()
    {
        var existingTf = GenerateInitialTerraform();
        // Strip the summary comment so it's clean HCL
        var tfOnly = existingTf.Split("\n\n// Summary:")[0];

        var result = UpdateTool.Update(
            _orchestrator,
            openApiJson: UpdatedOpenApi,
            existingTerraform: tfOnly,
            environment: "dev",
            apiGroupName: "user-api-group",
            stageGroupName: "rg-apim-dev",
            apimName: "apim-company-dev",
            apiPathPrefix: "users",
            apiPathSuffix: "api",
            apiGatewayHost: "api.dev.company.com",
            backendServicePath: "user-service");

        Assert.Contains("user-api-group = {", result);
        Assert.Contains("listusers", result.ToLowerInvariant());
        Assert.Contains("createuser", result.ToLowerInvariant());
    }

    [Fact]
    public void Update_AddsNewOperation_PresentInOutput()
    {
        var existingTf = GenerateInitialTerraform();
        var tfOnly = existingTf.Split("\n\n// Summary:")[0];

        var result = UpdateTool.Update(
            _orchestrator,
            openApiJson: UpdatedOpenApi,
            existingTerraform: tfOnly,
            environment: "dev",
            apiGroupName: "user-api-group",
            stageGroupName: "rg-apim-dev",
            apimName: "apim-company-dev",
            apiPathPrefix: "users",
            apiPathSuffix: "api",
            apiGatewayHost: "api.dev.company.com",
            backendServicePath: "user-service");

        Assert.Contains("createuser", result.ToLowerInvariant());
        Assert.Contains("// Summary:", result);
        Assert.Contains("2 operations", result);
    }

    [Fact]
    public void Update_InvalidJson_ReturnsFailureMessage()
    {
        var result = UpdateTool.Update(
            _orchestrator,
            openApiJson: "not valid json",
            existingTerraform: "some-group = { }",
            environment: "dev",
            apiGroupName: "user-api-group",
            stageGroupName: "rg-apim-dev",
            apimName: "apim-company-dev",
            apiPathPrefix: "users",
            apiPathSuffix: "api",
            apiGatewayHost: "api.dev.company.com",
            backendServicePath: "user-service");

        Assert.StartsWith("Update failed:", result);
    }

    [Fact]
    public void Update_InvalidResourceGroup_ReturnsFailureMessage()
    {
        var existingTf = GenerateInitialTerraform();
        var tfOnly = existingTf.Split("\n\n// Summary:")[0];

        var result = UpdateTool.Update(
            _orchestrator,
            openApiJson: UpdatedOpenApi,
            existingTerraform: tfOnly,
            environment: "dev",
            apiGroupName: "user-api-group",
            stageGroupName: "bad group name!!",
            apimName: "apim-company-dev",
            apiPathPrefix: "users",
            apiPathSuffix: "api",
            apiGatewayHost: "api.dev.company.com",
            backendServicePath: "user-service");

        Assert.StartsWith("Update failed:", result);
    }

    [Fact]
    public void Update_WithCors_IncludesCorsInMergedOutput()
    {
        var existingTf = GenerateInitialTerraform();
        var tfOnly = existingTf.Split("\n\n// Summary:")[0];

        var result = UpdateTool.Update(
            _orchestrator,
            openApiJson: UpdatedOpenApi,
            existingTerraform: tfOnly,
            environment: "dev",
            apiGroupName: "user-api-group",
            stageGroupName: "rg-apim-dev",
            apimName: "apim-company-dev",
            apiPathPrefix: "users",
            apiPathSuffix: "api",
            apiGatewayHost: "api.dev.company.com",
            backendServicePath: "user-service",
            includeCorsPolicy: true,
            frontendHost: "portal",
            companyDomain: "company.com");

        Assert.Contains("policy = <<XML", result);
        Assert.Contains("<cors", result);
    }

    [Fact]
    public void Update_SameSpec_ProducesSameOutput()
    {
        var existingTf = GenerateInitialTerraform();
        var tfOnly = existingTf.Split("\n\n// Summary:")[0];

        var result = UpdateTool.Update(
            _orchestrator,
            openApiJson: InitialOpenApi,
            existingTerraform: tfOnly,
            environment: "dev",
            apiGroupName: "user-api-group",
            stageGroupName: "rg-apim-dev",
            apimName: "apim-company-dev",
            apiPathPrefix: "users",
            apiPathSuffix: "api",
            apiGatewayHost: "api.dev.company.com",
            backendServicePath: "user-service");

        Assert.Contains("user-api-group = {", result);
        Assert.Contains("listusers", result.ToLowerInvariant());
        Assert.Contains("1 operations", result);
    }
}
