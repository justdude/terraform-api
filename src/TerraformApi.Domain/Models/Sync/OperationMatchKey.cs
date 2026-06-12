namespace TerraformApi.Domain.Models.Sync;

/// <summary>
/// Which fingerprint fields are compared, in what combination.
/// </summary>
public enum OperationMatchKey
{
    OperationId,
    MethodAndUrl,
    MethodAndUrlAndParams,
    Tag,
    ApiAndMethodAndUrl,

    /// <summary>resource group + api + method + url — the strictest scope.</summary>
    RgApiAndMethodAndUrl,

    Custom
}
