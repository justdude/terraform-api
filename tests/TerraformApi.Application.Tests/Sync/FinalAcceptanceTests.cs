using Microsoft.Extensions.Logging.Abstractions;
using TerraformApi.Application.Services;
using TerraformApi.Application.Services.Apim;
using TerraformApi.Application.Services.Hcl;
using TerraformApi.Application.Services.Sync;
using TerraformApi.Domain.Models;
using TerraformApi.Domain.Models.Hcl;
using TerraformApi.Domain.Models.Sync;

namespace TerraformApi.Application.Tests.Sync;

/// <summary>
/// Final acceptance scenarios A/B/C from §REV-4 of the implementation plan.
/// These tests are mandatory for feature acceptance.
/// </summary>
public class FinalAcceptanceTests
{
    private readonly SyncOrchestratorService _orchestrator;
    private readonly HclParserService _hclParser = new();
    private readonly ApimTerraformReaderService _reader;

    public FinalAcceptanceTests()
    {
        var validator = new ApimNamingValidatorService();
        var openApiParser = new OpenApiParserService(validator);
        _reader = new ApimTerraformReaderService(_hclParser);
        var hclWriter = new HclWriterService();
        var commentBuilder = new OperationCommentBuilderService();
        var writer = new ApimTerraformWriterService(hclWriter, _reader, commentBuilder);
        var resolver = new TerraformInterpolationResolver();
        var synchronizer = new AppendOnlySynchronizerService(
            new OperationMatcherService(resolver),
            new DuplicateDetectorService(),
            new ApimTemplateProfileDetectorService(),
            commentBuilder,
            hclWriter,
            writer,
            resolver,
            NullLogger<AppendOnlySynchronizerService>.Instance);

        _orchestrator = new SyncOrchestratorService(
            openApiParser, _reader, writer, _hclParser, synchronizer,
            new ApimTemplateProfileDetectorService(),
            new DuplicateDetectorService(),
            new ApimTemplateProfileApplierService(resolver),
            new OperationExecutionGraphBuilderService());
    }

