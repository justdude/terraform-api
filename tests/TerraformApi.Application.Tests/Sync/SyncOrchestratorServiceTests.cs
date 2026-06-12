using Microsoft.Extensions.Logging.Abstractions;
using TerraformApi.Application.Services;
using TerraformApi.Application.Services.Apim;
using TerraformApi.Application.Services.Hcl;
using TerraformApi.Application.Services.Sync;
using TerraformApi.Domain.Models;
using TerraformApi.Domain.Models.Sync;

namespace TerraformApi.Application.Tests.Sync;

/// <summary>
/// Orchestrator tests (§6 Phase 8): Sync from scratch, Sync preserving
/// existing, populated report, Analyze, ApplyProfile both directions.
/// </summary>
public class SyncOrchestratorServiceTests
{
    private readonly SyncOrchestratorService _orchestrator;
    private readonly HclParserService _hclParser = new();

    public SyncOrchestratorServiceTests()
    {
        var validator = new ApimNamingValidatorService();
        var openApiParser = new OpenApiParserService(validator);
        var reader = new ApimTerraformReaderService(_hclParser);
        var hclWriter = new HclWriterService();
        var commentBuilder = new OperationCommentBuilderService();
        var writer = new ApimTerraformWriterService(hclWriter, reader, commentBuilder);
        var resolver = new TerraformInterpolationResolver();
        var synchronizer = new AppendOnlySynchronizerService(
            new OperationMatcherService(resolver),
            new DuplicateDetectorService(),
            new ApimTemplateProfileDetectorService(),
            commentBuilder,
            hclWriter,
            resolver,
            NullLogger<AppendOnlySynchronizerService>.Instance);

        _orchestrator = new SyncOrchestratorService(
            openApiParser,
            reader,
            writer,
            _hclParser,
            synchronizer,
            new ApimTemplateProfileDetectorService(),
            new DuplicateDetectorService(),
            new ApimTemplateProfileApplierService(resolver));
    }

