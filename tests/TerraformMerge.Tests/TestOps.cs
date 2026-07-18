using TerraformMerge.Engine;

namespace TerraformMerge.Tests;

/// <summary>Convenience factory for building operation nodes in tests.</summary>
internal static class TestOps
{
    public static OperationNode Op(
        string method,
        string url,
        string id = "",
        OperationSource source = OperationSource.TargetTerraform,
        params string[] parameterKeys) => new()
    {
        Source = source,
        Method = method.ToUpperInvariant(),
        UrlTemplate = url,
        OperationId = id,
        ParameterKeys = parameterKeys.OrderBy(k => k, StringComparer.Ordinal).ToList()
    };
}
