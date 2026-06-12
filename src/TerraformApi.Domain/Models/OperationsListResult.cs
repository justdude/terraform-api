using System.Text.Json.Serialization;

namespace TerraformApi.Domain.Models;

/// <summary>
/// Unified result type for both OpenAPI and Terraform operations parsing.
/// Both parsers return this same type so output shapes are identical
/// and consumers can compare side-by-side without format-specific handling.
/// Fields not available from a given source are null.
/// </summary>
public sealed class OperationsListResult
{
    public bool Success { get; init; }
    public string? Error { get; init; }
    public OperationsApiInfo? Api { get; init; }
    public int TotalOperations { get; init; }
    public List<OperationInfo> Operations { get; init; } = [];
}

/// <summary>
/// Unified API-level metadata from either an OpenAPI spec or a Terraform config.
/// All fields are always serialized (including nulls) so both sources produce
/// identical JSON key structures for reliable side-by-side comparison.
/// </summary>
public sealed class OperationsApiInfo
{
    /// <summary>API title (from OpenAPI info.title or Terraform display_name).</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string Title { get; init; } = "";

    /// <summary>API version (from OpenAPI info.version). Null for Terraform sources.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string? Version { get; init; }

    /// <summary>API description (from OpenAPI info.description).</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string? Description { get; init; }

    /// <summary>URL the spec was fetched from. Null for Terraform sources.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string? SourceUrl { get; init; }

    /// <summary>Terraform resource name (e.g. "my-api-dev"). Null for OpenAPI sources.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string? Name { get; init; }

    /// <summary>API path prefix (from Terraform "path" field). Null for OpenAPI sources.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string? Path { get; init; }

    /// <summary>Backend service URL (from Terraform "service_url"). Null for OpenAPI sources.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string? ServiceUrl { get; init; }

    /// <summary>Detected environment (e.g. "dev", "prod"). Null for OpenAPI sources.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string? Environment { get; init; }

    /// <summary>Source type: "openapi" or "terraform".</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string Source { get; init; } = "openapi";
}

/// <summary>
/// A single API operation from either an OpenAPI spec or a Terraform config.
/// All fields are present in both output types; unused fields are null.
/// </summary>
public sealed class OperationInfo
{
    public string Method { get; init; } = "";
    public string UrlTemplate { get; init; } = "";
    public string Path { get; init; } = "";
    public string? OperationId { get; init; }
    public string? Description { get; init; }

    /// <summary>Tags from OpenAPI. Null for Terraform sources. Always serialized for format consistency.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public List<string>? Tags { get; init; }

    public List<ParameterInfo>? Parameters { get; init; }
    public List<string>? RequestBodyContentTypes { get; init; }
    public List<int>? ResponseCodes { get; init; }
}

/// <summary>
/// A parameter (path, query, header) from either source.
/// </summary>
public sealed class ParameterInfo
{
    public string Name { get; init; } = "";
    public string In { get; init; } = "";
    public string Type { get; init; } = "string";
    public bool Required { get; init; }
    public string? Description { get; init; }
}
