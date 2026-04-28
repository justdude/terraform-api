using System.ComponentModel;
using System.Text;
using Microsoft.OpenApi.Readers;
using ModelContextProtocol.Server;
using TerraformApi.Domain.Interfaces;

namespace TerraformApi.Mcp.Tools;

/// <summary>
/// MCP tool that validates an OpenAPI specification against Azure APIM naming rules
/// without generating Terraform output.
/// </summary>
[McpServerToolType]
public static class ValidateTool
{
    [McpServerTool(Name = "validate_openapi_for_apim")]
    [Description("Validates an OpenAPI JSON specification against Azure APIM naming rules without generating Terraform. " +
                 "Checks operation IDs, display names, API paths, and resource group names for compliance. " +
                 "Returns detected operations and any naming rule violations.")]
    public static string Validate(
        IApimNamingValidator validator,
        [Description("The OpenAPI specification JSON string to validate (OpenAPI 3.x format)")] string openApiJson,
        [Description("Target environment name for operation ID generation (e.g. 'dev')")] string environment = "dev")
    {
        var sb = new StringBuilder();
        var errors = new List<string>();

        try
        {
            var reader = new OpenApiStringReader();
            var doc = reader.Read(openApiJson, out var diagnostic);

            if (diagnostic.Errors.Count > 0)
            {
                errors.AddRange(diagnostic.Errors.Select(e => e.Message));
            }

            if (doc?.Paths == null)
            {
                sb.AppendLine("VALIDATION FAILED");
                sb.AppendLine();
                sb.AppendLine("Errors:");
                if (errors.Count > 0)
                {
                    foreach (var error in errors)
                        sb.AppendLine($"  - {error}");
                }
                else
                {
                    sb.AppendLine("  - Could not parse the OpenAPI document. No paths found.");
                }
                return sb.ToString();
            }

            sb.AppendLine($"API Title: {doc.Info?.Title ?? "Unknown"}");
            sb.AppendLine($"API Version: {doc.Info?.Version ?? "Unknown"}");
            sb.AppendLine();

            var operations = new List<(string Method, string Path, string OperationId, bool IsValid)>();

            foreach (var path in doc.Paths)
            {
                foreach (var op in path.Value.Operations)
                {
                    var rawOpId = op.Value.OperationId ?? $"{op.Key}-{path.Key}";
                    var sanitized = validator.SanitizeOperationId(rawOpId);
                    var sanitizedWithEnv = $"{sanitized}-{environment}";
                    var result = validator.ValidateOperationId(sanitizedWithEnv);

                    if (!result.IsValid)
                        errors.AddRange(result.Errors);

                    var displayName = op.Value.Summary ?? op.Value.OperationId ?? path.Key;
                    var displayResult = validator.ValidateDisplayName(displayName);

                    if (!displayResult.IsValid)
                        errors.AddRange(displayResult.Errors);

                    operations.Add((
                        op.Key.ToString().ToUpperInvariant(),
                        path.Key,
                        sanitizedWithEnv,
                        result.IsValid && displayResult.IsValid
                    ));
                }
            }

            sb.AppendLine($"Operations ({operations.Count}):");
            foreach (var (method, path, opId, isValid) in operations)
            {
                var status = isValid ? "OK" : "INVALID";
                sb.AppendLine($"  [{status}] {method,-8} {path,-40} -> {opId}");
            }

            sb.AppendLine();
            if (errors.Count == 0)
            {
                sb.AppendLine("Result: VALID - All naming rules pass.");
            }
            else
            {
                sb.AppendLine($"Result: INVALID - {errors.Count} issue(s) found:");
                foreach (var error in errors)
                    sb.AppendLine($"  - {error}");
            }

            return sb.ToString();
        }
        catch (Exception ex)
        {
            return $"Validation failed: could not parse the provided OpenAPI document.\nDetails: {ex.Message}";
        }
    }
}
