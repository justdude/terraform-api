using TerraformApi.Domain.Models;

namespace TerraformApi.Domain.Interfaces;

/// <summary>
/// Parses an OpenAPI JSON specification and returns a structured operations list.
/// Used by both the MCP tool and the API endpoint for consistent output.
/// </summary>
public interface IOpenApiOperationsFetcher
{
    /// <summary>
    /// Parses OpenAPI JSON and returns the operations list using the unified format.
    /// </summary>
    OperationsListResult ParseOperations(string openApiJson, string sourceUrl = "inline");
}
