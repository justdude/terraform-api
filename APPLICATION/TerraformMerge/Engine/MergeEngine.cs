namespace TerraformMerge.Engine;

/// <summary>How an operation in the Target differs from the Original.</summary>
public enum DiffKind
{
    /// <summary>No similar operation exists in the Original — a candidate to add.</summary>
    Added,

    /// <summary>A similar operation exists but its fields differ.</summary>
    Changed
}

/// <summary>One entry in the computed Diff pane.</summary>
public sealed record DiffEntry(
    OperationNode Operation,
    DiffKind Kind,
    OperationNode? MatchedOriginal,
    double Similarity)
{
    public string Display => Kind switch
    {
        DiffKind.Added => $"[+] {Operation.Display}",
        DiffKind.Changed => $"[~] {Operation.Display}  (~{Similarity:0.00})",
        _ => Operation.Display
    };
}

/// <summary>
/// The merge orchestration: aligns Original against Target on the similarity
/// graph, classifies the differences for the Diff pane, and produces the merged
/// operation list (append-only — nothing is dropped) for headless use.
/// </summary>
public sealed class MergeEngine
{
    /// <summary>
    /// Computes the diff of <paramref name="target"/> relative to
    /// <paramref name="original"/>: operations only in the target are Added,
    /// matched-but-different operations are Changed. Identical matches are omitted.
    /// </summary>
    public IReadOnlyList<DiffEntry> ComputeDiff(
        IReadOnlyList<OperationNode> original,
        IReadOnlyList<OperationNode> target,
        double threshold = BlockAligner.DefaultThreshold)
    {
        var alignment = BlockAligner.Align(original, target, threshold);
        var entries = new List<DiffEntry>();

        foreach (var match in alignment.Matched)
        {
            var originalOp = original[match.LeftIndex];
            var targetOp = target[match.RightIndex];
            if (!AreEquivalent(originalOp, targetOp))
                entries.Add(new DiffEntry(targetOp, DiffKind.Changed, originalOp, match.Similarity));
        }

        foreach (var j in alignment.UnmatchedRight)
            entries.Add(new DiffEntry(target[j], DiffKind.Added, null, 0.0));

        return entries;
    }

    /// <summary>
    /// Append-only merge: the original operations followed by every target
    /// operation that has no similar counterpart in the original. Used by the
    /// headless <c>merge</c> CLI verb.
    /// </summary>
    public IReadOnlyList<OperationNode> AppendMissing(
        IReadOnlyList<OperationNode> original,
        IReadOnlyList<OperationNode> target,
        double threshold = BlockAligner.DefaultThreshold)
    {
        var alignment = BlockAligner.Align(original, target, threshold);
        var result = new List<OperationNode>(original);
        foreach (var j in alignment.UnmatchedRight)
            result.Add(target[j]);
        return result;
    }

    /// <summary>Two operations are equivalent when method, normalized URL and parameter set match.</summary>
    internal static bool AreEquivalent(OperationNode a, OperationNode b) =>
        string.Equals(a.Method, b.Method, StringComparison.OrdinalIgnoreCase)
        && string.Equals(a.NormalizedUrl, b.NormalizedUrl, StringComparison.Ordinal)
        && a.ParameterKeys.SequenceEqual(b.ParameterKeys, StringComparer.Ordinal);
}
