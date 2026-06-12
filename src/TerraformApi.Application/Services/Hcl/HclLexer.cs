using TerraformApi.Domain.Interfaces;
using TerraformApi.Domain.Models.Hcl;

namespace TerraformApi.Application.Services.Hcl;

/// <summary>Token kinds produced by <see cref="HclLexer"/>.</summary>
internal enum HclTokenKind
{
    LBrace,
    RBrace,
    LBracket,
    RBracket,
    Equals,
    Comma,
    Ident,
    String,
    Number,
    Heredoc,
    Comment,
    Eof
}

/// <summary>
/// A lexical token with source position and character span.
/// </summary>
internal sealed record HclToken
{
    public required HclTokenKind Kind { get; init; }

    /// <summary>
    /// Token payload. For strings: the raw text between the quotes (escapes preserved).
    /// For comments: the text without the comment markers. For heredocs: the body.
    /// </summary>
    public string Text { get; init; } = "";

    public int Line { get; init; }
    public int Column { get; init; }
    public int StartOffset { get; init; }
    public int EndOffset { get; init; }

    /// <summary>For String tokens: true when the text contains ${...} interpolation.</summary>
    public bool HasInterpolation { get; init; }

    /// <summary>For Comment tokens: the comment syntax used.</summary>
    public HclCommentKind CommentKind { get; init; }

    /// <summary>For Heredoc tokens: the marker identifier.</summary>
    public string HeredocMarker { get; init; } = "";

    /// <summary>For Heredoc tokens: true for the &lt;&lt;- variant.</summary>
    public bool HeredocIndented { get; init; }
}

/// <summary>
/// Minimal HCL lexer covering the subset used in APIM Terraform configs.
/// Emits COMMENT tokens (they are preserved in the AST, not skipped).
/// </summary>
internal sealed class HclLexer
{
    private readonly string _src;
    private int _pos;
    private int _line = 1;
    private int _col = 1;

    public HclLexer(string source) => _src = source;

    public List<HclToken> Tokenize()
    {
        var tokens = new List<HclToken>();
        while (true)
        {
            var token = NextToken();
            tokens.Add(token);
            if (token.Kind == HclTokenKind.Eof)
                break;
        }
        return tokens;
    }

    private HclToken NextToken()
    {
        SkipWhitespace();

        if (_pos >= _src.Length)
            return new HclToken { Kind = HclTokenKind.Eof, Line = _line, Column = _col, StartOffset = _pos, EndOffset = _pos };

        var startLine = _line;
        var startCol = _col;
        var start = _pos;
        var ch = _src[_pos];

        switch (ch)
        {
            case '{':
                Advance();
                return Tok(HclTokenKind.LBrace, "{", startLine, startCol, start);
            case '}':
                Advance();
                return Tok(HclTokenKind.RBrace, "}", startLine, startCol, start);
            case '[':
                Advance();
                return Tok(HclTokenKind.LBracket, "[", startLine, startCol, start);
            case ']':
                Advance();
                return Tok(HclTokenKind.RBracket, "]", startLine, startCol, start);
            case '=':
                Advance();
                return Tok(HclTokenKind.Equals, "=", startLine, startCol, start);
            case ',':
                Advance();
                return Tok(HclTokenKind.Comma, ",", startLine, startCol, start);
            case '"':
                return ReadString(startLine, startCol, start);
            case '#':
                return ReadLineComment(1, HclCommentKind.LineHash, startLine, startCol, start);
            case '/':
                if (Peek(1) == '/')
                    return ReadLineComment(2, HclCommentKind.LineSlash, startLine, startCol, start);
                if (Peek(1) == '*')
                    return ReadBlockComment(startLine, startCol, start);
                throw new HclParseException($"Unexpected character '{ch}'", startLine, startCol);
            case '<':
                if (Peek(1) == '<')
                    return ReadHeredoc(startLine, startCol, start);
                throw new HclParseException($"Unexpected character '{ch}'", startLine, startCol);
        }

        if (char.IsDigit(ch) || (ch == '-' && char.IsDigit(Peek(1))))
            return ReadNumber(startLine, startCol, start);

        if (char.IsLetter(ch) || ch == '_')
            return ReadIdent(startLine, startCol, start);

        throw new HclParseException($"Unexpected character '{ch}'", startLine, startCol);
    }

    private void SkipWhitespace()
    {
        while (_pos < _src.Length)
        {
            var ch = _src[_pos];
            if (ch is ' ' or '\t' or '\r' or '\n')
                Advance();
            else
                break;
        }
    }

    private HclToken ReadString(int line, int col, int start)
    {
        Advance(); // past opening quote
        var sb = new System.Text.StringBuilder();
        var hasInterpolation = false;

        while (true)
        {
            if (_pos >= _src.Length)
                throw new HclParseException("Unterminated string", line, col);

            var ch = _src[_pos];

            if (ch == '"')
            {
                Advance();
                break;
            }

            if (ch == '\\')
            {
                // Preserve the escape sequence verbatim.
                sb.Append(ch);
                Advance();
                if (_pos < _src.Length)
                {
                    sb.Append(_src[_pos]);
                    Advance();
                }
                continue;
            }

            if (ch == '$' && Peek(1) == '{')
            {
                hasInterpolation = true;
                sb.Append("${");
                Advance();
                Advance();
                var depth = 1;
                while (_pos < _src.Length && depth > 0)
                {
                    var c = _src[_pos];
                    if (c == '{') depth++;
                    else if (c == '}') depth--;
                    if (depth > 0) sb.Append(c);
                    Advance();
                }
                if (depth > 0)
                    throw new HclParseException("Unterminated interpolation '${'", line, col);
                sb.Append('}');
                continue;
            }

            if (ch == '\n')
                throw new HclParseException("Unterminated string (newline in string literal)", line, col);

            sb.Append(ch);
            Advance();
        }

        return new HclToken
        {
            Kind = HclTokenKind.String,
            Text = sb.ToString(),
            HasInterpolation = hasInterpolation,
            Line = line,
            Column = col,
            StartOffset = start,
            EndOffset = _pos
        };
    }

