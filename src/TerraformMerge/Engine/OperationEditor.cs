using TerraformApi.Domain.Models.Hcl;

namespace TerraformMerge.Engine;

/// <summary>
/// Applies chosen field values to an <see cref="OperationNode"/>, honoring the
/// rule that an operation can accept data from either side except its identity
/// (<c>operation_id</c>, which is not an editable field).
///
/// For an Original-sourced node the change is applied <b>surgically</b> to the
/// underlying HCL AST: only the one changed assignment is replaced (and marked
/// dirty), so the format-preserving writer re-renders that single line and
/// reproduces everything else — nested request/response blocks, comments,
/// spacing — byte-for-byte. Generated (non-Original) blocks are rebuilt from the
/// node's fields by <see cref="OperationHclBuilder"/>, so updating the fields is
/// enough.
/// </summary>
public static class OperationEditor
{
    /// <summary>
    /// Applies <paramref name="values"/> (field → chosen value) to
    /// <paramref name="node"/>. A value that normalizes to the current one is a
    /// no-op. A non-empty status code that does not parse is rejected (the
    /// existing value is kept) rather than silently blanking the field. Returns
    /// true when at least one field actually changed.
    /// </summary>
    public static bool Apply(OperationNode node, IReadOnlyDictionary<OperationField, string> values)
    {
        var changed = false;

        foreach (var (field, rawValue) in values)
        {
            var info = OperationFieldInfo.For(field);
            var value = rawValue ?? "";
            var before = info.Get(node);

            // Normalize through the setter (method upper-cases, status code
            // parses), then compare the normalized value — so "get" for an
            // existing "GET" is correctly a no-op and does not mark the field
            // edited or dirty the AST.
            info.Set(node, value);

            // A non-empty status code that failed to parse would blank the
            // field and destroy the existing value; reject it and restore.
            if (field == OperationField.StatusCode &&
                !string.IsNullOrWhiteSpace(value) &&
                node.StatusCode is null)
            {
                info.Set(node, before);
                continue;
            }

            var after = info.Get(node);
            if (string.Equals(before, after, StringComparison.Ordinal))
                continue;

            changed = true;
            node.EditedFields.Add(field);

            if (node.Source == OperationSource.OriginalTerraform && node.ArrayItem?.Value is HclObject op)
                ApplyToAst(op, info.HclKey, after);
        }

        return changed;
    }

    /// <summary>
    /// Replaces (or inserts) one scalar assignment in the operation's AST object.
    /// The replacement carries no source span, so the writer treats it as dirty
    /// and re-renders just that line while slicing every unchanged sibling.
    /// </summary>
    private static void ApplyToAst(HclObject op, string key, string value)
    {
        var items = op.Items;
        var index = items.FindIndex(i => i is HclAssignment a && a.Key == key);

        if (index >= 0)
        {
            var old = (HclAssignment)items[index];
            items[index] = new HclAssignment
            {
                Key = old.Key,
                KeyIsQuoted = old.KeyIsQuoted,
                Value = MakeValue(value)
            };
            return;
        }

        // Field was absent. Adding an empty value would only add noise.
        if (string.IsNullOrEmpty(value))
            return;

        // Insert before the first nested block (request/response) so scalar
        // fields stay grouped at the top of the operation, matching the style
        // of the generated blocks.
        var insertAt = items.FindIndex(i => i is HclAssignment { Value: HclArray or HclObject });
        var assignment = new HclAssignment { Key = key, Value = MakeValue(value) };

        if (insertAt >= 0)
            items.Insert(insertAt, assignment);
        else
            items.Add(assignment);
    }

    /// <summary>
    /// A value containing <c>${...}</c> becomes an interpolation node so it is
    /// written as a quoted interpolation and compares structurally; everything
    /// else is a plain string literal whose special characters are escaped, so a
    /// value with a quote or backslash produces valid HCL.
    /// </summary>
    private static HclValue MakeValue(string value) =>
        value.Contains("${", StringComparison.Ordinal)
            ? new HclInterpolation { InnerText = value, Bare = false }
            : new HclLiteral { RawValue = EscapeForHcl(value), Kind = HclLiteralKind.String };

    /// <summary>
    /// Escapes a raw value into the between-quotes text an <see cref="HclLiteral"/>
    /// expects. Backslash first (so it does not double-escape the quote escapes),
    /// then the double quote.
    /// </summary>
    private static string EscapeForHcl(string value) =>
        value.Replace("\\", "\\\\").Replace("\"", "\\\"");
}
