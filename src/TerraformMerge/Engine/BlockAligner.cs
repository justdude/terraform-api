namespace TerraformMerge.Engine;

/// <summary>One aligned pair of operations with the similarity that joined them.</summary>
public sealed record Alignment(int LeftIndex, int RightIndex, double Similarity);

/// <summary>
/// The outcome of aligning two operation lists: matched pairs above the
/// threshold, plus the indices left unmatched on each side.
/// </summary>
public sealed record AlignmentResult(
    IReadOnlyList<Alignment> Matched,
    IReadOnlyList<int> UnmatchedLeft,
    IReadOnlyList<int> UnmatchedRight);

/// <summary>
/// Aligns two sets of operation nodes by building a complete bipartite
/// similarity graph and solving the maximum-weight assignment on it with the
/// Hungarian (Kuhn–Munkres) algorithm — a globally optimal matching, not a
/// greedy one, so a locally attractive pair never steals a partner that a
/// better global assignment needs. Pairs whose similarity falls below
/// <c>threshold</c> are demoted to "unmatched".
/// </summary>
public static class BlockAligner
{
    public const double DefaultThreshold = 0.55;

    public static AlignmentResult Align(
        IReadOnlyList<OperationNode> left,
        IReadOnlyList<OperationNode> right,
        double threshold = DefaultThreshold)
    {
        var matched = new List<Alignment>();
        var usedLeft = new HashSet<int>();
        var usedRight = new HashSet<int>();

        if (left.Count > 0 && right.Count > 0)
        {
            var similarity = new double[left.Count][];
            for (var i = 0; i < left.Count; i++)
            {
                similarity[i] = new double[right.Count];
                for (var j = 0; j < right.Count; j++)
                    similarity[i][j] = SimilarityScorer.Similarity(left[i], right[j]);
            }

            foreach (var (i, j) in MaxWeightAssignment(similarity))
            {
                if (similarity[i][j] >= threshold)
                {
                    matched.Add(new Alignment(i, j, similarity[i][j]));
                    usedLeft.Add(i);
                    usedRight.Add(j);
                }
            }
        }

        matched.Sort((x, y) => y.Similarity.CompareTo(x.Similarity));

        var unmatchedLeft = Enumerable.Range(0, left.Count).Where(i => !usedLeft.Contains(i)).ToList();
        var unmatchedRight = Enumerable.Range(0, right.Count).Where(j => !usedRight.Contains(j)).ToList();

        return new AlignmentResult(matched, unmatchedLeft, unmatchedRight);
    }

    /// <summary>
    /// Maximum-weight bipartite assignment via the Hungarian algorithm.
    /// Weights are negated into costs and the rectangular matrix is padded to a
    /// square with zero-weight dummy cells; only real row/column pairs are
    /// returned. Runs in O(n³) over the padded dimension — ample for the tens
    /// of operations a config holds.
    /// </summary>
    internal static IEnumerable<(int Row, int Col)> MaxWeightAssignment(double[][] weights)
    {
        var rows = weights.Length;
        var cols = rows == 0 ? 0 : weights[0].Length;
        if (rows == 0 || cols == 0)
            yield break;

        var n = Math.Max(rows, cols);

        // cost[i][j] = -weight, padded cells cost 0 (weight 0).
        var cost = new double[n, n];
        for (var i = 0; i < n; i++)
            for (var j = 0; j < n; j++)
                cost[i, j] = i < rows && j < cols ? -weights[i][j] : 0.0;

        // Kuhn–Munkres with potentials (u, v) and column matching (p, way).
        const double inf = double.PositiveInfinity;
        var u = new double[n + 1];
        var v = new double[n + 1];
        var p = new int[n + 1]; // p[j] = row assigned to column j (1-based rows), 0 = none
        var way = new int[n + 1];

        for (var i = 1; i <= n; i++)
        {
            p[0] = i;
            var j0 = 0;
            var minv = new double[n + 1];
            var used = new bool[n + 1];
            for (var j = 0; j <= n; j++)
                minv[j] = inf;

            do
            {
                used[j0] = true;
                var i0 = p[j0];
                var delta = inf;
                var j1 = -1;

                for (var j = 1; j <= n; j++)
                {
                    if (used[j])
                        continue;
                    var cur = cost[i0 - 1, j - 1] - u[i0] - v[j];
                    if (cur < minv[j])
                    {
                        minv[j] = cur;
                        way[j] = j0;
                    }
                    if (minv[j] < delta)
                    {
                        delta = minv[j];
                        j1 = j;
                    }
                }

                for (var j = 0; j <= n; j++)
                {
                    if (used[j])
                    {
                        u[p[j]] += delta;
                        v[j] -= delta;
                    }
                    else
                    {
                        minv[j] -= delta;
                    }
                }

                j0 = j1;
            }
            while (p[j0] != 0);

            do
            {
                var j1 = way[j0];
                p[j0] = p[j1];
                j0 = j1;
            }
            while (j0 != 0);
        }

        for (var j = 1; j <= n; j++)
        {
            var row = p[j] - 1;
            var col = j - 1;
            if (row >= 0 && row < rows && col < cols)
                yield return (row, col);
        }
    }
}
