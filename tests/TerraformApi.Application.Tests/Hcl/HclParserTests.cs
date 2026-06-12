using TerraformApi.Application.Services.Hcl;
using TerraformApi.Domain.Interfaces;
using TerraformApi.Domain.Models.Hcl;

namespace TerraformApi.Application.Tests.Hcl;

/// <summary>
/// Parser tests P1–P17 plus comment tests C1–C4 from the implementation plan (§5.1, §REV-1.7).
/// </summary>
public class HclParserTests
{
    private readonly HclParserService _parser = new();

    // P1
    [Fact]
    public void Parse_EmptyDocument_ReturnsEmptyRootAssignments()
    {
        var doc = _parser.Parse("");
        Assert.Empty(doc.RootAssignments);
    }

    // P2
    [Fact]
    public void Parse_CommentsOnly_ReturnsNoAssignments()
    {
        var doc = _parser.Parse("# just a comment\n// another\n");
        Assert.Empty(doc.RootAssignments);
        Assert.Equal(2, doc.RootItems.OfType<HclComment>().Count());
    }

    // P3
    [Fact]
    public void Parse_SimpleAssignment_StringLiteral()
    {
        var doc = _parser.Parse("a = \"b\"");
        var assignment = Assert.Single(doc.RootAssignments);
        Assert.Equal("a", assignment.Key);
        var literal = Assert.IsType<HclLiteral>(assignment.Value);
        Assert.Equal(HclLiteralKind.String, literal.Kind);
        Assert.Equal("b", literal.RawValue);
    }

    // P4
    [Fact]
    public void Parse_PureInterpolation_ReturnsInterpolationNode()
    {
        var doc = _parser.Parse("a = \"${x}\"");
        var interp = Assert.IsType<HclInterpolation>(doc.RootAssignments.Single().Value);
        Assert.Equal("${x}", interp.InnerText);
        Assert.Equal(["x"], interp.ReferencedExpressions);
    }

    // P5
    [Fact]
    public void Parse_InterpolationWithPrefixSuffix_PreservesFullText()
    {
        var doc = _parser.Parse("a = \"prefix-${x}-suffix\"");
        var interp = Assert.IsType<HclInterpolation>(doc.RootAssignments.Single().Value);
        Assert.Equal("prefix-${x}-suffix", interp.InnerText);
        Assert.Equal(["x"], interp.ReferencedExpressions);
    }

    // P6
    [Fact]
    public void Parse_MultipleInterpolations_AllReferencesExtracted()
    {
        var doc = _parser.Parse("a = \"${x}-${y}\"");
        var interp = Assert.IsType<HclInterpolation>(doc.RootAssignments.Single().Value);
        Assert.Equal(["x", "y"], interp.ReferencedExpressions);
    }

    // P7
    [Fact]
    public void Parse_Heredoc_CapturesMarkerContentNotIndented()
    {
        var doc = _parser.Parse("a = <<XML\n<foo/>\nXML\n");
        var heredoc = Assert.IsType<HclHeredoc>(doc.RootAssignments.Single().Value);
        Assert.Equal("XML", heredoc.Marker);
        Assert.Equal("<foo/>", heredoc.Content);
        Assert.False(heredoc.Indented);
    }

    // P8
    [Fact]
    public void Parse_IndentedHeredoc_TrimsMinimumIndent()
    {
        var doc = _parser.Parse("a = <<-XML\n    <foo/>\n  XML\n");
        var heredoc = Assert.IsType<HclHeredoc>(doc.RootAssignments.Single().Value);
        Assert.True(heredoc.Indented);
        Assert.Equal("<foo/>", heredoc.Content);
    }

    // P9
    [Fact]
    public void Parse_ArrayOfObjectsWithTrailingComma_ParsesCorrectly()
    {
        var doc = _parser.Parse("""
            items = [
              {
                name = "first"
              },
              {
                name = "second"
              },
            ]
            """);
        var array = Assert.IsType<HclArray>(doc.RootAssignments.Single().Value);
        Assert.Equal(2, array.Items.Count);
        Assert.All(array.Items, i => Assert.IsType<HclObject>(i.Value));
    }

    // P10
    [Fact]
    public void Parse_DeeplyNestedStructure_CorrectAst()
    {
        var doc = _parser.Parse("""
            apis = {
              bpc_apis = {
                backend_apis = {
                  "${api_group_name}" = {
                    product = []
                  }
                }
              }
            }
            """);

        var apis = Assert.IsType<HclObject>(doc.RootAssignments.Single().Value);
        var bpc = Assert.IsType<HclObject>(apis.Get("bpc_apis"));
        var backend = Assert.IsType<HclObject>(bpc.Get("backend_apis"));
        var groupAssignment = backend.Assignments.Single();
        Assert.Equal("${api_group_name}", groupAssignment.Key);
        Assert.True(groupAssignment.KeyIsQuoted);
        var group = Assert.IsType<HclObject>(groupAssignment.Value);
        Assert.IsType<HclArray>(group.Get("product"));
    }

    // P11
    [Fact]
    public void Parse_QuotedInterpolatedKey_KeyIsQuotedTrue()
    {
        var doc = _parser.Parse("\"${api_group_name}\" = {}");
        var assignment = doc.RootAssignments.Single();
        Assert.Equal("${api_group_name}", assignment.Key);
        Assert.True(assignment.KeyIsQuoted);
    }

    // P12
    [Fact]
    public void Parse_UnbalancedBraces_ThrowsWithPosition()
    {
        var ex = Assert.Throws<HclParseException>(() => _parser.Parse("a = {\n  b = 1\n"));
        Assert.True(ex.Line >= 1);
        Assert.True(ex.Column >= 1);
    }

