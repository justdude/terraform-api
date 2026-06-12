namespace TerraformApi.Api.Dtos;

/// <summary>
/// Unified response DTO for operations listing — used by both
/// POST /api/fetch-operations and POST /api/parse-terraform-operations.
/// Ensures identical JSON shape regardless of source (OpenAPI or Terraform).
/// </summary>
public sealed record OperationsResponse
{
    public bool Success { get; init; }
    public string? Error { get; init; }
    public OperationsApiInfoDto? Api { get; init; }
    public int TotalOperations { get; init; }
    public List<OperationDto> Operations { get; init; } = [];
}

/// <summary>
/// Unified API-level metadata — superset of OpenAPI and Terraform fields.
/// </summary>
public sealed class OperationsApiInfoDto
{
    public string Title { get; init; } = "";
    public string? Version { get; init; }
    public string? Description { get; init; }
    public string? SourceUrl { get; init; }
    public string? Name { get; init; }
    public string? Path { get; init; }
    public string? ServiceUrl { get; init; }
    public string? Environment { get; init; }
    public string Source { get; init; } = "openapi";
}

/// <summary>
/// A single operation — same fields for both OpenAPI and Terraform sources.
/// </summary>
public sealed class OperationDto
{
    public string Method { get; init; } = "";
    public string UrlTemplate { get; init; } = "";
    public string Path { get; init; } = "";
    public string? OperationId { get; init; }
    public string? Description { get; init; }
    public List<string>? Tags { get; init; }
    public List<ParameterDto>? Parameters { get; init; }
    public List<string>? RequestBodyContentTypes { get; init; }
    public List<int>? ResponseCodes { get; init; }
}

/// <summary>
/// A parameter (path, query, header) — same for both sources.
/// </summary>
public sealed class ParameterDto
{
    public string Name { get; init; } = "";
    public string In { get; init; } = "";
    public string Type { get; init; } = "string";
    public bool Required { get; init; }
    public string? Description { get; init; }
}