    private static string LoadFixture() =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "example-existing.tf"));

    private static ConversionSettings Settings(string groupName = "${api_group_name}") => new()
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

    // -----------------------------------------------------------------
    // Scenario A: Sync without specifying a profile (auto-detect)
    // -----------------------------------------------------------------

    [Fact]
    public void ScenarioA_AutoDetectSync_TwoNewOpsInDetectedStyle()
    {
        const string openApi = """
            {
              "openapi": "3.0.1",
              "info": { "title": "API", "version": "1.0.0" },
              "paths": {
                "/health": {
                  "get": { "operationId": "getHealth", "summary": "Health", "responses": { "200": { "description": "OK" } } }
                },
                "/users": {
                  "get": { "operationId": "listUsers", "summary": "List users", "responses": { "200": { "description": "OK" } } }
                },
                "/users/{id}": {
                  "get": { "operationId": "getUserById", "summary": "Get user", "responses": { "200": { "description": "OK" } } }
                }
              }
            }
            """;

        var result = _orchestrator.Sync(new SyncRequest
        {
            OpenApiJson = openApi,
            ExistingTerraform = LoadFixture(),
            Settings = Settings(),
            // Resolved-mode context: the fixture's url_template = "${operation_path}"
            // matches GET /users/{id} once resolved.
            MatchStrategy = new OperationMatchStrategy
            {
                VariableContext = new Dictionary<string, string>
                {
                    ["operation_path"] = "users/{id}"
                }
            }
        });

        Assert.True(result.Success, string.Join("; ", result.Errors));

        // 1. Output HCL is parseable and valid.
        var reparsed = _reader.Read(result.TerraformConfig);
        var group = Assert.Single(reparsed.ApiGroups);

        // 2. Exactly 2 new entries (health + users); /users/{id} matched the existing op.
        Assert.Equal(2, result.Report.OperationsAdded);
        Assert.Equal(3, group.Operations.Count);

        // 7. Report counters.
        Assert.Equal(1, result.Report.OperationsIdentical + result.Report.OperationsEnriched);

        // 3. First comment line of a new operation: "# GET <url> | op_id: <id>".
        Assert.Matches(@"# GET \S*health \| op_id: ", result.TerraformConfig);

        // 4. The existing operation is unchanged.
        Assert.Contains("operation_id             = \"${operation_prefix}-${env}\"", result.TerraformConfig);
        Assert.Contains("url_template             = \"${operation_path}\"", result.TerraformConfig);

        // 5. Auto-detected profile: new operations use the file's placeholders.
        var newOps = reparsed.ApiGroups.Single().Operations
            .Where(o => o.UrlTemplate.StructuralText != "${operation_path}")
            .ToList();
        Assert.Equal(2, newOps.Count);
        Assert.All(newOps, op =>
        {
            Assert.Equal("${operation_prefix}-${env}", op.OperationId.StructuralText);
            Assert.Equal("${stage_group_name}", op.ApimResourceGroupName?.StructuralText);
        });

        // 6. REPLACE BEFORE APPLY header with placeholders.
        Assert.Contains("REPLACE BEFORE APPLY", result.TerraformConfig);
    }

    // -----------------------------------------------------------------
    // Scenario B: Conversion from scratch
    // -----------------------------------------------------------------

    [Fact]
    public void ScenarioB_FromScratch_TemplatedStructureWithComments()
    {
        const string openApi = """
            {
              "openapi": "3.0.1",
              "info": { "title": "API", "version": "1.0.0" },
              "paths": {
                "/a": { "get": { "operationId": "opA", "summary": "A", "responses": { "200": { "description": "OK" } } } },
                "/b": { "post": { "operationId": "opB", "summary": "B", "responses": { "200": { "description": "OK" } } } },
                "/c": { "delete": { "operationId": "opC", "summary": "C", "responses": { "200": { "description": "OK" } } } }
              }
            }
            """;

        var result = _orchestrator.Sync(new SyncRequest
        {
            OpenApiJson = openApi,
            ExistingTerraform = "",
            Settings = Settings(),
            Options = new SyncOptions
            {
                OverrideProfile = ApimTemplateProfile.UserExampleProfile,
                AddOperationComments = true
            }
        });

        Assert.True(result.Success, string.Join("; ", result.Errors));

        // 1. Structure apis.bpc_apis.backend_apis."${api_group_name}".
        var doc = _hclParser.Parse(result.TerraformConfig);
        var apis = Assert.IsType<HclObject>(doc.RootAssignments.Single(a => a.Key == "apis").Value);
        var bpc = Assert.IsType<HclObject>(apis.Get("bpc_apis"));
        var backend = Assert.IsType<HclObject>(bpc.Get("backend_apis"));
        var groupAssignment = backend.Assignments.Single();
        Assert.Equal("${api_group_name}", groupAssignment.Key);
        Assert.True(groupAssignment.KeyIsQuoted);

        // 2. Exactly 3 operations.
        var reparsed = _reader.Read(doc);
        Assert.Equal(3, reparsed.ApiGroups.Single().Operations.Count);

        // 3. Each operation has 3 leading comments (METHOD URL | op_id, source, placeholders).
        var opsArray = (HclArray)reparsed.ApiGroups.Single().AstNode.Get("api_operations")!;
        Assert.All(opsArray.Items, item => Assert.Equal(3, item.LeadingComments.Count));

        // 4. The REPLACE BEFORE APPLY header is present.
        Assert.Contains("REPLACE BEFORE APPLY", result.TerraformConfig);

        // 5. All required operation fields are interpolations from the profile.
        var op = reparsed.ApiGroups.Single().Operations[0];
        Assert.Equal("${operation_prefix}-${env}", op.OperationId.StructuralText);
        Assert.Equal("${stage_group_name}", op.ApimResourceGroupName?.StructuralText);
        Assert.Equal("${api_name}-${env}", op.ApiName?.StructuralText);
    }

    // -----------------------------------------------------------------
    // Scenario C: Analyze
    // -----------------------------------------------------------------

    [Fact]
    public void ScenarioC_AnalyzeUserExample_FullDiagnostics()
    {
        var result = _orchestrator.Analyze(LoadFixture());

        // 1–3: success, one group, one operation.
        Assert.True(result.Success);
        Assert.Single(result.ApiGroups);
        Assert.Equal(1, result.TotalOperations);

        // 4–5: profile detection.
        Assert.Equal(StylingConfidence.HighlyTemplated, result.DetectedProfile!.Confidence);
        Assert.Equal("UserExampleProfile", result.DetectedProfile.ClosestKnownProfileName);

        // 6: the full variable dictionary.
        string[] expected =
        [
            "stage_group_name", "apim_name", "api_name", "env", "operation_prefix",
            "operation_path", "frontend_host", "company_domain", "local_dev_host",
            "local_dev_port", "api_path_prefix", "api_path_suffix", "api_gateway_host",
            "api_version", "backend_service_path", "api_revision", "product_id",
            "api_display_name", "operation_display_name"
        ];
        foreach (var name in expected)
            Assert.Contains(name, result.DetectedProfile.AllReferencedVariables);
    }
}
