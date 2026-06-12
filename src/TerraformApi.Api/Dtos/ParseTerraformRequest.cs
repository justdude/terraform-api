namespace TerraformApi.Api.Dtos;

/// <summary>
/// Request DTO for parsing Terraform APIM configuration to extract operations.
/// </summary>
public sealed record ParseTerraformRequest
{
    /// <summary>
    /// The Terraform APIM configuration HCL text to parse.
    /// </summary>
    public required string Terraform { get; init; }
}
