using TerraformApi.Domain.Models;

namespace TerraformApi.Domain.Interfaces;

/// <summary>
/// Parses a Terraform APIM configuration and extracts a structured operations list
/// using the same unified format as the OpenAPI parser, enabling side-by-side comparison.
/// </summary>
public interface ITerraformOperationsParser
{
    /// <summary>
    /// Parses Terraform HCL text and returns all API operations using the unified format.
    /// </summary>
    OperationsListResult Parse(string terraform);
}
