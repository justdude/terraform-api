using TerraformApi.Application.Services.Hcl;
using TerraformApi.Domain.Interfaces;
using TerraformApi.Domain.Models.Hcl;

namespace TerraformApi.Application.Tests.Hcl;

/// <summary>
/// Tests for <see cref="HclLexer"/> covering all token types and edge cases
/// with heredocs, interpolations, escaping, and comments.
/// </summary>
public class HclLexerTests
{
    private static List<HclToken> Lex(string source) => new HclLexer(source).Tokenize();

    [Fact]
    public void Tokenize_EmptySource_ReturnsOnlyEof()
    {
        var tokens = Lex("");
        Assert.Single(tokens);
        Assert.Equal(HclTokenKind.Eof, tokens[0].Kind);
    }

    [Fact]
    public void Tokenize_Braces_EmitsBraceTokens()
    {
        var tokens = Lex("{ }");
        Assert.Equal(HclTokenKind.LBrace, tokens[0].Kind);
        Assert.Equal(HclTokenKind.RBrace, tokens[1].Kind);
    }

    [Fact]
    public void Tokenize_Brackets_EmitsBracketTokens()
    {
        var tokens = Lex("[ ]");
        Assert.Equal(HclTokenKind.LBracket, tokens[0].Kind);
        Assert.Equal(HclTokenKind.RBracket, tokens[1].Kind);
    }

    [Fact]
    public void Tokenize_SimpleAssignment_EmitsIdentEqualsString()
    {
        var tokens = Lex("a = \"b\"");
        Assert.Equal(HclTokenKind.Ident, tokens[0].Kind);
        Assert.Equal("a", tokens[0].Text);
        Assert.Equal(HclTokenKind.Equals, tokens[1].Kind);
        Assert.Equal(HclTokenKind.String, tokens[2].Kind);
        Assert.Equal("b", tokens[2].Text);
        Assert.False(tokens[2].HasInterpolation);
    }

    [Fact]
    public void Tokenize_IdentWithDashAndUnderscore()
    {
        var tokens = Lex("my-api_group = {}");
        Assert.Equal("my-api_group", tokens[0].Text);
    }

    [Fact]
    public void Tokenize_StringWithInterpolation_SetsFlag()
    {
        var tokens = Lex("a = \"${x}\"");
        Assert.Equal(HclTokenKind.String, tokens[2].Kind);
        Assert.Equal("${x}", tokens[2].Text);
        Assert.True(tokens[2].HasInterpolation);
    }

    [Fact]
    public void Tokenize_StringWithNestedBracesInInterpolation_Balanced()
    {
        var tokens = Lex("a = \"${lookup({a = 1}, x)}\"");
        Assert.Equal(HclTokenKind.String, tokens[2].Kind);
        Assert.True(tokens[2].HasInterpolation);
        Assert.Equal("${lookup({a = 1}, x)}", tokens[2].Text);
    }

    [Fact]
    public void Tokenize_EscapedQuoteInString_Preserved()
    {
        var tokens = Lex("a = \"say \\\" hi \\\"\"");
        Assert.Equal("say \\\" hi \\\"", tokens[2].Text);
    }

    [Fact]
    public void Tokenize_IntegerNumber()
    {
        var tokens = Lex("port = 8080");
        Assert.Equal(HclTokenKind.Number, tokens[2].Kind);
        Assert.Equal("8080", tokens[2].Text);
    }

    [Fact]
    public void Tokenize_DecimalNumber()
    {
        var tokens = Lex("ratio = 0.5");
        Assert.Equal(HclTokenKind.Number, tokens[2].Kind);
        Assert.Equal("0.5", tokens[2].Text);
    }

    [Fact]
    public void Tokenize_NegativeNumber()
    {
        var tokens = Lex("offset = -42");
        Assert.Equal(HclTokenKind.Number, tokens[2].Kind);
        Assert.Equal("-42", tokens[2].Text);
    }

    [Fact]
    public void Tokenize_BoolAndNull_AreIdents()
    {
        var tokens = Lex("a = true\nb = false\nc = null");
        Assert.Equal("true", tokens[2].Text);
        Assert.Equal(HclTokenKind.Ident, tokens[2].Kind);
        Assert.Equal("false", tokens[5].Text);
        Assert.Equal("null", tokens[8].Text);
    }

    [Fact]
    public void Tokenize_Heredoc_CapturesMarkerAndContent()
    {
        var tokens = Lex("a = <<XML\n<foo/>\nXML\n");
        var heredoc = tokens[2];
        Assert.Equal(HclTokenKind.Heredoc, heredoc.Kind);
        Assert.Equal("XML", heredoc.HeredocMarker);
        Assert.Equal("<foo/>", heredoc.Text);
        Assert.False(heredoc.HeredocIndented);
    }

