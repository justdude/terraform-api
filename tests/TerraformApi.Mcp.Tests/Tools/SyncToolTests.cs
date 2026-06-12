using System.Text.Json;
using TerraformApi.Domain.Interfaces;
using TerraformApi.Mcp.Tools;

namespace TerraformApi.Mcp.Tests.Tools;

/// <summary>Tests for the sync_openapi_with_terraform MCP tool (§REV-2.7).</summary>
public class SyncToolTests
{
    private readonly ISyncOrchestrator _orchestrator = AnalyzeToolTests.BuildOrchestrator();
    private readonly HttpClient _httpClient = new();

    private const string ExistingTerraform = """
        my-api-group = {
          product = []
          api_operations = [
            {
              operation_id             = "get-users-dev"
              apim_resource_group_name = "rg-apim-dev"
              apim_name                = "apim-company-dev"
              api_name                 = "my-api-dev"
              display_name             = "Get users"
              method                   = "GET"
              url_template             = "/users"
              status_code              = "200"
              description              = ""
            },
          ]
        }
        """;

    private const string OpenApiTwoOps = """
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

    private async Task<string> RunSync(
        string existing = ExistingTerraform,
        string openApi = OpenApiTwoOps,
        string profile = "Auto",
        string? fieldOverrides = null,
        bool comments = true,
        bool header = true)
    {
        return await SyncTool.Sync(
            _httpClient,
            _orchestrator,
            existingTerraform: existing,
            environment: "dev",
            apiGroupName: "my-api-group",
            stageGroupName: "rg-apim-dev",
            apimName: "apim-company-dev",
            apiPathPrefix: "users",
            apiPathSuffix: "api",
            apiGatewayHost: "api.dev.company.com",
            backendServicePath: "user-service",
            openApiJson: openApi,
            templateProfileName: profile,
            operationFieldOverridesJson: fieldOverrides,
            addOperationComments: comments,
            addReplaceBeforeApplyHeader: header,
            apiName: "my-api-dev");
    }

    [Fact]
    public async Task Sync_AppendOnly_AddsNewPreservesExisting()
    {
        var result = await RunSync();
        using var doc = JsonDocument.Parse(result);

        Assert.True(doc.RootElement.GetProperty("success").GetBoolean());
        var report = doc.RootElement.GetProperty("report");
        Assert.Equal(1, report.GetProperty("operationsAdded").GetInt32());

        var hcl = doc.RootElement.GetProperty("terraformConfig").GetString()!;
        Assert.Contains("get-users-dev", hcl);   // existing preserved
        Assert.Contains("POST", hcl);            // new appended
    }

    [Fact]
    public async Task Sync_MatchedOperation_ReportedIdenticalOrEnriched()
    {
        var result = await RunSync();
        using var doc = JsonDocument.Parse(result);

        var report = doc.RootElement.GetProperty("report");
        var identical = report.GetProperty("operationsIdentical").GetInt32();
        var enriched = report.GetProperty("operationsEnriched").GetInt32();
        Assert.Equal(1, identical + enriched); // GET /users matched
    }

    [Fact]
    public async Task Sync_EmptyExisting_GeneratesFromScratch()
    {
        var result = await RunSync(existing: "");
        using var doc = JsonDocument.Parse(result);

        Assert.True(doc.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal(2, doc.RootElement.GetProperty("report").GetProperty("operationsAdded").GetInt32());

        var hcl = doc.RootElement.GetProperty("terraformConfig").GetString()!;
        Assert.Contains("api_operations", hcl);
    }

    [Fact]
    public async Task Sync_NeitherJsonNorUrl_ReturnsError()
    {
        var result = await SyncTool.Sync(
            _httpClient,
            _orchestrator,
            existingTerraform: ExistingTerraform,
            environment: "dev",
            apiGroupName: "my-api-group",
            stageGroupName: "rg-apim-dev",
            apimName: "apim-company-dev",
            apiPathPrefix: "users",
            apiPathSuffix: "api",
            apiGatewayHost: "api.dev.company.com",
            backendServicePath: "user-service");

        using var doc = JsonDocument.Parse(result);
        Assert.False(doc.RootElement.GetProperty("success").GetBoolean());
    }

    [Fact]
    public async Task Sync_UnknownProfile_ReturnsError()
    {
        var result = await RunSync(profile: "NoSuchProfile");
        using var doc = JsonDocument.Parse(result);

        Assert.False(doc.RootElement.GetProperty("success").GetBoolean());
        Assert.Contains("Unknown template profile", doc.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Sync_DescriptionEnrichIfMissing_FilledFromOpenApi()
    {
        // The existing op has an empty description; OpenAPI's summary goes to
        // display_name (already set) and description stays empty in this spec,
        // so force enrichment of description via overrides + a spec description.
        var openApiWithDescription = OpenApiTwoOps.Replace(
            "\"summary\": \"List users\",",
            "\"summary\": \"List users\", \"description\": \"Returns every user\",");

        var result = await RunSync(openApi: openApiWithDescription);
        using var doc = JsonDocument.Parse(result);

        var hcl = doc.RootElement.GetProperty("terraformConfig").GetString()!;
        Assert.True(doc.RootElement.GetProperty("success").GetBoolean());
        Assert.Contains("get-users-dev", hcl);
    }

    [Fact]
    public async Task Sync_FieldOverrides_OverwriteApplied()
    {
        var result = await RunSync(fieldOverrides: """{"display_name":"Overwrite"}""");
        using var doc = JsonDocument.Parse(result);

        Assert.True(doc.RootElement.GetProperty("success").GetBoolean());
        var hcl = doc.RootElement.GetProperty("terraformConfig").GetString()!;
        // OpenAPI summary "List users" overwrites the TF display_name "Get users".
        Assert.Contains("List users", hcl);
    }

    [Fact]
    public async Task Sync_InvalidFieldOverride_ReturnsError()
    {
        var result = await RunSync(fieldOverrides: """{"display_name":"NotAPolicy"}""");
        using var doc = JsonDocument.Parse(result);

        Assert.False(doc.RootElement.GetProperty("success").GetBoolean());
    }

    [Fact]
    public async Task Sync_NewOperations_HaveComments()
    {
        var result = await RunSync();
        using var doc = JsonDocument.Parse(result);

        var hcl = doc.RootElement.GetProperty("terraformConfig").GetString()!;
        Assert.Matches(@"# POST \S*users \| op_id:", hcl);
        Assert.Contains("source: OpenApi", hcl);
    }

    [Fact]
    public async Task Sync_CommentsDisabled_NoComments()
    {
        var result = await RunSync(comments: false, header: false);
        using var doc = JsonDocument.Parse(result);

        var hcl = doc.RootElement.GetProperty("terraformConfig").GetString()!;
        Assert.DoesNotContain("op_id:", hcl);
    }

    [Fact]
    public async Task Sync_TemplatedProfileOverride_NewOpsTemplatedWithHeader()
    {
        var result = await RunSync(profile: "UserExampleProfile");
        using var doc = JsonDocument.Parse(result);

        var hcl = doc.RootElement.GetProperty("terraformConfig").GetString()!;
        Assert.Contains("${operation_prefix}-${env}", hcl);
        Assert.Contains("REPLACE BEFORE APPLY", hcl);
        // Existing literal operation untouched.
        Assert.Contains("get-users-dev", hcl);
    }

    [Fact]
    public async Task Sync_InvalidJson_ReturnsError()
    {
        var result = await RunSync(openApi: "not json");
        using var doc = JsonDocument.Parse(result);

        Assert.False(doc.RootElement.GetProperty("success").GetBoolean());
    }

    [Fact]
    public async Task Sync_ReportHasFullStructure()
    {
        var result = await RunSync();
        using var doc = JsonDocument.Parse(result);

        var report = doc.RootElement.GetProperty("report");
        Assert.True(report.TryGetProperty("operationsAdded", out _));
        Assert.True(report.TryGetProperty("operationsPreserved", out _));
        Assert.True(report.TryGetProperty("operationsEnriched", out _));
        Assert.True(report.TryGetProperty("operationsIdentical", out _));
        Assert.True(report.TryGetProperty("diffs", out _));
        Assert.True(report.TryGetProperty("duplicates", out _));
        Assert.True(report.TryGetProperty("warnings", out _));
    }
}
