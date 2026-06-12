using TerraformApi.Application.Services.Hcl;
using TerraformApi.Domain.Interfaces;
using TerraformApi.Domain.Models.Hcl;

namespace TerraformApi.Application.Tests.Hcl;

/// <summary>
/// Writer tests W1–W5 from the plan (§5.2) plus canonical-rendering checks.
/// </summary>
public class HclWriterTests
{
    private readonly HclParserService _parser = new();
    private readonly HclWriterService _writer = new();

    private static readonly HclWriteOptions Canonical = new() { PreserveOriginalFormatting = false };

    // W2
    [Fact]
    public void Write_HeredocContent_PreservedByteForByte()
    {
        var src = "policy = <<XML\n<policies>\n  <inbound />\n</policies>\nXML\n";
        var doc = _parser.Parse(src);
        var output = _writer.Write(doc, Canonical);

        Assert.Contains("<policies>\n  <inbound />\n</policies>", output);
        Assert.Contains("<<XML", output);

        // Round-trip: the heredoc content survives a second parse.
        var reparsed = _parser.Parse(output);
        var heredoc = Assert.IsType<HclHeredoc>(reparsed.RootAssignments.Single().Value);
        Assert.Equal("<policies>\n  <inbound />\n</policies>", heredoc.Content);
    }

    // W3
    [Fact]
    public void Write_Interpolations_PreservedVerbatim()
    {
        var doc = _parser.Parse("name = \"${a}-${b}\"");
        var output = _writer.Write(doc, Canonical);
        Assert.Contains("\"${a}-${b}\"", output);
    }

    // W4
    [Fact]
    public void Write_NewObject_AlignsEqualsForLongKeys()
    {
        var obj = new HclObject
        {
            Items =
            [
                new HclAssignment { Key = "apim_resource_group_name", Value = new HclLiteral { RawValue = "rg", Kind = HclLiteralKind.String } },
                new HclAssignment { Key = "name", Value = new HclLiteral { RawValue = "x", Kind = HclLiteralKind.String } }
            ]
        };
        var doc = new HclDocument
        {
            RootItems = [new HclAssignment { Key = "api", Value = obj }]
        };

        var output = _writer.Write(doc, Canonical);

        // Both '=' should be in the same column: short key padded to longest key length.
        Assert.Contains("apim_resource_group_name = \"rg\"", output);
        Assert.Contains("name                     = \"x\"", output);
    }

    // W5
    [Fact]
    public void Write_MultiLineArray_EmitsTrailingCommas()
    {
        var doc = _parser.Parse("""
            items = [
              {
                a = 1
              },
            ]
            """);
        var output = _writer.Write(doc, Canonical);
        Assert.Contains("},", output);
    }

    [Fact]
    public void Write_EmptyArray_Inline()
    {
        var doc = _parser.Parse("product = []");
        var output = _writer.Write(doc, Canonical);
        Assert.Contains("product = []", output);
    }

    [Fact]
    public void Write_ScalarArray_RenderedInline()
    {
        var doc = _parser.Parse("protocols = [\"https\"]");
        var output = _writer.Write(doc, Canonical);
        Assert.Contains("protocols = [\"https\"]", output);
    }

    [Fact]
    public void Write_QuotedKey_KeepsQuotes()
    {
        var doc = _parser.Parse("\"${api_group_name}\" = {}");
        var output = _writer.Write(doc, Canonical);
        Assert.StartsWith("\"${api_group_name}\" =", output.TrimStart());
    }

    [Fact]
    public void Write_Comments_EmittedWithOriginalSyntax()
    {
        var doc = _parser.Parse("# hash comment\n// slash comment\na = 1");
        var output = _writer.Write(doc, Canonical);
        Assert.Contains("# hash comment", output);
        Assert.Contains("// slash comment", output);
    }

    [Fact]
    public void Write_ArrayItemLeadingComments_BeforeElement()
    {
        var doc = _parser.Parse("""
            ops = [
              # GET /users | op_id: getUsers
              {
                method = "GET"
              },
            ]
            """);
        var output = _writer.Write(doc, Canonical);
        var commentIndex = output.IndexOf("# GET /users", StringComparison.Ordinal);
        var braceIndex = output.IndexOf("method", StringComparison.Ordinal);
        Assert.True(commentIndex >= 0 && commentIndex < braceIndex,
            "leading comment must appear before the element");
    }

