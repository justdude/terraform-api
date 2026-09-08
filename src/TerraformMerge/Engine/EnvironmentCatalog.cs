namespace TerraformMerge.Engine;

/// <summary>
/// The environment-specific values one environment actually uses in the loaded
/// documents — the resource group, APIM instance and api name seen on that
/// environment's operations. A field is filled only when every operation of the
/// environment agrees on it: with two api groups naming their apis differently
/// there is no single right answer, so the field stays null and the retarget
/// falls back to rewriting the environment token of the operation's own value.
/// </summary>
public sealed record EnvironmentProfile(string Name)
{
    public string? ResourceGroup { get; init; }
    public string? ApimName { get; init; }
    public string? ApiName { get; init; }

    /// <summary>The profile value for one identifying field, or null when the environment has none.</summary>
    public string? Value(OperationField field) => field switch
    {
        OperationField.ApimResourceGroupName => ResourceGroup,
        OperationField.ApimName => ApimName,
        OperationField.ApiName => ApiName,
        _ => null
    };
}

/// <summary>
/// Which environments the loaded documents hold, and what each one's identifying
/// fields look like. Built across <b>both sides</b> of a merge, so an operation
/// read from the dev file can be re-stamped with the qa file's own resource
/// group / APIM / api names rather than a guessed rename.
/// </summary>
public sealed class EnvironmentCatalog
{
    /// <summary>The fields an environment profile carries (all identifying, none per-operation).</summary>
    public static IReadOnlyList<OperationField> ProfileFields { get; } =
        [OperationField.ApimResourceGroupName, OperationField.ApimName, OperationField.ApiName];

    private readonly Dictionary<string, EnvironmentProfile> _profiles;

    private EnvironmentCatalog(Dictionary<string, EnvironmentProfile> profiles)
    {
        _profiles = profiles;
        Names = profiles.Keys.OrderBy(n => n, StringComparer.Ordinal).ToList();
    }

    /// <summary>Environments found in the loaded documents, ordered for display.</summary>
    public IReadOnlyList<string> Names { get; }

    public static EnvironmentCatalog Empty { get; } = new([]);

    /// <summary>Collects the environments used across both sides of the merge.</summary>
    public static EnvironmentCatalog Build(
        IEnumerable<OperationNode> left,
        IEnumerable<OperationNode> right)
    {
        var byEnvironment = new Dictionary<string, List<OperationNode>>(StringComparer.OrdinalIgnoreCase);

        foreach (var node in left.Concat(right))
        {
            var environment = Detect(node);
            if (environment is null)
                continue;

            if (!byEnvironment.TryGetValue(environment, out var bucket))
                byEnvironment[environment] = bucket = [];
            bucket.Add(node);
        }

        var profiles = new Dictionary<string, EnvironmentProfile>(StringComparer.OrdinalIgnoreCase);
        foreach (var (environment, nodes) in byEnvironment)
        {
            profiles[environment] = new EnvironmentProfile(environment)
            {
                ResourceGroup = Agreed(nodes, OperationField.ApimResourceGroupName),
                ApimName = Agreed(nodes, OperationField.ApimName),
                ApiName = Agreed(nodes, OperationField.ApiName)
            };
        }

        return new EnvironmentCatalog(profiles);
    }

    /// <summary>The profile for <paramref name="environment"/>, or null when the documents hold none.</summary>
    public EnvironmentProfile? Profile(string? environment) =>
        environment is not null && _profiles.TryGetValue(environment, out var profile) ? profile : null;

    /// <summary>
    /// The environments offered in the pickers: the ones actually loaded first,
    /// then the remaining well-known names so an operation can be moved to an
    /// environment no loaded file uses yet (an empty qa config, say).
    /// </summary>
    public IReadOnlyList<string> Choices() =>
    [
        .. Names,
        .. EnvironmentToken.Known
            .Where(known => !_profiles.ContainsKey(known))
            .OrderBy(known => known, StringComparer.Ordinal)
    ];

    /// <summary>
    /// The environment an operation belongs to, read from its identifying fields
    /// in order of reliability. Display name and description are deliberately
    /// not consulted — "Get test results" is not a test-environment operation.
    /// </summary>
    public static string? Detect(OperationNode node) =>
        EnvironmentToken.Find(node.ApimResourceGroupName)
        ?? EnvironmentToken.Find(node.ApimName)
        ?? EnvironmentToken.Find(node.ApiName)
        ?? EnvironmentToken.Find(node.OperationId);

    /// <summary>
    /// The environment most of <paramref name="nodes"/> belong to — the
    /// environment of a whole document/pane. Ties break alphabetically so the
    /// answer is stable. Null when no operation carries an environment.
    /// </summary>
    public static string? Dominant(IEnumerable<OperationNode> nodes) =>
        nodes.Select(Detect)
            .Where(environment => environment is not null)
            .GroupBy(environment => environment!, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => group.Key)
            .FirstOrDefault();

    /// <summary>
    /// The one value the environment's operations agree on for a field, or null
    /// when they disagree, none has it, or it is an interpolation (which is
    /// environment-neutral — Terraform resolves it per workspace).
    /// </summary>
    private static string? Agreed(List<OperationNode> nodes, OperationField field)
    {
        var info = OperationFieldInfo.For(field);
        var distinct = nodes
            .Select(info.Get)
            .Where(value => !string.IsNullOrWhiteSpace(value) && !value.Contains("${", StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .Take(2)
            .ToList();

        return distinct.Count == 1 ? distinct[0] : null;
    }
}
