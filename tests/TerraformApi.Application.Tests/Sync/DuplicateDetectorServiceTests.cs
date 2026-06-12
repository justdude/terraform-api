using TerraformApi.Application.Services.Apim;
using TerraformApi.Application.Services.Hcl;
using TerraformApi.Application.Services.Sync;
using TerraformApi.Domain.Models.Sync;

namespace TerraformApi.Application.Tests.Sync;

/// <summary>Duplicate detector tests D1–D5 (§5.5) plus the clean-fixture check.</summary>
public class DuplicateDetectorServiceTests
{
    private readonly ApimTerraformReaderService _reader = new(new HclParserService());
    private readonly DuplicateDetectorService _detector = new();
    private readonly OperationMatchStrategy _strategy = new();

    private static string LoadFixture() =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "example-existing.tf"));

    private static string Operation(string opId, string method, string url, string apiName = "my-api") => $$"""
        {
          operation_id             = "{{opId}}"
          apim_resource_group_name = "rg"
          apim_name                = "apim"
          api_name                 = "{{apiName}}"
          display_name             = "{{opId}}"
          method                   = "{{method}}"
          url_template             = "{{url}}"
        },
        """;

    private static string Group(params string[] operations) => $$"""
        my-group = {
          api_operations = [
        {{string.Join("\n", operations)}}
          ]
        }
        """;

    // D1
    [Fact]
    public void Detect_SameOperationIdInOneGroup_HardDuplicate()
    {
        var parsed = _reader.Read(Group(
            Operation("dup-id", "GET", "/a"),
            Operation("dup-id", "POST", "/b")));

        var groups = _detector.Detect(parsed, _strategy);

        var byId = Assert.Single(groups, g => g.MatchedBy == OperationMatchKey.OperationId);
        Assert.Equal(2, byId.Members.Count);
        Assert.All(byId.Members, m => Assert.Equal(DuplicateSeverity.HardDuplicate, m.Severity));
    }

    // D2
    [Fact]
    public void Detect_DifferentIdsSameMethodUrlSameApi_LogicalDuplicate()
    {
        var parsed = _reader.Read(Group(
            Operation("op-one", "GET", "/users"),
            Operation("op-two", "GET", "/users")));

        var groups = _detector.Detect(parsed, _strategy);

        var byUrl = Assert.Single(groups, g => g.MatchedBy == OperationMatchKey.MethodAndUrl);
        Assert.All(byUrl.Members, m => Assert.Equal(DuplicateSeverity.LogicalDuplicate, m.Severity));
    }

    // D3
    [Fact]
    public void Detect_SameMethodUrlDifferentApis_CrossApiSimilarity()
    {
        var parsed = _reader.Read(Group(
            Operation("op-one", "GET", "/users", apiName: "api-one"),
            Operation("op-two", "GET", "/users", apiName: "api-two")));

        var groups = _detector.Detect(parsed, _strategy);

        var byUrl = Assert.Single(groups, g => g.MatchedBy == OperationMatchKey.MethodAndUrl);
        Assert.All(byUrl.Members, m => Assert.Equal(DuplicateSeverity.CrossApiSimilarity, m.Severity));
    }

    // D4
    [Fact]
    public void Detect_AllUnique_NoDuplicates()
    {
        var parsed = _reader.Read(Group(
            Operation("op-one", "GET", "/users"),
            Operation("op-two", "POST", "/users"),
            Operation("op-three", "GET", "/items")));

        var groups = _detector.Detect(parsed, _strategy);

        Assert.Empty(groups);
    }

    // D5
    [Fact]
    public void Detect_InterpolatedDuplicates_ComparedStructurally()
    {
        var parsed = _reader.Read(Group(
            Operation("${prefix}-list-${env}", "GET", "/a"),
            Operation("${prefix}-list-${env}", "POST", "/b")));

        var groups = _detector.Detect(parsed, _strategy);

        var byId = Assert.Single(groups, g => g.MatchedBy == OperationMatchKey.OperationId);
        Assert.Equal("${prefix}-list-${env}", byId.MatchedValue);
        Assert.Equal(2, byId.Members.Count);
    }

    [Fact]
    public void Detect_ExampleFixture_NoDuplicates()
    {
        var parsed = _reader.Read(LoadFixture());
        var groups = _detector.Detect(parsed, _strategy);
        Assert.Empty(groups);
    }

    [Fact]
    public void Detect_MembersCarryLineNumbers()
    {
        var parsed = _reader.Read(Group(
            Operation("dup", "GET", "/a"),
            Operation("dup", "POST", "/b")));

        var groups = _detector.Detect(parsed, _strategy);
        var members = groups.Single(g => g.MatchedBy == OperationMatchKey.OperationId).Members;

        Assert.All(members, m => Assert.True(m.LineInSource > 0));
        Assert.NotEqual(members[0].LineInSource, members[1].LineInSource);
    }
}
