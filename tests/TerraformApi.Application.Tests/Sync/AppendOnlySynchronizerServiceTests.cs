using Microsoft.Extensions.Logging.Abstractions;
using TerraformApi.Application.Services.Apim;
using TerraformApi.Application.Services.Hcl;
using TerraformApi.Application.Services.Sync;
using TerraformApi.Domain.Models;
using TerraformApi.Domain.Models.Apim;
using TerraformApi.Domain.Models.Hcl;
using TerraformApi.Domain.Models.Sync;

namespace TerraformApi.Application.Tests.Sync;

/// <summary>
/// Append-only synchronizer tests S1–S17 (§5.6, §REV-1.7 Phase 7) plus the
/// real-user-example acceptance test.
/// </summary>
public class AppendOnlySynchronizerServiceTests
{
    private readonly HclParserService _parser = new();
    private readonly ApimTerraformReaderService _reader;
    private readonly AppendOnlySynchronizerService _synchronizer;

    public AppendOnlySynchronizerServiceTests()
    {
        _reader = new ApimTerraformReaderService(_parser);
        var resolver = new TerraformInterpolationResolver();
        _synchronizer = new AppendOnlySynchronizerService(
            new OperationMatcherService(resolver),
            new DuplicateDetectorService(),
            new ApimTemplateProfileDetectorService(),
            new OperationCommentBuilderService(),
            new HclWriterService(),
            resolver,
            NullLogger<AppendOnlySynchronizerService>.Instance);
    }