    [Fact]
    public void Tokenize_IndentedHeredoc_TrimsMinimumIndent()
    {
        var tokens = Lex("a = <<-XML\n    <foo/>\n      <bar/>\n  XML\n");
        var heredoc = tokens[2];
        Assert.True(heredoc.HeredocIndented);
        Assert.Equal("<foo/>\n  <bar/>", heredoc.Text);
    }

    [Fact]
    public void Tokenize_HeredocWithEqualsAndBraces_NotInterpretedAsHcl()
    {
        var tokens = Lex("policy = <<XML\n<cors allow=\"true\">{nested}</cors>\nXML\n");
        var heredoc = tokens[2];
        Assert.Equal(HclTokenKind.Heredoc, heredoc.Kind);
        Assert.Contains("allow=\"true\"", heredoc.Text);
        Assert.Contains("{nested}", heredoc.Text);
        // The next token after the heredoc is EOF — nothing inside was tokenized.
        Assert.Equal(HclTokenKind.Eof, tokens[3].Kind);
    }

    [Fact]
    public void Tokenize_HashComment_EmitsCommentToken()
    {
        var tokens = Lex("# hello\na = 1");
        Assert.Equal(HclTokenKind.Comment, tokens[0].Kind);
        Assert.Equal(" hello", tokens[0].Text);
        Assert.Equal(HclCommentKind.LineHash, tokens[0].CommentKind);
    }

    [Fact]
    public void Tokenize_SlashComment_EmitsCommentToken()
    {
        var tokens = Lex("// hello\na = 1");
        Assert.Equal(HclTokenKind.Comment, tokens[0].Kind);
        Assert.Equal(" hello", tokens[0].Text);
        Assert.Equal(HclCommentKind.LineSlash, tokens[0].CommentKind);
    }

    [Fact]
    public void Tokenize_BlockComment_EmitsCommentToken()
    {
        var tokens = Lex("/* multi\nline */\na = 1");
        Assert.Equal(HclTokenKind.Comment, tokens[0].Kind);
        Assert.Equal(" multi\nline ", tokens[0].Text);
        Assert.Equal(HclCommentKind.Block, tokens[0].CommentKind);
    }

    [Fact]
    public void Tokenize_UnterminatedString_Throws()
    {
        var ex = Assert.Throws<HclParseException>(() => Lex("a = \"oops"));
        Assert.Equal(1, ex.Line);
    }

    [Fact]
    public void Tokenize_UnterminatedHeredoc_Throws()
    {
        Assert.Throws<HclParseException>(() => Lex("a = <<XML\nno marker"));
    }

    [Fact]
    public void Tokenize_TracksLineNumbers()
    {
        var tokens = Lex("a = 1\nb = 2");
        Assert.Equal(1, tokens[0].Line); // a
        Assert.Equal(2, tokens[3].Line); // b
    }

    [Fact]
    public void Tokenize_MultiByteUtf8_HandledCorrectly()
    {
        var tokens = Lex("name = \"Привет — мир 日本\"");
        Assert.Equal(HclTokenKind.String, tokens[2].Kind);
        Assert.Equal("Привет — мир 日本", tokens[2].Text);
    }

    [Fact]
    public void Tokenize_DottedIdent_SingleToken()
    {
        var tokens = Lex("protocols = var.protocols");
        Assert.Equal(HclTokenKind.Ident, tokens[2].Kind);
        Assert.Equal("var.protocols", tokens[2].Text);
    }

    [Fact]
    public void Tokenize_EscapedInterpolation_NotTreatedAsInterpolation()
    {
        // HCL escapes a literal "${" as "$${" — must not set HasInterpolation.
        var tokens = Lex("a = \"$${not_a_var}\"");
        Assert.Equal(HclTokenKind.String, tokens[2].Kind);
        Assert.False(tokens[2].HasInterpolation);
        Assert.Equal("$${not_a_var}", tokens[2].Text);
    }

    [Fact]
    public void Tokenize_MixedEscapedAndRealInterpolation_OnlyRealDetected()
    {
        var tokens = Lex("a = \"$${literal}-${real}\"");
        Assert.True(tokens[2].HasInterpolation);
        Assert.Equal("$${literal}-${real}", tokens[2].Text);
    }

    [Fact]
    public void Tokenize_SourceSpans_SliceBackToOriginalText()
    {
        var src = "key = \"value\"";
        var tokens = Lex(src);
        Assert.Equal("key", src[tokens[0].StartOffset..tokens[0].EndOffset]);
        Assert.Equal("\"value\"", src[tokens[2].StartOffset..tokens[2].EndOffset]);
    }
}
