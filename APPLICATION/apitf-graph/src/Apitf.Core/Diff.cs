namespace Apitf.Core;

public enum ChangeType { Unchanged, Added, Removed, Modified }

/// <summary>One operation-level change between two specs.</summary>
public sealed record OperationChange(OperationKey Key, ChangeType Type);

/// <summary>Two-way operation diff: what changed going from A (left) to B (right).</summary>
public static class Differ
{
    public static IReadOnlyList<OperationChange> Diff(SpecModel left, SpecModel right)
    {
        var a = left.OperationMap();
        var b = right.OperationMap();
        var keys = new SortedSet<string>(
            a.Keys.Concat(b.Keys).Select(k => k.ToString()), StringComparer.Ordinal);

        var result = new List<OperationChange>();
        foreach (var ks in keys)
        {
            // recover key from either map
            var key = a.Keys.Concat(b.Keys).First(k => k.ToString() == ks);
            var inA = a.TryGetValue(key, out var oa);
            var inB = b.TryGetValue(key, out var ob);

            ChangeType type;
            if (inA && inB)
                type = oa!.Canonical == ob!.Canonical ? ChangeType.Unchanged : ChangeType.Modified;
            else if (inB)
                type = ChangeType.Added;
            else
                type = ChangeType.Removed;

            result.Add(new OperationChange(key, type));
        }
        return result;
    }
}
