using System.ComponentModel.DataAnnotations;

namespace TerraformApi.Api.Dtos;

/// <summary>
/// Request DTO for fetching and parsing OpenAPI operations from a URL.
/// </summary>
public sealed record FetchOperationsRequest
{
    /// <summary>
    /// URL to the OpenAPI/Swagger JSON endpoint.
    /// </summary>
    [Required]
    public required string OpenApiUrl { get; init; }
}
