using System.Text.RegularExpressions;
using TerraformApi.Domain.Interfaces;
using TerraformApi.Domain.Models.Hcl;

namespace TerraformApi.Application.Services.Hcl;

/// <summary>
/// Recursive-descent HCL parser (no backtracking) over <see cref="HclLexer"/> tokens.
/// Preserves comments in the AST and records source spans on every node so the
/// writer can round-trip unmodified nodes byte-for-byte.
/// </summary>
public sealed partial class HclParserService : IHclParser
{
    [GeneratedRegex(@"\$\{([^{}]*(?:\{[^{}]*\}[^{}]*)*)\}")]
    private static partial Regex InterpolationRegex();

    /// <inheritdoc />
    public HclDocument Parse(string source)
    {
        var tokens = new HclLexer(source).Tokenize();
        var state = new ParserState(tokens);

        var rootItems = new List<HclObjectItem>();
        while (state.Current.Kind != HclTokenKind.Eof)
        {
            switch (state.Current.Kind)
            {
                case HclTokenKind.Comment:
                    rootItems.Add(CommentFromToken(state.Current));
                    state.Next();
                    break;

                case HclTokenKind.Ident:
                case HclTokenKind.String:
                    rootItems.Add(ParseAssignment(state));
                    break;

                default:
                    throw new HclParseException(
                        $"Expected assignment or comment, found '{state.Current.Text}'",
                        state.Current.Line, state.Current.Column);
            }
        }

        return new HclDocument { RootItems = rootItems, OriginalSource = source };
    }

    /// <inheritdoc />
    public HclParseResult TryParse(string source)
    {
        try
        {
            var document = Parse(source);
            return new HclParseResult { Document = document };
        }
        catch (HclParseException ex)
        {
            return new HclParseResult
            {
                Diagnostics =
                [
                    new HclParseDiagnostic
                    {
                        Message = ex.Message,
                        Severity = HclDiagnosticSeverity.Error,
                        Line = ex.Line,
                        Column = ex.Column
                    }
                ]
            };
        }
    }

    private HclAssignment ParseAssignment(ParserState state)
    {
        var keyToken = state.Current;
        var keyIsQuoted = keyToken.Kind == HclTokenKind.String;
        var key = keyToken.Text;
        state.Next();

        if (state.Current.Kind != HclTokenKind.Equals)
        {
            throw new HclParseException(
                $"Expected '=' after key '{key}', found '{state.Current.Text}'",
                state.Current.Line, state.Current.Column);
        }
        state.Next();

        var value = ParseValue(state);

        return new HclAssignment
        {
            Key = key,
            KeyIsQuoted = keyIsQuoted,
            Value = value,
            Line = keyToken.Line,
            Column = keyToken.Column,
            StartOffset = keyToken.StartOffset,
            EndOffset = value.EndOffset
        };
    }

    private HclValue ParseValue(ParserState state)
    {
        var token = state.Current;
        switch (token.Kind)
        {
            case HclTokenKind.LBrace:
                return ParseObject(state);

            case HclTokenKind.LBracket:
                return ParseArray(state);

            case HclTokenKind.String:
                state.Next();
                if (token.HasInterpolation)
                {
                    return new HclInterpolation
                    {
                        InnerText = token.Text,
                        ReferencedExpressions = ExtractReferences(token.Text),
                        Line = token.Line,
                        Column = token.Column,
                        StartOffset = token.StartOffset,
                        EndOffset = token.EndOffset
                    };
                }
                return new HclLiteral
                {
                    RawValue = token.Text,
                    Kind = HclLiteralKind.String,
                    Line = token.Line,
                    Column = token.Column,
                    StartOffset = token.StartOffset,
                    EndOffset = token.EndOffset
                };

            case HclTokenKind.Number:
                state.Next();
                return new HclLiteral
                {
                    RawValue = token.Text,
                    Kind = HclLiteralKind.Number,
                    Line = token.Line,
                    Column = token.Column,
                    StartOffset = token.StartOffset,
                    EndOffset = token.EndOffset
                };

            case HclTokenKind.Heredoc:
                state.Next();
                return new HclHeredoc
                {
                    Marker = token.HeredocMarker,
                    Content = token.Text,
                    Indented = token.HeredocIndented,
                    Line = token.Line,
                    Column = token.Column,
                    StartOffset = token.StartOffset,
                    EndOffset = token.EndOffset
                };

            case HclTokenKind.Ident:
                state.Next();
                return token.Text switch
                {
                    "true" or "false" => new HclLiteral
                    {
                        RawValue = token.Text,
                        Kind = HclLiteralKind.Bool,
                        Line = token.Line,
                        Column = token.Column,
                        StartOffset = token.StartOffset,
                        EndOffset = token.EndOffset
                    },
                    "null" => new HclLiteral
                    {
                        RawValue = token.Text,
                        Kind = HclLiteralKind.Null,
                        Line = token.Line,
                        Column = token.Column,
                        StartOffset = token.StartOffset,
                        EndOffset = token.EndOffset
                    },
                    // Bare expression reference: protocols = var.protocols
                    _ => new HclInterpolation
                    {
                        InnerText = token.Text,
                        ReferencedExpressions = [token.Text],
                        Bare = true,
                        Line = token.Line,
                        Column = token.Column,
                        StartOffset = token.StartOffset,
                        EndOffset = token.EndOffset
                    }
                };

            default:
                throw new HclParseException(
                    $"Expected a value, found '{token.Text}'",
                    token.Line, token.Column);
        }
    }

