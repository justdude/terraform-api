using TerraformApi.Application.Services;
using TerraformApi.Domain.Interfaces;
using TerraformApi.Mcp.Tools;

namespace TerraformApi.Mcp.Tests.Tools;

public class UpdateToolTests
{
    private readonly IConversionOrchestrator _orchestrator;
    private readonly HttpClient _httpClient = new();

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

    private async Task<string> GenerateInitialTerraform()
    {
        var validator = new ApimNamingValidatorService();
        var openApiParser = new OpenApiParserService(validator);
        var apimWriter = new Application.Services.Apim.ApimTerraformWriterService(
            new Application.Services.Hcl.HclWriterService(),
            new Application.Services.Apim.ApimTerraformReaderService(new Application.Services.Hcl.HclParserService()),
            new Application.Services.Sync.OperationCommentBuilderService());

        return await ConvertTool.Convert(
            _httpClient,
            _orchestrator,
            openApiParser,
            apimWriter,
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
    public async Task Update_ValidInput_ReturnsMergedTerraform()
    {
        var existingTf = await GenerateInitialTerraform();
        var tfOnly = existingTf.Split("\n\n// Summary:")[0];

        var result = await UpdateTool.Update(
            _httpClient,
            _orchestrator,
            existingTerraform: tfOnly,
            openApiJson: UpdatedOpenApi,
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
    public async Task Update_AddsNewOperation_PresentInOutput()
    {
        var existingTf = await GenerateInitialTerraform();
        var tfOnly = existingTf.Split("\n\n// Summary:")[0];

        var result = await UpdateTool.Update(
            _httpClient,
            _orchestrator,
            existingTerraform: tfOnly,
            openApiJson: UpdatedOpenApi,
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
    public async Task Update_InvalidJson_ReturnsFailureMessage()
    {
        var result = await UpdateTool.Update(
            _httpClient,
            _orchestrator,
            existingTerraform: "some-group = { }",
            openApiJson: "not valid json",
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
    public async Task Update_InvalidResourceGroup_ReturnsFailureMessage()
    {
        var existingTf = await GenerateInitialTerraform();
        var tfOnly = existingTf.Split("\n\n// Summary:")[0];

        var result = await UpdateTool.Update(
            _httpClient,
            _orchestrator,
            existingTerraform: tfOnly,
            openApiJson: UpdatedOpenApi,
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
    public async Task Update_WithCors_IncludesCorsInMergedOutput()
    {
        var existingTf = await GenerateInitialTerraform();
        var tfOnly = existingTf.Split("\n\n// Summary:")[0];

        var result = await UpdateTool.Update(
            _httpClient,
            _orchestrator,
            existingTerraform: tfOnly,
            openApiJson: UpdatedOpenApi,
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
    public async Task Update_SameSpec_ProducesSameOutput()
    {
        var existingTf = await GenerateInitialTerraform();
        var tfOnly = existingTf.Split("\n\n// Summary:")[0];

        var result = await UpdateTool.Update(
            _httpClient,
            _orchestrator,
            existingTerraform: tfOnly,
            openApiJson: InitialOpenApi,
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

    [Fact]
    public async Task Update_EmptyExistingTerraform_ReturnsError()
    {
        var result = await UpdateTool.Update(
            _httpClient,
            _orchestrator,
            existingTerraform: "",
            openApiJson: InitialOpenApi,
            environment: "dev",
            apiGroupName: "user-api-group",
            stageGroupName: "rg-apim-dev",
            apimName: "apim-company-dev",
            apiPathPrefix: "users",
            apiPathSuffix: "api",
            apiGatewayHost: "api.dev.company.com",
            backendServicePath: "user-service");

        Assert.Contains("Existing Terraform configuration is required", result);
    }

    [Fact]
    public async Task Update_NeitherJsonNorUrl_ReturnsError()
    {
        var result = await UpdateTool.Update(
            _httpClient,
            _orchestrator,
            existingTerraform: "some-group = { }",
            environment: "dev",
            apiGroupName: "user-api-group",
            stageGroupName: "rg-apim-dev",
            apimName: "apim-company-dev",
            apiPathPrefix: "users",
            apiPathSuffix: "api",
            apiGatewayHost: "api.dev.company.com",
            backendServicePath: "user-service");

        Assert.Contains("error", result, StringComparison.OrdinalIgnoreCase);
    }
}