    private static string LoadFixture() =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "example-existing.tf"));

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

    private static ConversionSettings Settings(string groupName = "user-api-group") => new()
    {
        Environment = "dev",
        ApiGroupName = groupName,
        StageGroupName = "rg-apim-dev",
        ApimName = "apim-company-dev",
        ApiPathPrefix = "users",
        ApiPathSuffix = "api",
        ApiGatewayHost = "api.dev.company.com",
        BackendServicePath = "user-service"
    };

    [Fact]
    public void Sync_FromScratch_GeneratesValidConfig()
    {
        var result = _orchestrator.Sync(new SyncRequest
        {
            OpenApiJson = OpenApi,
            ExistingTerraform = null,
            Settings = Settings()
        });

        Assert.True(result.Success, string.Join("; ", result.Errors));
        Assert.Equal(2, result.Report.OperationsAdded);

        // Valid parseable HCL with the default nested structure.
        var parsed = _hclParser.Parse(result.TerraformConfig);
        Assert.NotNull(parsed);
        Assert.Contains("apis", result.TerraformConfig);
        Assert.Contains("api_operations", result.TerraformConfig);
    }

    [Fact]
    public void Sync_WithExisting_PreservesExisting()
    {
        var result = _orchestrator.Sync(new SyncRequest
        {
            OpenApiJson = OpenApi,
            ExistingTerraform = LoadFixture(),
            Settings = Settings("${api_group_name}")
        });

        Assert.True(result.Success, string.Join("; ", result.Errors));
        // The fixture op (GET ${operation_path}) is preserved; both OpenAPI ops added.
        Assert.Equal(1, result.Report.OperationsPreserved);
        Assert.Equal(2, result.Report.OperationsAdded);
        Assert.Contains("${operation_path}", result.TerraformConfig);
    }

    [Fact]
    public void Sync_ProducesPopulatedSyncReport()
    {
        var result = _orchestrator.Sync(new SyncRequest
        {
            OpenApiJson = OpenApi,
            ExistingTerraform = LoadFixture(),
            Settings = Settings("${api_group_name}")
        });

        Assert.Equal("${api_group_name}", result.Report.ApiGroupName);
        Assert.Equal(1, result.Report.TotalOperationsInTerraform);
        Assert.Equal(2, result.Report.TotalOperationsInOpenApi);
        Assert.NotEmpty(result.Report.Diffs);
        Assert.NotEmpty(result.Report.Warnings); // interpolation warnings at minimum
    }

    [Fact]
    public void Sync_InvalidExistingHcl_FailsWithDiagnostics()
    {
        var result = _orchestrator.Sync(new SyncRequest
        {
            OpenApiJson = OpenApi,
            ExistingTerraform = "this is { not valid",
            Settings = Settings()
        });

        Assert.False(result.Success);
        Assert.Contains(result.Errors, e => e.Contains("HCL parse error"));
    }

    [Fact]
    public void Analyze_UserExample_ReturnsExpectedSummary()
    {
        var result = _orchestrator.Analyze(LoadFixture());

        Assert.True(result.Success);
        var group = Assert.Single(result.ApiGroups);
        Assert.Equal("${stage_group_name}", group.ApimResourceGroupName);
        Assert.Equal("${api_name}-${env}", group.ApiName);
        Assert.Equal(1, group.OperationCount);
        Assert.Equal(1, result.TotalOperations);
        Assert.Empty(result.Duplicates);
        Assert.NotNull(result.DetectedProfile);
        Assert.Equal(StylingConfidence.HighlyTemplated, result.DetectedProfile!.Confidence);
        Assert.Equal("UserExampleProfile", result.DetectedProfile.ClosestKnownProfileName);
    }

    [Fact]
    public void Analyze_EmptyInput_Fails()
    {
        var result = _orchestrator.Analyze("");
        Assert.False(result.Success);
    }

    [Fact]
    public void Analyze_InvalidHcl_FailsWithDiagnostics()
    {
        var result = _orchestrator.Analyze("not { valid");
        Assert.False(result.Success);
        Assert.NotEmpty(result.Errors);
    }

    [Fact]
    public void ApplyProfile_Templatize_ReplacesEmptyAndKeepsLiterals()
    {
        var literalTf = """
            g = {
              api_operations = [
                {
                  operation_id             = "get-users-dev"
                  apim_resource_group_name = "rg-apim-dev"
                  apim_name                = ""
                  api_name                 = "my-api-dev"
                  method                   = "GET"
                  url_template             = "users"
                },
              ]
            }
            """;

        var result = _orchestrator.ApplyProfile(
            literalTf,
            ApimTemplateProfile.UserExampleProfile,
            new ApplyProfileOptions { OverwriteExisting = false });

        Assert.True(result.Success);
        // Empty apim_name was templatized; the literal operation_id was kept.
        Assert.Contains("\"${apim_name}\"", result.TerraformConfig);
        Assert.Contains("\"get-users-dev\"", result.TerraformConfig);
        Assert.Contains(result.AppliedChanges, c => c.StartsWith("api_operation.apim_name"));
    }

    [Fact]
    public void ApplyProfile_TemplatizeWithOverwrite_ReplacesLiterals()
    {
        var literalTf = """
            g = {
              api_operations = [
                {
                  operation_id             = "get-users-dev"
                  apim_resource_group_name = "rg-apim-dev"
                  apim_name                = "apim-company-dev"
                  api_name                 = "my-api-dev"
                  method                   = "GET"
                  url_template             = "users"
                },
              ]
            }
            """;

        var result = _orchestrator.ApplyProfile(
            literalTf,
            ApimTemplateProfile.UserExampleProfile,
            new ApplyProfileOptions { OverwriteExisting = true });

        Assert.True(result.Success);
        Assert.Contains("\"${apim_name}\"", result.TerraformConfig);
        Assert.Contains("\"${stage_group_name}\"", result.TerraformConfig);
        Assert.DoesNotContain("apim-company-dev", result.TerraformConfig);
    }

    [Fact]
    public void ApplyProfile_Resolve_SubstitutesVariables()
    {
        var result = _orchestrator.ApplyProfile(
            LoadFixture(),
            profile: null,
            new ApplyProfileOptions(),
            variableValues: new Dictionary<string, string>
            {
                ["stage_group_name"] = "rg-apim-dev",
                ["apim_name"] = "apim-company-dev",
                ["api_name"] = "bpc",
                ["env"] = "dev",
                ["operation_prefix"] = "get-users",
                ["operation_path"] = "users",
                ["operation_display_name"] = "Get users"
            },
            resolve: true);

        Assert.True(result.Success);
        Assert.Contains("\"rg-apim-dev\"", result.TerraformConfig);
        Assert.Contains("\"get-users-dev\"", result.TerraformConfig);
        Assert.Contains("\"bpc-dev\"", result.TerraformConfig);
        // Variables without values produce warnings and stay templated.
        Assert.NotEmpty(result.Warnings);
    }

    [Fact]
    public void ApplyProfile_ResolveWithoutVariables_Fails()
    {
        var result = _orchestrator.ApplyProfile(
            LoadFixture(), null, new ApplyProfileOptions(), variableValues: null, resolve: true);

        Assert.False(result.Success);
    }

    [Fact]
    public void ApplyProfile_TemplatizeWithoutProfile_Fails()
    {
        var result = _orchestrator.ApplyProfile(
            LoadFixture(), null, new ApplyProfileOptions(), resolve: false);

        Assert.False(result.Success);
    }
}