    // P13
    [Fact]
    public void Parse_Numbers_IntAndDecimal()
    {
        var doc = _parser.Parse("port = 8080\nratio = 0.5");
        var assignments = doc.RootAssignments.ToList();
        var port = Assert.IsType<HclLiteral>(assignments[0].Value);
        Assert.Equal(HclLiteralKind.Number, port.Kind);
        Assert.Equal("8080", port.RawValue);
        var ratio = Assert.IsType<HclLiteral>(assignments[1].Value);
        Assert.Equal("0.5", ratio.RawValue);
    }

    // P14
    [Fact]
    public void Parse_BoolAndNull_CorrectKinds()
    {
        var doc = _parser.Parse("flag = true\nvalue = null");
        var assignments = doc.RootAssignments.ToList();
        var flag = Assert.IsType<HclLiteral>(assignments[0].Value);
        Assert.Equal(HclLiteralKind.Bool, flag.Kind);
        var nullValue = Assert.IsType<HclLiteral>(assignments[1].Value);
        Assert.Equal(HclLiteralKind.Null, nullValue.Kind);
    }

    // P15
    [Fact]
    public void Parse_EscapedString_RawValuePreserved()
    {
        var doc = _parser.Parse("a = \"say \\\" hi \\\"\"");
        var literal = Assert.IsType<HclLiteral>(doc.RootAssignments.Single().Value);
        Assert.Equal("say \\\" hi \\\"", literal.RawValue);
    }

    // P16
    [Fact]
    public void Parse_XmlInsideHeredoc_NotInterpretedAsHcl()
    {
        var doc = _parser.Parse("""
            policy = <<XML
            <policies>
              <method>GET</method>
              <origin allow="true">{x = 1}</origin>
            </policies>
            XML
            after = "ok"
            """);

        var assignments = doc.RootAssignments.ToList();
        Assert.Equal(2, assignments.Count);
        var heredoc = Assert.IsType<HclHeredoc>(assignments[0].Value);
        Assert.Contains("<method>GET</method>", heredoc.Content);
        Assert.Equal("after", assignments[1].Key);
    }

    // P17
    [Fact]
    public void Parse_MultiByteUtf8_WorksCorrectly()
    {
        var doc = _parser.Parse("name = \"Сервис 日本語 ✓\"");
        var literal = Assert.IsType<HclLiteral>(doc.RootAssignments.Single().Value);
        Assert.Equal("Сервис 日本語 ✓", literal.RawValue);
    }

    // C1
    [Fact]
    public void Parse_CommentBeforeAssignmentInObject_BothItemsPreserved()
    {
        var doc = _parser.Parse("obj = {\n# foo\na = 1\n}");
        var obj = Assert.IsType<HclObject>(doc.RootAssignments.Single().Value);
        Assert.Equal(2, obj.Items.Count);
        var comment = Assert.IsType<HclComment>(obj.Items[0]);
        Assert.Equal(" foo", comment.Text);
        var assignment = Assert.IsType<HclAssignment>(obj.Items[1]);
        Assert.Equal("a", assignment.Key);
    }

    // C3
    [Fact]
    public void Parse_CommentsBeforeArrayElements_AttachedAsLeading()
    {
        var doc = _parser.Parse("""
            ops = [
              # GET /users | op_id: getUsers
              # source: OpenAPI
              {
                method = "GET"
              },
            ]
            """);
        var array = Assert.IsType<HclArray>(doc.RootAssignments.Single().Value);
        var item = Assert.Single(array.Items);
        Assert.Equal(2, item.LeadingComments.Count);
        Assert.Contains("GET /users", item.LeadingComments[0].Text);
    }

    // C4
    [Fact]
    public void Parse_MultiLineBlockComment_SingleCommentNode()
    {
        var doc = _parser.Parse("/* line one\nline two */\na = 1");
        var comment = Assert.IsType<HclComment>(doc.RootItems[0]);
        Assert.Equal(HclCommentKind.Block, comment.Kind);
        Assert.Contains("line one", comment.Text);
        Assert.Contains("line two", comment.Text);
    }

    [Fact]
    public void Parse_BareExpression_BecomesBareInterpolation()
    {
        var doc = _parser.Parse("protocols = var.protocols");
        var interp = Assert.IsType<HclInterpolation>(doc.RootAssignments.Single().Value);
        Assert.True(interp.Bare);
        Assert.Equal("var.protocols", interp.InnerText);
    }

    [Fact]
    public void TryParse_InvalidInput_ReturnsDiagnosticsNotThrow()
    {
        var result = _parser.TryParse("a = {");
        Assert.False(result.IsSuccess);
        Assert.Contains(result.Diagnostics, d => d.Severity == HclDiagnosticSeverity.Error);
    }

    [Fact]
    public void TryParse_ValidInput_IsSuccess()
    {
        var result = _parser.TryParse("a = 1");
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Document);
    }

    [Fact]
    public void Parse_InlineScalarArray_ParsesItems()
    {
        var doc = _parser.Parse("protocols = [\"https\"]");
        var array = Assert.IsType<HclArray>(doc.RootAssignments.Single().Value);
        var item = Assert.Single(array.Items);
        var literal = Assert.IsType<HclLiteral>(item.Value);
        Assert.Equal("https", literal.RawValue);
    }

    [Fact]
    public void Parse_NodesCarrySourceSpans()
    {
        var src = "a = \"value\"";
        var doc = _parser.Parse(src);
        var assignment = doc.RootAssignments.Single();
        Assert.True(assignment.HasSourceSpan);
        Assert.Equal("a = \"value\"", src[assignment.StartOffset..assignment.EndOffset]);
    }
}