    private static string LoadFixture() =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "example-existing.tf"));

    // -----------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------

    private static ApimApiOperation Op(
        string opId, string method, string url,
        string description = "", string? displayName = null,
        List<ApimOperationRequest>? requests = null,
        List<ApimOperationResponse>? responses = null) => new()
    {
        OperationId = opId,
        ApimResourceGroupName = "rg-apim-dev",
        ApimName = "apim-company-dev",
        ApiName = "my-api-dev",
        DisplayName = displayName ?? opId,
        Method = method,
        UrlTemplate = url,
        Description = description,
        Requests = requests ?? [],
        Responses = responses ?? []
    };

    private static ApimConfiguration Config(string groupName, params ApimApiOperation[] ops) => new()
    {
        ApiGroupName = groupName,
        Api = new ApimApi
        {
            ApimResourceGroupName = "rg-apim-dev",
            ApimName = "apim-company-dev",
            Name = "my-api-dev",
            DisplayName = "My API - dev",
            Path = "myapp.dev/v1/api",
            ServiceUrl = "https://api-dev.company.com/v1/svc/"
        },
        ApiOperations = ops.ToList()
    };

    private static string TfOperation(string opId, string method, string url, string description = "") => $$"""
            {
              operation_id             = "{{opId}}"
              apim_resource_group_name = "rg-apim-dev"
              apim_name                = "apim-company-dev"
              api_name                 = "my-api-dev"
              display_name             = "{{opId}}"
              method                   = "{{method}}"
              url_template             = "{{url}}"
              status_code              = "200"
              description              = "{{description}}"
            },
    """;

    private static string TfGroup(string name, params string[] ops) => $$"""
        {{name}} = {
          product = []
          api_operations = [
        {{string.Join("\n", ops)}}
          ]
        }
        """;

    private SyncResult Sync(string terraform, ApimConfiguration config,
        MergePolicy? policy = null, OperationMatchStrategy? strategy = null, SyncOptions? options = null)
    {
        var parsed = _reader.Read(terraform);
        return _synchronizer.Synchronize(
            parsed, config, policy ?? new MergePolicy(), strategy ?? new OperationMatchStrategy(), options);
    }

    // -----------------------------------------------------------------
    // S1–S12
    // -----------------------------------------------------------------

    // S1
    [Fact]
    public void Synchronize_IdenticalSets_NoChangesAllIdentical()
    {
        var tf = TfGroup("g",
            TfOperation("op-a", "GET", "/a"), TfOperation("op-b", "POST", "/b"),
            TfOperation("op-c", "GET", "/c"), TfOperation("op-d", "DELETE", "/d"),
            TfOperation("op-e", "PUT", "/e"));

        var config = Config("g",
            Op("op-a", "GET", "/a"), Op("op-b", "POST", "/b"),
            Op("op-c", "GET", "/c"), Op("op-d", "DELETE", "/d"),
            Op("op-e", "PUT", "/e"));

        var result = Sync(tf, config);

        Assert.True(result.Success);
        Assert.Equal(5, result.Report.OperationsIdentical);
        Assert.Equal(0, result.Report.OperationsAdded);
        Assert.Equal(0, result.Report.OperationsEnriched);
        Assert.Equal(tf, result.TerraformConfig); // byte-for-byte unchanged
    }

    // S2
    [Fact]
    public void Synchronize_TwoNewOperations_AppendedAtEnd()
    {
        var tf = TfGroup("g", TfOperation("op-a", "GET", "/a"));
        var config = Config("g",
            Op("op-a", "GET", "/a"),
            Op("op-new1", "POST", "/new1"),
            Op("op-new2", "GET", "/new2"));

        var result = Sync(tf, config);

        Assert.Equal(2, result.Report.OperationsAdded);
        Assert.Equal(1, result.Report.OperationsIdentical);

        var reparsed = _reader.Read(result.TerraformConfig);
        Assert.Equal(3, reparsed.ApiGroups.Single().Operations.Count);
        // New operations come after the original.
        var ids = reparsed.ApiGroups.Single().Operations
            .Select(o => o.OperationId.StructuralText).ToList();
        Assert.Equal("op-a", ids[0]);
    }

    // S3
    [Fact]
    public void Synchronize_OpenApiRemovedOps_PreservedUnchanged()
    {
        var tf = TfGroup("g",
            TfOperation("op-a", "GET", "/a"),
            TfOperation("op-b", "POST", "/b"),
            TfOperation("op-c", "GET", "/c"));

        var config = Config("g", Op("op-a", "GET", "/a"));

        var result = Sync(tf, config);

        Assert.Equal(2, result.Report.OperationsPreserved);
        Assert.Equal(1, result.Report.OperationsIdentical);
        Assert.Equal(tf, result.TerraformConfig); // nothing deleted, nothing changed
    }

    // S4
    [Fact]
    public void Synchronize_MissingDescription_EnrichedFromOpenApi()
    {
        var tf = TfGroup("g", TfOperation("op-a", "GET", "/a", description: ""));
        var config = Config("g", Op("op-a", "GET", "/a", description: "Returns all items"));

        var result = Sync(tf, config);

        Assert.Equal(1, result.Report.OperationsEnriched);
        Assert.Contains("Returns all items", result.TerraformConfig);

        var reparsed = _reader.Read(result.TerraformConfig);
        Assert.Equal("Returns all items",
            reparsed.ApiGroups.Single().Operations.Single().Description?.StructuralText);
    }

    // S5
    [Fact]
    public void Synchronize_ExistingDescription_NotOverwritten()
    {
        var tf = TfGroup("g", TfOperation("op-a", "GET", "/a", description: "Original text"));
        var config = Config("g", Op("op-a", "GET", "/a", description: "New text"));

        var result = Sync(tf, config);

        Assert.Equal(0, result.Report.OperationsEnriched);
        Assert.Contains("Original text", result.TerraformConfig);
        Assert.DoesNotContain("New text", result.TerraformConfig);
    }

    // S6
    [Fact]
    public void Synchronize_NoRequestBlock_CreatedWithParameters()
    {
        var tf = TfGroup("g", TfOperation("op-a", "GET", "/a"));
        var config = Config("g", Op("op-a", "GET", "/a",
            requests:
            [
                new ApimOperationRequest
                {
                    Headers = [new ApimParameter { Name = "Authorization", Required = true }]
                }
            ]));

        var result = Sync(tf, config);

        Assert.Equal(1, result.Report.OperationsEnriched);
        var reparsed = _reader.Read(result.TerraformConfig);
        var op = reparsed.ApiGroups.Single().Operations.Single();
        Assert.NotNull(op.RequestArray);
        Assert.Contains("Authorization", result.TerraformConfig);
    }

    // S7
    [Fact]
    public void Synchronize_ExistingHeader_NewHeaderAppendedExistingUntouched()
    {
        var tf = """
            g = {
              api_operations = [
                {
                  operation_id             = "op-a"
                  apim_resource_group_name = "rg-apim-dev"
                  apim_name                = "apim-company-dev"
                  api_name                 = "my-api-dev"
                  display_name             = "op-a"
                  method                   = "GET"
                  url_template             = "/a"
                  status_code              = "200"
                  description              = ""
                  request = [
                    {
                      header = [
                        {
                          name        = "Authorization"
                          required    = true
                          type        = "string"
                          description = "Bearer token"
                        },
                      ]
                    },
                  ]
                },
              ]
            }
            """;

        var config = Config("g", Op("op-a", "GET", "/a",
            requests:
            [
                new ApimOperationRequest
                {
                    Headers =
                    [
                        new ApimParameter { Name = "Authorization", Required = true },
                        new ApimParameter { Name = "X-Trace", Required = false }
                    ]
                }
            ]));

        var result = Sync(tf, config);

        var reparsed = _reader.Read(result.TerraformConfig);
        var op = reparsed.ApiGroups.Single().Operations.Single();
        var requestObject = (HclObject)op.RequestArray!.Items[0].Value;
        var headers = (HclArray)requestObject.Get("header")!;

        Assert.Equal(2, headers.Items.Count);
        // Existing header untouched (original description text still present).
        Assert.Contains("Bearer token", result.TerraformConfig);
        Assert.Contains("X-Trace", result.TerraformConfig);
    }

    // S8
    [Fact]
    public void Synchronize_DifferentUrlTemplate_PreservedAndReported()
    {
        var tf = TfGroup("g", TfOperation("getUser", "GET", "/users/{id}"));
        var config = Config("g", Op("getUser", "GET", "/v2/users/{id}"));

        var result = Sync(tf, config);

        // Matched by OperationId (urls differ), url_template preserved.
        Assert.Contains("/users/{id}", result.TerraformConfig);
        Assert.DoesNotContain("/v2/users/{id}", result.TerraformConfig);

        var matchedDiff = result.Report.Diffs.Single(d => d.Kind is OperationDiffKind.Identical or OperationDiffKind.Changed);
        Assert.Contains("url_template", matchedDiff.SkippedDueToPolicy);
    }

    // S9
    [Fact]
    public void Synchronize_UrlOverwritePolicy_UrlChanged()
    {
        var tf = TfGroup("g", TfOperation("getUser", "GET", "/users/{id}"));
        var config = Config("g", Op("getUser", "GET", "/v2/users/{id}"));
        var policy = new MergePolicy().WithOverride("url_template", FieldMergePolicy.Overwrite);

        var result = Sync(tf, config, policy);

        Assert.Contains("/v2/users/{id}", result.TerraformConfig);
        var matchedDiff = result.Report.Diffs.Single(d => d.Kind == OperationDiffKind.Changed);
        Assert.Contains("url_template", matchedDiff.AppliedChanges);
    }

    // S10 + S15
    [Fact]
    public void Synchronize_MultipleGroups_UntouchedGroupByteForByte()
    {
        var groupTwo = TfGroup("group-two", TfOperation("other-op", "GET", "/other"));
        var tf = TfGroup("group-one", TfOperation("op-a", "GET", "/a")) + "\n" + groupTwo;

        var config = Config("group-one",
            Op("op-a", "GET", "/a"),
            Op("op-new", "POST", "/new"));

        var result = Sync(tf, config);

        Assert.Equal(1, result.Report.OperationsAdded);
        // The untouched group is present byte-for-byte.
        Assert.Contains(groupTwo, result.TerraformConfig);
    }

    // S11
    [Fact]
    public void Synchronize_MissingApiGroup_NewGroupCreatedUnderParentPath()
    {
        var tf = """
            apis = {
              backend_apis = {
                existing-group = {
                  api_operations = [
                    {
                      operation_id = "x"
                      method       = "GET"
                      url_template = "x"
                      apim_resource_group_name = "other-rg"
                      api_name     = "other-api"
                    },
                  ]
                }
              }
            }
            """;

        var config = Config("brand-new-group", Op("op-a", "GET", "/a"));
        // Two groups exist check: existing-group doesn't match name nor (rg, api).
        var result = Sync(tf, config);

        Assert.Equal(1, result.Report.OperationsAdded);

        var reparsed = _reader.Read(result.TerraformConfig);
        Assert.Equal(2, reparsed.ApiGroups.Count);
        Assert.Contains(reparsed.ApiGroups, g => g.ApiGroupName == "brand-new-group");
        Assert.Equal(["apis", "backend_apis"], reparsed.ApiGroupParentPath);
    }

    // S12
    [Fact]
    public void Synchronize_AmbiguousMatch_WarningAndNothingAdded()
    {
        var tf = TfGroup("g",
            TfOperation("op-one", "GET", "/users"),
            TfOperation("op-two", "GET", "/users"));

        var config = Config("g", Op("op-three", "GET", "/users"));

        var result = Sync(tf, config);

        Assert.Equal(0, result.Report.OperationsAdded);
        Assert.Contains(result.Report.Warnings, w => w.Kind == SyncWarningKind.AmbiguousMatch);
        Assert.Equal(tf, result.TerraformConfig);
    }

    // -----------------------------------------------------------------
    // S13–S17 (REVISION 1)
    // -----------------------------------------------------------------

    // S13
    [Fact]
    public void Synchronize_TemplatedFile_NewOperationInSameStyle()
    {
        var config = Config("${api_group_name}", Op("createUser", "POST", "/users"));

        var result = Sync(LoadFixture(), config);

        Assert.Equal(1, result.Report.OperationsAdded);

        var reparsed = _reader.Read(result.TerraformConfig);
        var ops = reparsed.ApiGroups.Single().Operations;
        Assert.Equal(2, ops.Count);

        var newOp = ops.Single(o => o.Method.StructuralText == "POST");
        Assert.Equal("${operation_prefix}-${env}", newOp.OperationId.StructuralText);
        Assert.Equal("${stage_group_name}", newOp.ApimResourceGroupName?.StructuralText);
        Assert.Equal("${api_name}-${env}", newOp.ApiName?.StructuralText);
        // Routing fields stay literal.
        Assert.Equal("/users", newOp.UrlTemplate.StructuralText);
    }

    // S14
    [Fact]
    public void Synchronize_LiteralFile_NewOperationAlsoLiteral()
    {
        var tf = TfGroup("g", TfOperation("get-users-dev", "GET", "/users"));
        var config = Config("g", Op("get-users-dev", "GET", "/users"), Op("createUser", "POST", "/users"));

        var result = Sync(tf, config);

        Assert.Equal(1, result.Report.OperationsAdded);

        var reparsed = _reader.Read(result.TerraformConfig);
        var newOp = reparsed.ApiGroups.Single().Operations.Single(o => o.Method.StructuralText == "POST");
        Assert.Equal("createUser", newOp.OperationId.StructuralText);
        Assert.False(newOp.OperationId.IsInterpolated);
    }

    // S16
    [Fact]
    public void Synchronize_InsertedOperations_HaveLeadingComments()
    {
        var config = Config("${api_group_name}", Op("createUser", "POST", "/users", displayName: "Create user"));

        var result = Sync(LoadFixture(), config);

        Assert.Contains("# POST /users | op_id:", result.TerraformConfig);
        Assert.Contains("source: OpenApi", result.TerraformConfig);

        var reparsed = _parser.Parse(result.TerraformConfig);
        // Find the new operation's array item and check its leading comments.
        var apis = (HclObject)reparsed.RootAssignments.Single().Value;
        var bpc = (HclObject)apis.Get("bpc_apis")!;
        var backend = (HclObject)bpc.Get("backend_apis")!;
        var group = (HclObject)backend.Assignments.Single().Value;
        var opsArray = (HclArray)group.Get("api_operations")!;

        var newItem = opsArray.Items.Last();
        Assert.InRange(newItem.LeadingComments.Count, 2, 3);
    }

    // S17
    [Fact]
    public void Synchronize_PlaceholdersAdded_ReplaceBeforeApplyHeaderInserted()
    {
        var config = Config("${api_group_name}", Op("createUser", "POST", "/users"));

        var result = Sync(LoadFixture(), config);

        Assert.Contains("REPLACE BEFORE APPLY", result.TerraformConfig);
        Assert.Contains("${operation_prefix}", result.TerraformConfig);
    }

    [Fact]
    public void Synchronize_HeaderNotDuplicatedOnSecondSync()
    {
        var config1 = Config("${api_group_name}", Op("createUser", "POST", "/users"));
        var first = Sync(LoadFixture(), config1);

        var config2 = Config("${api_group_name}", Op("deleteUser", "DELETE", "/users/{id}"));
        var second = Sync(first.TerraformConfig, config2);

        var headerCount = second.TerraformConfig.Split("REPLACE BEFORE APPLY").Length - 1;
        Assert.Equal(1, headerCount);
    }

    [Fact]
    public void Synchronize_DisabledComments_NoCommentsAdded()
    {
        var tf = TfGroup("g", TfOperation("op-a", "GET", "/a"));
        var config = Config("g", Op("op-a", "GET", "/a"), Op("op-new", "POST", "/new"));

        var result = Sync(tf, config, options: new SyncOptions
        {
            AddOperationComments = false,
            AddReplaceBeforeApplyHeader = false
        });

        Assert.DoesNotContain("op_id:", result.TerraformConfig);
        Assert.DoesNotContain("REPLACE BEFORE APPLY", result.TerraformConfig);
    }

    [Fact]
    public void Synchronize_AppendOnlyDefaults_NeverModifiesPreserveFields()
    {
        var tf = TfGroup("g", TfOperation("op-a", "GET", "/a"));
        var config = Config("g", Op("DIFFERENT-ID", "GET", "/a", description: "x"));

        var result = Sync(tf, config);

        // operation_id is Preserve — must keep the original.
        Assert.Contains("\"op-a\"", result.TerraformConfig);
        Assert.DoesNotContain("\"DIFFERENT-ID\"", result.TerraformConfig);
    }

    [Fact]
    public void Synchronize_InterpolatedOperationIds_ProduceWarnings()
    {
        var config = Config("${api_group_name}", Op("getUser", "GET", "/users/{id}"));

        var result = Sync(LoadFixture(), config);

        Assert.Contains(result.Report.Warnings,
            w => w.Kind == SyncWarningKind.OperationIdContainsInterpolation);
        Assert.Contains(result.Report.Warnings,
            w => w.Kind == SyncWarningKind.UrlTemplateContainsInterpolation);
    }

    // -----------------------------------------------------------------
    // The mandatory real-example acceptance test (§6 Phase 7)
    // -----------------------------------------------------------------

    [Fact]
    public void Synchronize_RealUserExample_AddsNewOperationOnly()
    {
        var source = LoadFixture();
        var config = Config("${api_group_name}", Op("getHealth", "GET", "/health", displayName: "Health check"));

        var result = Sync(source, config);

        // (a) output HCL is valid
        var reparsed = _parser.Parse(result.TerraformConfig);
        Assert.NotNull(reparsed);

        // (b) parses back through the reader
        var reread = _reader.Read(reparsed);
        var group = Assert.Single(reread.ApiGroups);

        // (c) exactly one new entry in api_operations
        Assert.Equal(2, group.Operations.Count);
        Assert.Equal(1, result.Report.OperationsAdded);
        Assert.Equal(1, result.Report.OperationsPreserved);

        // (d) all original lines are present byte-for-byte
        foreach (var line in source.Split('\n').Where(l => l.Trim().Length > 0))
            Assert.Contains(line.TrimEnd('\r'), result.TerraformConfig);
    }
}
