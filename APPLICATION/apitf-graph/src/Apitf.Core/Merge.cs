using System.Text.Json.Nodes;

namespace Apitf.Core;

/// <summary>How a conflicted operation should be resolved.</summary>
public enum Resolution { Unresolved, TakeOurs, TakeTheirs, TakeBase, Delete }

/// <summary>Why an operation conflicts (git-style classification).</summary>
public enum ConflictKind { ModifyModify, AddAdd, ModifyDelete, DeleteModify }

/// <summary>A merge conflict requiring a human (or rule) decision.</summary>
public sealed class Conflict
{
    public required OperationKey Key { get; init; }
    public required ConflictKind Kind { get; init; }
    public JsonNode? Base { get; init; }
    public JsonNode? Ours { get; init; }
    public JsonNode? Theirs { get; init; }
    public Resolution Resolution { get; set; } = Resolution.Unresolved;
}

/// <summary>Auto-resolved or conflicting outcome for one operation.</summary>
public sealed record MergeEntry(OperationKey Key, ChangeType OursVsBase, ChangeType TheirsVsBase, bool Conflicted);

/// <summary>
/// Result of a three-way merge. Auto-merged operations are applied directly;
/// conflicts must be resolved before <see cref="Build"/>.
/// </summary>
public sealed class MergeResult
{
    private readonly SpecModel _ours;                       // scaffold source (info/servers/components)
    private readonly Dictionary<OperationKey, JsonNode?> _auto;   // null => operation deleted

    public IReadOnlyList<Conflict> Conflicts { get; }
    public IReadOnlyList<MergeEntry> Entries { get; }

    internal MergeResult(SpecModel ours,
                         Dictionary<OperationKey, JsonNode?> auto,
                         List<Conflict> conflicts,
                         List<MergeEntry> entries)
    {
        _ours = ours;
        _auto = auto;
        Conflicts = conflicts;
        Entries = entries;
    }

    public bool HasConflicts => Conflicts.Any(c => c.Resolution == Resolution.Unresolved);
    public int ConflictCount => Conflicts.Count;
    public int AutoMergedCount => _auto.Count;

    /// <summary>Apply all resolutions and produce the merged spec. Throws if conflicts remain.</summary>
    public SpecModel Build()
    {
        var unresolved = Conflicts.Where(c => c.Resolution == Resolution.Unresolved).ToList();
        if (unresolved.Count > 0)
            throw new InvalidOperationException(
                $"{unresolved.Count} unresolved conflict(s): {string.Join(", ", unresolved.Select(c => c.Key))}");

        // Final desired operation set = auto-merged + resolved conflicts.
        var final = new Dictionary<OperationKey, JsonNode?>(_auto);
        foreach (var c in Conflicts)
        {
            JsonNode? chosen = c.Resolution switch
            {
                Resolution.TakeOurs => c.Ours,
                Resolution.TakeTheirs => c.Theirs,
                Resolution.TakeBase => c.Base,
                Resolution.Delete => null,
                _ => null
            };
            final[c.Key] = chosen;
        }

        // Start from OURS as scaffold, then overlay the final operation set.
        var merged = _ours.Clone();
        var paths = merged.Root["paths"] as JsonObject;
        if (paths is null) { paths = new JsonObject(); merged.Root["paths"] = paths; }

        foreach (var (key, node) in final)
        {
            var item = paths[key.Path] as JsonObject;
            if (node is null)
            {
                item?.Remove(key.Method);
            }
            else
            {
                if (item is null) { item = new JsonObject(); paths[key.Path] = item; }
                item[key.Method] = node.DeepClone();
            }
        }

        // Prune path items that no longer contain any operation.
        foreach (var (route, pathItem) in paths.ToList())
        {
            if (pathItem is JsonObject obj &&
                !SpecModel.HttpMethods.Any(m => obj.ContainsKey(m)))
            {
                paths.Remove(route);
            }
        }

        return merged.Canonicalized();
    }
}

/// <summary>Git-style three-way merge over OpenAPI operations.</summary>
public static class ThreeWayMerger
{
    public static MergeResult Merge(SpecModel @base, SpecModel ours, SpecModel theirs)
    {
        var b = @base.OperationMap();
        var o = ours.OperationMap();
        var t = theirs.OperationMap();

        var allKeys = b.Keys.Concat(o.Keys).Concat(t.Keys).Distinct().ToList();

        var auto = new Dictionary<OperationKey, JsonNode?>();
        var conflicts = new List<Conflict>();
        var entries = new List<MergeEntry>();

        foreach (var key in allKeys)
        {
            b.TryGetValue(key, out var bo);
            o.TryGetValue(key, out var oo);
            t.TryGetValue(key, out var to);

            string? bc = bo?.Canonical, oc = oo?.Canonical, tc = to?.Canonical;

            var oursVsBase = Classify(bc, oc);
            var theirsVsBase = Classify(bc, tc);

            // 1. Ours == Theirs (incl. both deleted) -> no conflict, take ours.
            if (oc == tc)
            {
                if (oo is not null) auto[key] = oo.Node.DeepClone();
                // both deleted => leave out entirely
                entries.Add(new MergeEntry(key, oursVsBase, theirsVsBase, false));
                continue;
            }

            // 2. Ours unchanged vs base -> take theirs (their edit/add/delete wins).
            if (oc == bc)
            {
                if (to is not null) auto[key] = to.Node.DeepClone();
                else auto[key] = null; // theirs deleted
                entries.Add(new MergeEntry(key, ChangeType.Unchanged, theirsVsBase, false));
                continue;
            }

            // 3. Theirs unchanged vs base -> take ours.
            if (tc == bc)
            {
                if (oo is not null) auto[key] = oo.Node.DeepClone();
                else auto[key] = null; // ours deleted
                entries.Add(new MergeEntry(key, oursVsBase, ChangeType.Unchanged, false));
                continue;
            }

            // 4. Both diverged from base AND from each other -> CONFLICT.
            var kind = (bo, oo, to) switch
            {
                (null, not null, not null) => ConflictKind.AddAdd,
                (not null, null, not null) => ConflictKind.DeleteModify, // ours deleted, theirs modified
                (not null, not null, null) => ConflictKind.ModifyDelete, // ours modified, theirs deleted
                _ => ConflictKind.ModifyModify
            };
            conflicts.Add(new Conflict
            {
                Key = key,
                Kind = kind,
                Base = bo?.Node.DeepClone(),
                Ours = oo?.Node.DeepClone(),
                Theirs = to?.Node.DeepClone()
            });
            entries.Add(new MergeEntry(key, oursVsBase, theirsVsBase, true));
        }

        return new MergeResult(ours, auto, conflicts, entries);
    }

    private static ChangeType Classify(string? baseC, string? sideC)
    {
        if (baseC == sideC) return ChangeType.Unchanged;
        if (baseC is null) return ChangeType.Added;
        if (sideC is null) return ChangeType.Removed;
        return ChangeType.Modified;
    }
}