    private HclToken ReadLineComment(int prefixLength, HclCommentKind kind, int line, int col, int start)
    {
        for (var i = 0; i < prefixLength; i++)
            Advance();

        var textStart = _pos;
        while (_pos < _src.Length && _src[_pos] != '\n')
            Advance();

        var text = _src[textStart.._pos].TrimEnd('\r');
        return new HclToken
        {
            Kind = HclTokenKind.Comment,
            Text = text,
            CommentKind = kind,
            Line = line,
            Column = col,
            StartOffset = start,
            EndOffset = start + prefixLength + text.Length
        };
    }

    private HclToken ReadBlockComment(int line, int col, int start)
    {
        Advance(); // '/'
        Advance(); // '*'
        var textStart = _pos;

        while (_pos < _src.Length)
        {
            if (_src[_pos] == '*' && Peek(1) == '/')
            {
                var text = _src[textStart.._pos];
                Advance();
                Advance();
                return new HclToken
                {
                    Kind = HclTokenKind.Comment,
                    Text = text,
                    CommentKind = HclCommentKind.Block,
                    Line = line,
                    Column = col,
                    StartOffset = start,
                    EndOffset = _pos
                };
            }
            Advance();
        }

        throw new HclParseException("Unterminated block comment", line, col);
    }

    private HclToken ReadHeredoc(int line, int col, int start)
    {
        Advance(); // '<'
        Advance(); // '<'

        var indented = false;
        if (_pos < _src.Length && _src[_pos] == '-')
        {
            indented = true;
            Advance();
        }

        var markerStart = _pos;
        while (_pos < _src.Length && (char.IsLetterOrDigit(_src[_pos]) || _src[_pos] == '_'))
            Advance();

        var marker = _src[markerStart.._pos];
        if (marker.Length == 0)
            throw new HclParseException("Heredoc marker expected after '<<'", line, col);

        // Skip to end of the opening line.
        while (_pos < _src.Length && _src[_pos] != '\n')
            Advance();
        if (_pos < _src.Length)
            Advance(); // past '\n'

        var lines = new List<string>();
        var endOffset = _pos;

        while (true)
        {
            if (_pos >= _src.Length)
                throw new HclParseException($"Unterminated heredoc (marker '{marker}' not found)", line, col);

            var lineStart = _pos;
            while (_pos < _src.Length && _src[_pos] != '\n')
                Advance();

            var rawLine = _src[lineStart.._pos].TrimEnd('\r');

            if (rawLine.Trim() == marker)
            {
                endOffset = _pos; // span ends after the marker, before its newline
                if (_pos < _src.Length)
                    Advance(); // past '\n'
                break;
            }

            lines.Add(rawLine);
            if (_pos < _src.Length)
                Advance(); // past '\n'
        }

        var content = string.Join("\n", lines);
        if (indented && lines.Count > 0)
        {
            var minIndent = lines
                .Where(l => l.Trim().Length > 0)
                .Select(l => l.Length - l.TrimStart().Length)
                .DefaultIfEmpty(0)
                .Min();
            if (minIndent > 0)
                content = string.Join("\n", lines.Select(l => l.Length >= minIndent ? l[minIndent..] : l));
        }

        return new HclToken
        {
            Kind = HclTokenKind.Heredoc,
            Text = content,
            HeredocMarker = marker,
            HeredocIndented = indented,
            Line = line,
            Column = col,
            StartOffset = start,
            EndOffset = endOffset
        };
    }

    private HclToken ReadNumber(int line, int col, int start)
    {
        if (_src[_pos] == '-')
            Advance();
        while (_pos < _src.Length && char.IsDigit(_src[_pos]))
            Advance();
        if (_pos < _src.Length && _src[_pos] == '.' && char.IsDigit(Peek(1)))
        {
            Advance();
            while (_pos < _src.Length && char.IsDigit(_src[_pos]))
                Advance();
        }

        return new HclToken
        {
            Kind = HclTokenKind.Number,
            Text = _src[start.._pos],
            Line = line,
            Column = col,
            StartOffset = start,
            EndOffset = _pos
        };
    }

    private HclToken ReadIdent(int line, int col, int start)
    {
        while (_pos < _src.Length)
        {
            var ch = _src[_pos];
            if (char.IsLetterOrDigit(ch) || ch == '_' || ch == '-')
            {
                Advance();
            }
            else if (ch == '.' && _pos + 1 < _src.Length
                     && (char.IsLetter(_src[_pos + 1]) || _src[_pos + 1] == '_'))
            {
                // Dotted expression path like var.foo — kept as one identifier token.
                Advance();
            }
            else
            {
                break;
            }
        }

        return new HclToken
        {
            Kind = HclTokenKind.Ident,
            Text = _src[start.._pos],
            Line = line,
            Column = col,
            StartOffset = start,
            EndOffset = _pos
        };
    }

    private HclToken Tok(HclTokenKind kind, string text, int line, int col, int start) =>
        new()
        {
            Kind = kind,
            Text = text,
            Line = line,
            Column = col,
            StartOffset = start,
            EndOffset = _pos
        };

    private char Peek(int ahead) =>
        _pos + ahead < _src.Length ? _src[_pos + ahead] : '\0';

    private void Advance()
    {
        if (_pos >= _src.Length)
            return;
        if (_src[_pos] == '\n')
        {
            _line++;
            _col = 1;
        }
        else
        {
            _col++;
        }
        _pos++;
    }
}
