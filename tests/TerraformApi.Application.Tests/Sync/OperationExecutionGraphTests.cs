using System.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using TerraformApi.Application.Services;
using TerraformApi.Application.Services.Apim;
using TerraformApi.Application.Services.Hcl;
using TerraformApi.Application.Services.Sync;
using TerraformApi.Domain.Models;
using TerraformApi.Domain.Models.Sync;
using TerraformApi.Domain.Models.Tracking;

namespace TerraformApi.Application.Tests.Sync;

/// <summary>
/// ExecutionGraph tests (plan §8 DoD, §9 acceptance) and the I3 performance
/// scenario (§5.7: 50+ operations, 5+ groups, &lt; 1 second).
/// </summary>
public class OperationExecutionGraphTests
{
    private readonly SyncOrchestratorService _orchestrator;
    private readonly OperationExecutionGraphBuilderService _builder = new();

    public OperationExecutionGraphTests()
    {
        var validator = new ApimNamingValidatorService();
        var openApiParser = new OpenApiParserService(validator);
        var hclParser = new HclParserService();
        var reader = new ApimTerraformReaderService(hclParser);
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
            openApiParser, reader, writer, hclParser, synchronizer,
            new ApimTemplateProfileDetectorService(),
            new DuplicateDetectorService(),
            new ApimTemplateProfileApplierService(resolver),
            _builder);
    }

    private static ConversionSettings Settings(string groupName = "my-api-group") => new()
    {
        Environment = "dev",
        ApiGroupName = groupName,
        StageGroupName = "rg-apim-dev",
        ApimName = "apim-company-dev",
        ApiPathPrefix = "users",
        ApiPathSuffix = "api",
        ApiGatewayHost = "api.dev.company.com",
        BackendServicePath = "user-service",
        ApiName = "my-api-dev"
    };

    // -----------------------------------------------------------------
    // Builder unit tests
    // -----------------------------------------------------------------

    [Fact]
    public void Build_MapsDiffKindsToStatuses()
    {
        var report = new SyncReport
        {
            GeneratedAt = DateTime.UtcNow,
            ApiGroupName = "g",
            OperationsPreserved = 1,
            Diffs =
            [
                new OperationDiff
                {
                    Kind = OperationDiffKind.AddedFromOpenApi,
                    OpenApiFingerprint = new OperationFingerprint { OperationId = "new-op", Method = "POST", UrlTemplate = "x" },
                    AppliedChanges = ["appended POST x"]
                },
                new OperationDiff
                {
                    Kind = OperationDiffKind.Identical,
                    TerraformFingerprint = new OperationFingerprint { OperationId = "same-op", Method = "GET", UrlTemplate = "y" }
                },
                new OperationDiff
                {
                    Kind = OperationDiffKind.Changed,
                    TerraformFingerprint = new OperationFingerprint { OperationId = "enriched-op", Method = "GET", UrlTemplate = "z" },
                    AppliedChanges = ["description"]
                },
                new OperationDiff
                {
                    Kind = OperationDiffKind.PreservedFromTerraform,
                    TerraformFingerprint = new OperationFingerprint { OperationId = "tf-only-op", Method = "DELETE", UrlTemplate = "w" }
                }
            ]
        };

        var graph = _builder.BuildFromSyncReport(report, "g");

        Assert.Equal(4, graph.Nodes.Count);
        Assert.Equal(OperationNodeStatus.New, graph.Nodes[0].Status);
        Assert.Equal(OperationNodeStatus.Included, graph.Nodes[1].Status);
        Assert.Equal(OperationNodeStatus.Modified, graph.Nodes[2].Status);
        Assert.Equal(OperationNodeStatus.Included, graph.Nodes[3].Status);
        Assert.Equal(OperationNodeOrigin.ExistingTerraform, graph.Nodes[3].Origin);

        Assert.Equal(4, graph.Statistics.TotalOperations);
        Assert.Equal(1, graph.Statistics.NewOperations);
        Assert.Equal(1, graph.Statistics.ModifiedOperations);
        Assert.Equal(1, graph.Statistics.PreservedOperations);
        Assert.Equal(4, graph.Statistics.IncludedOperations);
        Assert.Equal(0, graph.Statistics.SkippedOperations);
    }

    [Fact]
    public void Build_ReportOnlyDiff_CountedAsSkipped()
    {
        var report = new SyncReport
        {
            GeneratedAt = DateTime.UtcNow,
            ApiGroupName = "g",
            Diffs =
            [
                new OperationDiff
                {
                    Kind = OperationDiffKind.AddedFromOpenApi,
                    OpenApiFingerprint = new OperationFingerprint { OperationId = "not-applied" },
                    SkippedDueToPolicy = ["new operation not appended (NewOperationPolicy=ReportOnly)"]
                }
            ]
        };

        var graph = _builder.BuildFromSyncReport(report, "g");

        Assert.Equal(OperationNodeStatus.Skipped, graph.Nodes.Single().Status);
        Assert.Equal(1, graph.Statistics.SkippedOperations);
        Assert.Equal(0, graph.Statistics.TotalOperations);
    }

    // -----------------------------------------------------------------
    // §9 acceptance: 1 existing + 1 new → Total 2, New 1, Included 2
    // -----------------------------------------------------------------

    [Fact]
    public void Sync_OneExistingOneNew_GraphStatisticsPerPlan()
    {
        const string existing = """
            my-api-group = {
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

        const string openApi = """
            {
              "openapi": "3.0.1",
              "info": { "title": "API", "version": "1.0.0" },
              "paths": {
                "/users": {
                  "get": { "operationId": "listUsers", "summary": "Get users", "responses": { "200": { "description": "OK" } } },
                  "post": { "operationId": "createUser", "summary": "Create user", "responses": { "201": { "description": "Created" } } }
                }
              }
            }
            """;

        var result = _orchestrator.Sync(new SyncRequest
        {
            OpenApiJson = openApi,
            ExistingTerraform = existing,
            Settings = Settings()
        });

        Assert.True(result.Success);
        Assert.NotNull(result.ExecutionGraph);

        var stats = result.ExecutionGraph!.Statistics;
        Assert.Equal(2, stats.TotalOperations);
        Assert.Equal(1, stats.NewOperations);
        Assert.Equal(2, stats.IncludedOperations);
    }

    // -----------------------------------------------------------------
    // I3: large file performance (§5.7)
    // -----------------------------------------------------------------

    [Fact]
    public void Sync_LargeFile_50PlusOpsAnd5Groups_UnderOneSecond()
    {
        // 5 groups × 12 operations = 60 operations.
        var groups = string.Join("\n", Enumerable.Range(1, 5).Select(g => $$"""
            group-{{g}} = {
              product = []
              api_operations = [
            {{string.Join("\n", Enumerable.Range(1, 12).Select(i => $$"""
                {
                  operation_id             = "op-{{g}}-{{i}}"
                  apim_resource_group_name = "rg-{{g}}"
                  apim_name                = "apim-company-dev"
                  api_name                 = "api-{{g}}"
                  display_name             = "Operation {{g}}-{{i}}"
                  method                   = "GET"
                  url_template             = "/group{{g}}/items/{{i}}"
                  status_code              = "200"
                  description              = ""
                },
            """))}}
              ]
            }
            """));

        // OpenAPI: intersecting subset for group 3 (6 existing + 4 new).
        var paths = string.Join(",\n", Enumerable.Range(1, 6).Select(i => $$"""
            "/group3/items/{{i}}": {
              "get": { "operationId": "op-3-{{i}}", "summary": "Operation 3-{{i}}", "responses": { "200": { "description": "OK" } } }
            }
            """).Concat(Enumerable.Range(100, 4).Select(i => $$"""
            "/group3/new/{{i}}": {
              "post": { "operationId": "new-op-{{i}}", "summary": "New {{i}}", "responses": { "200": { "description": "OK" } } }
            }
            """)));

        var openApi = $$"""
            {
              "openapi": "3.0.1",
              "info": { "title": "Big API", "version": "1.0.0" },
              "paths": { {{paths}} }
            }
            """;

        var settings = Settings("group-3") with { StageGroupName = "rg-3", ApiName = "api-3" };

        var stopwatch = Stopwatch.StartNew();
        var result = _orchestrator.Sync(new SyncRequest
        {
            OpenApiJson = openApi,
            ExistingTerraform = groups,
            Settings = settings
        });
        stopwatch.Stop();

        Assert.True(result.Success, string.Join("; ", result.Errors));
        Assert.Equal(4, result.Report.OperationsAdded);
        Assert.Equal(6, result.Report.OperationsIdentical + result.Report.OperationsEnriched);

        // Untouched groups stay byte-for-byte.
        Assert.Contains("operation_id             = \"op-1-1\"", result.TerraformConfig);
        Assert.Contains("operation_id             = \"op-5-12\"", result.TerraformConfig);

        Assert.True(stopwatch.ElapsedMilliseconds < 1000,
            $"Sync took {stopwatch.ElapsedMilliseconds} ms — must be under 1000 ms");
    }
}
