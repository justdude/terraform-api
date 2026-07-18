namespace TerraformMerge.Engine;

/// <summary>
/// String-distance primitives for the similarity scorer: Levenshtein edit
/// distance (normalized to [0,1]) and Jaccard set similarity.
/// </summary>
public static class TextDistance
{
    /// <summary>Classic Levenshtein edit distance with an O(min) rolling buffer.</summary>
    public static int Levenshtein(string a, string b)
    {
        a ??= "";
        b ??= "";
        if (a.Length == 0) return b.Length;
        if (b.Length == 0) return a.Length;

        // Ensure b is the shorter for the rolling row.
        if (b.Length > a.Length)
            (a, b) = (b, a);

        var previous = new int[b.Length + 1];
        var current = new int[b.Length + 1];

        for (var j = 0; j <= b.Length; j++)
            previous[j] = j;

        for (var i = 1; i <= a.Length; i++)
        {
            current[0] = i;
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                current[j] = Math.Min(
                    Math.Min(current[j - 1] + 1, previous[j] + 1),
                    previous[j - 1] + cost);
            }
            (previous, current) = (current, previous);
        }

        return previous[b.Length];
    }

    /// <summary>Levenshtein distance scaled to [0,1] (0 = identical, 1 = maximally different).</summary>
    public static double NormalizedLevenshtein(string a, string b)
    {
        a ??= "";
        b ??= "";
        var max = Math.Max(a.Length, b.Length);
        if (max == 0) return 0.0;
        return (double)Levenshtein(a, b) / max;
    }

    /// <summary>Jaccard similarity of two token sets: |∩| / |∪|. Two empty sets are identical (1).</summary>
    public static double Jaccard(IEnumerable<string> a, IEnumerable<string> b)
    {
        var setA = new HashSet<string>(a, StringComparer.Ordinal);
        var setB = new HashSet<string>(b, StringComparer.Ordinal);

        if (setA.Count == 0 && setB.Count == 0)
            return 1.0;

        var intersection = setA.Count <= setB.Count
            ? setA.Count(setB.Contains)
            : setB.Count(setA.Contains);
        var union = setA.Count + setB.Count - intersection;
        return union == 0 ? 1.0 : (double)intersection / union;
    }
}
