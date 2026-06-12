using TerraformApi.Application.Services.Hcl;
using TerraformApi.Domain.Interfaces;
using TerraformApi.Domain.Models.Hcl;

namespace TerraformApi.Application.Tests.Hcl;

/// <summary>
/// Critical round-trip tests (W1 + plan §6 Phase 1 acceptance gate):
/// parse example-existing.tf → write → parse again → ASTs are equal.
/// These tests block all later phases.
/// </summary>
public class HclRoundTripTests
{
    private readonly HclParserService _parser = new();
    private readonly HclWriterService _writer = new();

    private static string LoadFixture() =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "example-existing.tf"));

    [Fact]
    public void RoundTripPreservesExistingExample_PreserveMode_ByteForByte()
    {
        var source = LoadFixture();
        var doc = _parser.Parse(source);
        var output = _writer.Write(doc); // PreserveOriginalFormatting = true (default)
        Assert.Equal(source, output);
    }

    [Fact]
    public void RoundTripPreservesExistingExample_CanonicalMode_AstEqual()
    {
        var source = LoadFixture();
        var first = _parser.Parse(source);

        var written = _writer.Write(first, new HclWriteOptions { PreserveOriginalFormatting = false });
        var second = _parser.Parse(written);

        AstAssert.Equal(first, second);
    }

    [Fact]
    public void Fixture_ParsesToExpectedStructure()
    {
        var doc = _parser.Parse(LoadFixture());

        var apis = Assert.IsType<HclObject>(doc.RootAssignments.Single().Value);
        var bpc = Assert.IsType<HclObject>(apis.Get("bpc_apis"));
        var backend = Assert.IsType<HclObject>(bpc.Get("backend_apis"));
        var groupAssignment = backend.Assignments.Single();
        Assert.Equal("${api_group_name}", groupAssignment.Key);
        Assert.True(groupAssignment.KeyIsQuoted);

        var group = Assert.IsType<HclObject>(groupAssignment.Value);
        var apiArray = Assert.IsType<HclArray>(group.Get("api"));
        var opsArray = Assert.IsType<HclArray>(group.Get("api_operations"));

        Assert.Single(apiArray.Items);
        Assert.Single(opsArray.Items);

        // The API block contains the CORS policy heredoc.
        var api = Assert.IsType<HclObject>(apiArray.Items[0].Value);
        var policy = Assert.IsType<HclHeredoc>(api.Get("policy"));
        Assert.Contains("<cors", policy.Content);
        Assert.Contains("${frontend_host}", policy.Content);

        // The operation is fully templated.
        var op = Assert.IsType<HclObject>(opsArray.Items[0].Value);
        var opId = Assert.IsType<HclInterpolation>(op.Get("operation_id"));
        Assert.Equal("${operation_prefix}-${env}", opId.InnerText);
        var urlTemplate = Assert.IsType<HclInterpolation>(op.Get("url_template"));
        Assert.Equal("${operation_path}", urlTemplate.InnerText);
        var method = Assert.IsType<HclLiteral>(op.Get("method"));
        Assert.Equal("GET", method.RawValue);
    }

    [Fact]
    public void RoundTrip_ModifiedFixture_OnlyTouchedPartChanges()
    {
        var source = LoadFixture();
        var doc = _parser.Parse(source);

        // Navigate to api_operations and append one new operation.
        var apis = (HclObject)doc.RootAssignments.Single().Value;
        var bpc = (HclObject)apis.Get("bpc_apis")!;
        var backend = (HclObject)bpc.Get("backend_apis")!;
        var group = (HclObject)backend.Assignments.Single().Value;
        var operations = (HclArray)group.Get("api_operations")!;

        operations.Items.Add(new HclArrayItem
        {
            LeadingComments =
            [
                new HclComment { Kind = HclCommentKind.LineHash, Text = " POST /users | op_id: createUser", IsLeading = true }
            ],
            Value = new HclObject
            {
                Items =
                [
                    new HclAssignment { Key = "operation_id", Value = new HclInterpolation { InnerText = "${operation_prefix}-create-${env}", ReferencedExpressions = ["operation_prefix", "env"] } },
                    new HclAssignment { Key = "method", Value = new HclLiteral { RawValue = "POST", Kind = HclLiteralKind.String } },
                    new HclAssignment { Key = "url_template", Value = new HclLiteral { RawValue = "/users", Kind = HclLiteralKind.String } }
                ]
            }
        });

        var output = _writer.Write(doc);

        // All original lines for the api block must still be present verbatim.
        Assert.Contains("service_url                      = \"https://${api_gateway_host}/${api_version}/${backend_service_path}/\"", output);
        Assert.Contains("operation_id             = \"${operation_prefix}-${env}\"", output);
        // New operation and its comment are present.
        Assert.Contains("# POST /users | op_id: createUser", output);
        Assert.Contains("${operation_prefix}-create-${env}", output);

        // Output is valid HCL with 2 operations now.
        var reparsed = _parser.Parse(output);
        var reGroup = (HclObject)((HclObject)((HclObject)((HclObject)reparsed.RootAssignments.Single().Value).Get("bpc_apis")!).Get("backend_apis")!).Assignments.Single().Value;
        var reOps = (HclArray)reGroup.Get("api_operations")!;
        Assert.Equal(2, reOps.Items.Count);
    }
}