    private HclObject ParseObject(ParserState state)
    {
        var openToken = state.Current;
        state.Next(); // past '{'

        var items = new List<HclObjectItem>();

        while (true)
        {
            var token = state.Current;
            switch (token.Kind)
            {
                case HclTokenKind.RBrace:
                    state.Next();
                    return new HclObject
                    {
                        Items = items,
                        Line = openToken.Line,
                        Column = openToken.Column,
                        StartOffset = openToken.StartOffset,
                        EndOffset = token.EndOffset
                    };

                case HclTokenKind.Comment:
                    items.Add(CommentFromToken(token));
                    state.Next();
                    break;

                case HclTokenKind.Ident:
                case HclTokenKind.String:
                    items.Add(ParseAssignment(state));
                    break;

                case HclTokenKind.Comma:
                    // Tolerate stray commas between object items.
                    state.Next();
                    break;

                case HclTokenKind.Eof:
                    throw new HclParseException(
                        "Unterminated object: '}' expected before end of input",
                        openToken.Line, openToken.Column);

                default:
                    throw new HclParseException(
                        $"Expected key, comment or '}}' in object, found '{token.Text}'",
                        token.Line, token.Column);
            }
        }
    }

    private HclArray ParseArray(ParserState state)
    {
        var openToken = state.Current;
        state.Next(); // past '['

        var items = new List<HclArrayItem>();
        var pendingComments = new List<HclComment>();

        while (true)
        {
            var token = state.Current;
            switch (token.Kind)
            {
                case HclTokenKind.RBracket:
                    state.Next();
                    return new HclArray
                    {
                        Items = items,
                        TrailingComments = pendingComments,
                        Line = openToken.Line,
                        Column = openToken.Column,
                        StartOffset = openToken.StartOffset,
                        EndOffset = token.EndOffset
                    };

                case HclTokenKind.Comment:
                    pendingComments.Add(CommentFromToken(token) with { IsLeading = true });
                    state.Next();
                    break;

                case HclTokenKind.Comma:
                    state.Next();
                    break;

                case HclTokenKind.Eof:
                    throw new HclParseException(
                        "Unterminated array: ']' expected before end of input",
                        openToken.Line, openToken.Column);

                default:
                    var leading = pendingComments;
                    pendingComments = [];
                    var value = ParseValue(state);
                    var itemStart = leading.Count > 0 ? leading[0].StartOffset : value.StartOffset;
                    items.Add(new HclArrayItem
                    {
                        LeadingComments = leading,
                        Value = value,
                        Line = value.Line,
                        Column = value.Column,
                        StartOffset = itemStart,
                        EndOffset = value.EndOffset
                    });
                    break;
            }
        }
    }

    private static HclComment CommentFromToken(HclToken token) =>
        new()
        {
            Text = token.Text,
            Kind = token.CommentKind,
            Line = token.Line,
            Column = token.Column,
            StartOffset = token.StartOffset,
            EndOffset = token.EndOffset
        };

    /// <summary>
    /// Extracts the expression names referenced inside ${...} blocks, in order.
    /// </summary>
    internal static List<string> ExtractReferences(string innerText)
    {
        var result = new List<string>();
        foreach (Match match in InterpolationRegex().Matches(innerText))
            result.Add(match.Groups[1].Value.Trim());
        return result;
    }

    private sealed class ParserState
    {
        private readonly List<HclToken> _tokens;
        private int _index;

        public ParserState(List<HclToken> tokens) => _tokens = tokens;

        public HclToken Current => _tokens[_index];

        public void Next()
        {
            if (_index < _tokens.Count - 1)
                _index++;
        }
    }
}
