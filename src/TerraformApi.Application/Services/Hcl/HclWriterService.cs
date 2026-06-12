using System.Text;
using TerraformApi.Domain.Interfaces;
using TerraformApi.Domain.Models.Hcl;

namespace TerraformApi.Application.Services.Hcl;

/// <summary>
/// Serializes an HCL AST back to text.
/// With <see cref="HclWriteOptions.PreserveOriginalFormatting"/> (default),
/// nodes parsed from source that have not been modified are emitted
/// byte-for-byte from their original source slice. Only new or dirty nodes
/// are rendered canonically (2-space indent, aligned '='), which gives the
/// minimal-change guarantee required by append-only sync.
/// </summary>
public sealed class HclWriterService : IHclWriter
{
    /// <inheritdoc />
    public string Write(HclDocument document, HclWriteOptions? options = null)
    {
        options ??= new HclWriteOptions();
        var source = options.PreserveOriginalFormatting ? document.OriginalSource : null;

        // Fast path: nothing changed → the original text verbatim.
        if (source is not null && !document.RootItems.Any(i => IsDirtyItem(i)))
            return source;

        var sb = new StringBuilder();
        var keyWidth = ComputeKeyWidth(document.RootItems, options);
        foreach (var item in document.RootItems)
            WriteObjectItem(sb, item, 0, keyWidth, source, options);
        return sb.ToString();
    }

    // ---------------------------------------------------------------
    // Dirtiness: a node is dirty when it (or any descendant) was created
    // programmatically (no source span) or explicitly marked Dirty.
    // ---------------------------------------------------------------

    internal static bool IsDirtyItem(HclObjectItem item) => item switch
    {
        HclComment c => c.Dirty || !c.HasSourceSpan,
        HclAssignment a => a.Dirty || !a.HasSourceSpan || IsDirtyValue(a.Value),
        _ => true
    };

    internal static bool IsDirtyValue(HclValue value)
    {
        if (value.Dirty || !value.HasSourceSpan)
            return true;

        return value switch
        {
            HclObject obj => obj.Items.Any(IsDirtyItem),
            HclArray arr => arr.Items.Any(IsDirtyArrayItem) || arr.TrailingComments.Any(c => c.Dirty || !c.HasSourceSpan),
            _ => false
        };
    }

    private static bool IsDirtyArrayItem(HclArrayItem item) =>
        item.Dirty
        || item.LeadingComments.Any(c => c.Dirty || !c.HasSourceSpan)
        || IsDirtyValue(item.Value);

    // ---------------------------------------------------------------
    // Rendering
    // ---------------------------------------------------------------

    private void WriteObjectItem(
        StringBuilder sb, HclObjectItem item, int indent, int keyWidth, string? source, HclWriteOptions options)
    {
        var pad = new string(' ', indent);

        // Fast path: emit the original slice (re-indented if depth changed).
        if (source is not null && !IsDirtyItem(item))
        {
            sb.Append(pad).Append(ReindentedSlice(source, item, indent)).Append(options.LineEnding);
            return;
        }

        switch (item)
        {
            case HclComment comment:
                sb.Append(pad).Append(FormatComment(comment)).Append(options.LineEnding);
                break;

            case HclAssignment assignment:
                var keyText = assignment.KeyIsQuoted ? $"\"{assignment.Key}\"" : assignment.Key;
                if (keyWidth > 0 && keyText.Length < keyWidth)
                    keyText = keyText.PadRight(keyWidth);
                sb.Append(pad).Append(keyText).Append(" = ");
                WriteValue(sb, assignment.Value, indent, source, options);
                sb.Append(options.LineEnding);
                break;
        }
    }

    private void WriteValue(
        StringBuilder sb, HclValue value, int indent, string? source, HclWriteOptions options)
    {
        // Fast path for unmodified subtrees.
        if (source is not null && !IsDirtyValue(value))
        {
            sb.Append(Slice(source, value));
            return;
        }

        switch (value)
        {
            case HclLiteral { Kind: HclLiteralKind.String } literal:
                sb.Append('"').Append(literal.RawValue).Append('"');
                break;

            case HclLiteral literal:
                sb.Append(literal.RawValue);
                break;

            case HclInterpolation interpolation:
                if (interpolation.Bare)
                    sb.Append(interpolation.InnerText);
                else
                    sb.Append('"').Append(interpolation.InnerText).Append('"');
                break;

            case HclHeredoc heredoc:
                sb.Append("<<");
                if (heredoc.Indented)
                    sb.Append('-');
                sb.Append(heredoc.Marker).Append(options.LineEnding);
                sb.Append(heredoc.Content).Append(options.LineEnding);
                if (heredoc.Indented)
                    sb.Append(new string(' ', indent));
                sb.Append(heredoc.Marker);
                break;

            case HclObject obj:
                WriteObject(sb, obj, indent, source, options);
                break;

            case HclArray array:
                WriteArray(sb, array, indent, source, options);
                break;
        }
    }

