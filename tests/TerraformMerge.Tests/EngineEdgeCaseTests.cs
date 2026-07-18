using TerraformMerge.Engine;

namespace TerraformMerge.Tests;

/// <summary>
/// Edge cases: multiple api groups, interpolated fields, ambiguous duplicates,
/// and target-group selection during a rewrite.
/// </summary>
public class EngineEdgeCaseTests
{
    private readonly OperationSourceLoader _loader = new();
    private readonly OriginalWriter _writer = new();

    private const string TwoGroups = """
        group-a = {
          api_operations = [
            {
              operation_id             = "a1-dev"
              apim_resource_group_name = "rg"
              api_name                 = "api-a"
              method                   = "GET"
              url_template             = "a"
            },
          ]
        }
        group-b = {
          api_operations = [
            {
              operation_id             = "b1-dev"
              apim_resource_group_name = "rg"
              api_name                 = "api-b"
              method                   = "GET"
              url_template             = "b"
            },
          ]
        }
        """;

    [Fact]
    public void Load_MultiGroup_TracksGroupPerOperation()
    {
        var loaded = _loader.LoadTerraform(TwoGroups, OperationSource.OriginalTerraform);

        Assert.Equal(2, loaded.Nodes.Count);
        Assert.Equal("group-a", loaded.Nodes.Single(n => n.OperationId == "a1-dev").ApiGroupName);
        Assert.Equal("group-b", loaded.Nodes.Single(n => n.OperationId == "b1-dev").ApiGroupName);
    }

    [Fact]
    public void Rewrite_MultiGroup_TargetsRetainedGroup_LeavesOtherGroupVerbatim()
    {
        var loaded = _loader.LoadTerraform(TwoGroups, OperationSource.OriginalTerraform);
        var a1 = loaded.Nodes.Single(n => n.OperationId == "a1-dev");

        var newOp = new OperationNode
        {
            Source = OperationSource.TargetOpenApi,
            OperationId = "a2",
            Method = "POST",
            UrlTemplate = "a"
        };

        // Retain a1 (group-a) and add a generated op → only group-a changes.
        var text = _writer.Rewrite(loaded, [a1, newOp]);

        // group-b is emitted byte-for-byte (it is the trailing block).
        var groupBTail = TwoGroups[TwoGroups.IndexOf("group-b = {", StringComparison.Ordinal)..].TrimEnd();
        Assert.EndsWith(groupBTail, text.TrimEnd());

        var reparsed = _loader.LoadTerraform(text, OperationSource.OriginalTerraform);
        Assert.Equal(2, reparsed.Nodes.Count(n => n.ApiGroupName == "group-a"));
        Assert.Equal(1, reparsed.Nodes.Count(n => n.ApiGroupName == "group-b"));
    }

    [Fact]
    public void Rewrite_TargetsGroupOfRetainedOriginals_NotAlwaysFirst()
    {
        var loaded = _loader.LoadTerraform(TwoGroups, OperationSource.OriginalTerraform);
        var b1 = loaded.Nodes.Single(n => n.OperationId == "b1-dev");

        var newOp = new OperationNode { Source = OperationSource.TargetTerraform, OperationId = "b2", Method = "POST", UrlTemplate = "b" };

        var text = _writer.Rewrite(loaded, [b1, newOp]);

        var reparsed = _loader.LoadTerraform(text, OperationSource.OriginalTerraform);
        Assert.Equal(2, reparsed.Nodes.Count(n => n.ApiGroupName == "group-b"));
        Assert.Equal(1, reparsed.Nodes.Count(n => n.ApiGroupName == "group-a"));
    }

    [Fact]
    public void Load_InterpolatedFields_PreservedStructurally()
    {
        const string interpolated = """
            g = {
              api_operations = [
                {
                  operation_id             = "${operation_prefix}-${env}"
                  apim_resource_group_name = "rg"
                  api_name                 = "api"
                  method                   = "GET"
                  url_template             = "${operation_path}"
                },
              ]
            }
            """;

        var node = _loader.LoadTerraform(interpolated, OperationSource.OriginalTerraform).Nodes.Single();

        Assert.Equal("${operation_path}", node.UrlTemplate);
        Assert.Equal("${operation_prefix}-${env}", node.OperationId);

        // Two structurally identical interpolated ops are a perfect match.
        var copy = TestOps.Op("GET", "${operation_path}", "${operation_prefix}-${env}");
        Assert.Equal(1.0, SimilarityScorer.Similarity(node, copy), 6);
    }

    [Fact]
    public void Align_DuplicateOriginals_OneMatches_OtherStaysUnmatched()
    {
        var left = new[]
        {
            TestOps.Op("GET", "orders", "dup-1-dev", OperationSource.OriginalTerraform),
            TestOps.Op("GET", "orders", "dup-2-dev", OperationSource.OriginalTerraform)
        };
        var right = new[] { TestOps.Op("GET", "orders", "listOrders") };

        var result = BlockAligner.Align(left, right);

        Assert.Single(result.Matched);
        Assert.Single(result.UnmatchedLeft);
        Assert.Empty(result.UnmatchedRight);
    }

    [Fact]
    public void Align_SameUrlDifferentMethods_DoNotCrossMatch()
    {
        var left = new[]
        {
            TestOps.Op("GET", "users", "list-dev", OperationSource.OriginalTerraform),
            TestOps.Op("POST", "users", "create-dev", OperationSource.OriginalTerraform)
        };
        var right = new[]
        {
            TestOps.Op("POST", "users", "createUser"),
            TestOps.Op("GET", "users", "listUsers")
        };

        var result = BlockAligner.Align(left, right);

        Assert.Equal(2, result.Matched.Count);
        // GET pairs with GET, POST with POST — never crossed.
        Assert.All(result.Matched, m =>
            Assert.Equal(left[m.LeftIndex].Method, right[m.RightIndex].Method));
    }
}