    [Fact]
    public void Write_PreserveMode_UnchangedDocument_ReturnsOriginalText()
    {
        var src = "weird   =    \"spacing\"\n\n# comment\n";
        var doc = _parser.Parse(src);
        var output = _writer.Write(doc); // preserve = default
        Assert.Equal(src, output);
    }

    [Fact]
    public void Write_PreserveMode_AppendedArrayItem_KeepsExistingItemsByteForByte()
    {
        var src = """
            group = {
              api_operations = [
                {
                  operation_id             = "${operation_prefix}-${env}"
                  method                   = "GET"
                },
              ]
            }
            """;
        var doc = _parser.Parse(src);

        // Append a new operation to the existing array (as the synchronizer would).
        var group = Assert.IsType<HclObject>(doc.RootAssignments.Single().Value);
        var operations = Assert.IsType<HclArray>(group.Get("api_operations"));
        operations.Items.Add(new HclArrayItem
        {
            Value = new HclObject
            {
                Items =
                [
                    new HclAssignment { Key = "operation_id", Value = new HclLiteral { RawValue = "new-op", Kind = HclLiteralKind.String } },
                    new HclAssignment { Key = "method", Value = new HclLiteral { RawValue = "POST", Kind = HclLiteralKind.String } }
                ]
            }
        });

        var output = _writer.Write(doc);

        // The original item keeps its exact formatting (including alignment).
        Assert.Contains("operation_id             = \"${operation_prefix}-${env}\"", output);
        // The new item is present.
        Assert.Contains("\"new-op\"", output);
        Assert.Contains("\"POST\"", output);
        // And the result is still valid HCL.
        var reparsed = _parser.Parse(output);
        var reGroup = Assert.IsType<HclObject>(reparsed.RootAssignments.Single().Value);
        var reOps = Assert.IsType<HclArray>(reGroup.Get("api_operations"));
        Assert.Equal(2, reOps.Items.Count);
    }

    [Fact]
    public void Write_CleanMultiLineChildUnderDirtyParent_ReindentedConsistently()
    {
        // Source uses 4-space indentation; the canonical writer uses 2.
        // When the parent object is dirty (new sibling added), the clean
        // multi-line child slice must be re-indented to the new depth.
        var src = "group = {\n    nested = {\n        a = 1\n        b = 2\n    }\n}";
        var doc = _parser.Parse(src);

        var group = Assert.IsType<HclObject>(doc.RootAssignments.Single().Value);
        group.Items.Add(new HclAssignment
        {
            Key = "added",
            Value = new HclLiteral { RawValue = "x", Kind = HclLiteralKind.String }
        });

        var output = _writer.Write(doc);

        // The preserved child is anchored at the canonical depth (2) and its
        // internal relative indentation (4-space steps) is kept intact.
        Assert.Contains("\n  nested = {\n      a = 1\n      b = 2\n  }", output.Replace("\r", ""));

        // Output stays valid and complete.
        var reparsed = _parser.Parse(output);
        var reGroup = Assert.IsType<HclObject>(reparsed.RootAssignments.Single().Value);
        Assert.NotNull(reGroup.Get("nested"));
        Assert.NotNull(reGroup.Get("added"));
    }

    [Fact]
    public void Write_CleanHeredocChildUnderDirtyParent_NeverShifted()
    {
        // Heredoc bodies are whitespace-significant; the closing marker of a
        // plain << heredoc must remain at column 0 even when re-parented.
        var src = "group = {\n    policy = <<XML\n<a/>\nXML\n}";
        var doc = _parser.Parse(src);

        var group = Assert.IsType<HclObject>(doc.RootAssignments.Single().Value);
        group.Items.Add(new HclAssignment
        {
            Key = "added",
            Value = new HclLiteral { RawValue = "x", Kind = HclLiteralKind.String }
        });

        var output = _writer.Write(doc);

        Assert.Contains("\nXML", output.Replace("\r", ""));
        var reparsed = _parser.Parse(output);
        var reGroup = Assert.IsType<HclObject>(reparsed.RootAssignments.Single().Value);
        var heredoc = Assert.IsType<HclHeredoc>(reGroup.Get("policy"));
        Assert.Equal("<a/>", heredoc.Content);
    }

    [Fact]
    public void Write_BareExpression_NoQuotes()
    {
        var doc = _parser.Parse("protocols = var.protocols");
        var output = _writer.Write(doc, Canonical);
        Assert.Contains("protocols = var.protocols", output);
        Assert.DoesNotContain("\"var.protocols\"", output);
    }
}