    private void WriteObject(
        StringBuilder sb, HclObject obj, int indent, string? source, HclWriteOptions options)
    {
        if (obj.Items.Count == 0)
        {
            sb.Append("{}");
            return;
        }

        sb.Append('{').Append(options.LineEnding);
        var innerIndent = indent + options.IndentSize;
        var keyWidth = ComputeKeyWidth(obj.Items, options);

        foreach (var item in obj.Items)
            WriteObjectItem(sb, item, innerIndent, keyWidth, source, options);

        sb.Append(new string(' ', indent)).Append('}');
    }

    private void WriteArray(
        StringBuilder sb, HclArray array, int indent, string? source, HclWriteOptions options)
    {
        if (array.Items.Count == 0 && array.TrailingComments.Count == 0)
        {
            sb.Append("[]");
            return;
        }

        // Inline rendering for short scalar arrays without comments: ["https"]
        var allScalar = array.Items.All(i =>
            i.LeadingComments.Count == 0 && i.Value is HclLiteral or HclInterpolation);
        if (allScalar && array.TrailingComments.Count == 0)
        {
            sb.Append('[');
            for (var i = 0; i < array.Items.Count; i++)
            {
                if (i > 0)
                    sb.Append(", ");
                WriteValue(sb, array.Items[i].Value, indent, source, options);
            }
            sb.Append(']');
            return;
        }

        sb.Append('[').Append(options.LineEnding);
        var innerIndent = indent + options.IndentSize;
        var pad = new string(' ', innerIndent);

        foreach (var item in array.Items)
        {
            // Fast path: unchanged element (incl. its leading comments) → original slice.
            if (source is not null && !IsDirtyArrayItem(item) && item.HasSourceSpan)
            {
                sb.Append(pad).Append(ReindentedSlice(source, item, innerIndent)).Append(',').Append(options.LineEnding);
                continue;
            }

            foreach (var comment in item.LeadingComments)
                sb.Append(pad).Append(FormatComment(comment)).Append(options.LineEnding);

            sb.Append(pad);
            WriteValue(sb, item.Value, innerIndent, source, options);
            sb.Append(',').Append(options.LineEnding);
        }

        foreach (var comment in array.TrailingComments)
            sb.Append(pad).Append(FormatComment(comment)).Append(options.LineEnding);

        sb.Append(new string(' ', indent)).Append(']');
    }

    private static int ComputeKeyWidth(IEnumerable<HclObjectItem> items, HclWriteOptions options)
    {
        if (!options.AlignAssignmentEquals)
            return 0;

        var maxKey = 0;
        foreach (var item in items)
        {
            if (item is HclAssignment a)
            {
                var len = a.Key.Length + (a.KeyIsQuoted ? 2 : 0);
                if (len > maxKey)
                    maxKey = len;
            }
        }
        return Math.Min(maxKey, options.MaxAlignedKeyLength);
    }

    private static string FormatComment(HclComment comment) => comment.Kind switch
    {
        HclCommentKind.LineHash => "#" + comment.Text,
        HclCommentKind.LineSlash => "//" + comment.Text,
        HclCommentKind.Block => "/*" + comment.Text + "*/",
        _ => "#" + comment.Text
    };

    private static string Slice(string source, HclNode node) =>
        source[node.StartOffset..node.EndOffset];

    /// <summary>
    /// Returns the original slice with continuation lines shifted so the node's
    /// original indentation (Column - 1) becomes <paramref name="targetIndent"/>.
    /// Keeps multi-line slices aligned when a clean child is emitted under a
    /// re-rendered parent at a different depth. Single-line slices and slices
    /// already at the target depth are returned unchanged.
    /// </summary>
    private static string ReindentedSlice(string source, HclNode node, int targetIndent)
    {
        var slice = Slice(source, node);
        var originalIndent = Math.Max(node.Column - 1, 0);
        if (originalIndent == targetIndent || !slice.Contains('\n'))
            return slice;

        // Heredoc bodies are whitespace-significant and the closing marker of a
        // plain << heredoc must stay at column 0 — never shift those slices.
        if (ContainsHeredoc(node))
            return slice;

        var delta = targetIndent - originalIndent;
        var lines = slice.Split('\n');
        for (var i = 1; i < lines.Length; i++)
        {
            var line = lines[i];
            if (line.Length == 0)
                continue;

            if (delta > 0)
            {
                lines[i] = new string(' ', delta) + line;
            }
            else
            {
                var removable = Math.Min(-delta, line.Length - line.TrimStart(' ').Length);
                lines[i] = line[removable..];
            }
        }
        return string.Join('\n', lines);
    }

    private static bool ContainsHeredoc(HclNode node) => node switch
    {
        HclHeredoc => true,
        HclAssignment a => ContainsHeredoc(a.Value),
        HclObject o => o.Items.Any(ContainsHeredoc),
        HclArray ar => ar.Items.Any(ContainsHeredoc),
        HclArrayItem i => ContainsHeredoc(i.Value),
        _ => false
    };
}
