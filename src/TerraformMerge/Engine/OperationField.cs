namespace TerraformMerge.Engine;

/// <summary>
/// An editable scalar field of an operation. <c>operation_id</c> is intentionally
/// absent — it identifies the operation and is never merged across sides.
/// </summary>
public enum OperationField
{
    Method,
    UrlTemplate,
    DisplayName,
    Description,
    StatusCode,
    ApiName,
    ApimResourceGroupName,
    ApimName
}

/// <summary>
/// Metadata for one editable field: its display label, its HCL key, and typed
/// accessors onto an <see cref="OperationNode"/>. This is the single source of
/// truth shared by the value catalog, the editor dialog, and the applier, so the
/// three can never disagree about which fields exist or how they map to HCL.
/// </summary>
public sealed record OperationFieldInfo(
    OperationField Field,
    string Label,
    string HclKey,
    Func<OperationNode, string> Get,
    Action<OperationNode, string> Set)
{
    /// <summary>Every editable field, in the order the editor presents them.</summary>
    public static IReadOnlyList<OperationFieldInfo> All { get; } =
    [
        new(OperationField.Method, "Method", "method",
            n => n.Method, (n, v) => n.Method = v.ToUpperInvariant()),
        new(OperationField.UrlTemplate, "URL template", "url_template",
            n => n.UrlTemplate, (n, v) => n.UrlTemplate = v),
        new(OperationField.DisplayName, "Display name", "display_name",
            n => n.DisplayName, (n, v) => n.DisplayName = v),
        new(OperationField.Description, "Description", "description",
            n => n.Description, (n, v) => n.Description = v),
        new(OperationField.StatusCode, "Status code", "status_code",
            n => n.StatusCode?.ToString() ?? "",
            (n, v) => n.StatusCode = int.TryParse(v, out var c) ? c : null),
        new(OperationField.ApiName, "API name", "api_name",
            n => n.ApiName, (n, v) => n.ApiName = v),
        new(OperationField.ApimResourceGroupName, "Resource group", "apim_resource_group_name",
            n => n.ApimResourceGroupName, (n, v) => n.ApimResourceGroupName = v),
        new(OperationField.ApimName, "APIM name", "apim_name",
            n => n.ApimName, (n, v) => n.ApimName = v),
    ];

    private static readonly Dictionary<OperationField, OperationFieldInfo> Index =
        All.ToDictionary(f => f.Field);

    public static OperationFieldInfo For(OperationField field) => Index[field];
}
