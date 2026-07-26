namespace TerraformMerge.Engine;

/// <summary>
/// The distinct value each editable field takes across <b>both sides</b> of a
/// merge (Original and Target). The editor uses these as the drop-down choices
/// for every field, so a user can accept a value from either side — e.g. switch
/// an operation's resource group from <c>rg-apim-staging</c> to
/// <c>rg-apim-dev</c> because both appear in the loaded files.
/// </summary>
public sealed class MergeFieldCatalog
{
    private readonly IReadOnlyDictionary<OperationField, IReadOnlyList<string>> _values;

    private MergeFieldCatalog(IReadOnlyDictionary<OperationField, IReadOnlyList<string>> values) =>
        _values = values;

    /// <summary>
    /// Collects, per field, the distinct non-empty values found across every
    /// operation on both sides. Values are ordinal-sorted for a stable ordering.
    /// </summary>
    public static MergeFieldCatalog Build(
        IEnumerable<OperationNode> left,
        IEnumerable<OperationNode> right)
    {
        var all = left.Concat(right).ToList();
        var values = new Dictionary<OperationField, IReadOnlyList<string>>();

        foreach (var info in OperationFieldInfo.All)
        {
            values[info.Field] = all
                .Select(info.Get)
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(v => v, StringComparer.Ordinal)
                .ToList();
        }

        return new MergeFieldCatalog(values);
    }

    /// <summary>The distinct values seen for <paramref name="field"/> across both sides.</summary>
    public IReadOnlyList<string> Values(OperationField field) =>
        _values.TryGetValue(field, out var list) ? list : [];
}
