using TerraformApi.Domain.Interfaces;
using TerraformApi.Domain.Validation;

namespace TerraformApi.Application.Services;

/// <summary>
/// Validates and sanitises Azure APIM resource names against Microsoft's documented
/// naming constraints. All length limits and character rules are sourced from
/// <see cref="ApimNamingRules"/> so they can be updated in a single place.
/// </summary>
public sealed class ApimNamingValidatorService : IApimNamingValidator
{
    /// <summary>
    /// Validates an APIM API name: 1–256 alphanumeric characters and hyphens,
    /// starting and ending with an alphanumeric character.
    /// </summary>
    public NamingValidationResult ValidateApiName(string name)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(name))
        {
            return NamingValidationResult.Invalid("API name is required.");
        }

        if (name.Length < ApimNamingRules.ApiNameMinLength || name.Length > ApimNamingRules.ApiNameMaxLength)
        {
            errors.Add($"API name must be between {ApimNamingRules.ApiNameMinLength} and {ApimNamingRules.ApiNameMaxLength} characters. Current length: {name.Length}.");
        }

        if (!ApimNamingRules.ApiNamePattern().IsMatch(name))
        {
            errors.Add("API name must contain only alphanumeric characters and hyphens, and must start and end with an alphanumeric character.");
        }

        return errors.Count == 0 ? NamingValidationResult.Valid() : NamingValidationResult.Invalid([.. errors]);
    }

    /// <summary>
    /// Validates an APIM operation ID: 1–80 alphanumeric characters, hyphens, and
    /// underscores, starting and ending with an alphanumeric character.
    /// </summary>
    public NamingValidationResult ValidateOperationId(string operationId)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(operationId))
        {
            return NamingValidationResult.Invalid("Operation ID is required.");
        }

        if (operationId.Length < ApimNamingRules.OperationIdMinLength || operationId.Length > ApimNamingRules.OperationIdMaxLength)
        {
            errors.Add($"Operation ID must be between {ApimNamingRules.OperationIdMinLength} and {ApimNamingRules.OperationIdMaxLength} characters. Current length: {operationId.Length}.");
        }

        if (!ApimNamingRules.OperationIdPattern().IsMatch(operationId))
        {
            errors.Add("Operation ID must contain only alphanumeric characters, hyphens, and underscores, and must start and end with an alphanumeric character.");
        }

        return errors.Count == 0 ? NamingValidationResult.Valid() : NamingValidationResult.Invalid([.. errors]);
    }

    /// <summary>
    /// Validates an APIM display name: 1–300 characters with no <c>* # &amp; + : &lt; &gt; ?</c>.
    /// </summary>
    public NamingValidationResult ValidateDisplayName(string displayName)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(displayName))
        {
            return NamingValidationResult.Invalid("Display name is required.");
        }

        if (displayName.Length < ApimNamingRules.DisplayNameMinLength || displayName.Length > ApimNamingRules.DisplayNameMaxLength)
        {
            errors.Add($"Display name must be between {ApimNamingRules.DisplayNameMinLength} and {ApimNamingRules.DisplayNameMaxLength} characters. Current length: {displayName.Length}.");
        }

        if (!ApimNamingRules.DisplayNamePattern().IsMatch(displayName))
        {
            errors.Add("Display name must not contain characters: * # & + : < > ?");
        }

        return errors.Count == 0 ? NamingValidationResult.Valid() : NamingValidationResult.Invalid([.. errors]);
    }

    /// <summary>
    /// Validates an APIM API path: 0–400 characters of alphanumeric, hyphens, dots,
    /// underscores, and forward slashes.
    /// </summary>
    public NamingValidationResult ValidateApiPath(string path)
    {
        var errors = new List<string>();

        if (path.Length > ApimNamingRules.ApiPathMaxLength)
        {
            errors.Add($"API path must not exceed {ApimNamingRules.ApiPathMaxLength} characters. Current length: {path.Length}.");
        }

        if (!ApimNamingRules.ApiPathPattern().IsMatch(path))
        {
            errors.Add("API path must contain only alphanumeric characters, hyphens, dots, underscores, and forward slashes.");
        }

        return errors.Count == 0 ? NamingValidationResult.Valid() : NamingValidationResult.Invalid([.. errors]);
    }

    /// <summary>
    /// Validates an Azure resource group name: 1–90 alphanumeric characters, hyphens,
    /// underscores, periods, and parentheses.
    /// </summary>
    public NamingValidationResult ValidateResourceGroupName(string name)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(name))
        {
            return NamingValidationResult.Invalid("Resource group name is required.");
        }

        if (name.Length < ApimNamingRules.ResourceGroupNameMinLength || name.Length > ApimNamingRules.ResourceGroupNameMaxLength)
        {
            errors.Add($"Resource group name must be between {ApimNamingRules.ResourceGroupNameMinLength} and {ApimNamingRules.ResourceGroupNameMaxLength} characters.");
        }

        if (!ApimNamingRules.ResourceGroupNamePattern().IsMatch(name))
        {
            errors.Add("Resource group name must contain only alphanumeric characters, hyphens, underscores, periods, and parentheses.");
        }

        return errors.Count == 0 ? NamingValidationResult.Valid() : NamingValidationResult.Invalid([.. errors]);
    }

    /// <summary>
    /// Converts arbitrary text into a valid APIM API name by replacing invalid
    /// characters with hyphens, collapsing consecutive hyphens, stripping leading/
    /// trailing hyphens, lower-casing, and truncating to the maximum length.
    /// Returns <c>"api"</c> when the input produces an empty result.
    /// </summary>
    public string SanitizeApiName(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return "api";

        var sanitized = ApimNamingRules.NonApiNameChars().Replace(input.Trim(), "-");
        sanitized = ApimNamingRules.ConsecutiveHyphens().Replace(sanitized, "-");
        sanitized = sanitized.Trim('-');

        if (sanitized.Length == 0) return "api";
        if (sanitized.Length > ApimNamingRules.ApiNameMaxLength)
            sanitized = sanitized[..ApimNamingRules.ApiNameMaxLength].TrimEnd('-');

        return sanitized.ToLowerInvariant();
    }

    /// <summary>
    /// Converts arbitrary text into a valid APIM operation ID by replacing invalid
    /// characters with hyphens, collapsing consecutive hyphens, truncating to 80
    /// characters, and lower-casing. Returns <c>"operation"</c> when the result is empty.
    /// </summary>
    public string SanitizeOperationId(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return "operation";

        var sanitized = ApimNamingRules.NonOperationIdChars().Replace(input.Trim(), "-");
        sanitized = ApimNamingRules.ConsecutiveHyphens().Replace(sanitized, "-");
        sanitized = sanitized.Trim('-');

        if (sanitized.Length == 0) return "operation";
        if (sanitized.Length > ApimNamingRules.OperationIdMaxLength)
            sanitized = sanitized[..ApimNamingRules.OperationIdMaxLength].TrimEnd('-');

        return sanitized.ToLowerInvariant();
    }

    /// <summary>
    /// Strips characters not allowed in an APIM API path, removes a leading slash,
    /// lower-cases the result, and truncates to 400 characters.
    /// </summary>
    public string SanitizeApiPath(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return "";

        var sanitized = ApimNamingRules.NonApiPathChars().Replace(input.Trim(), "");
        sanitized = sanitized.TrimStart('/');

        if (sanitized.Length > ApimNamingRules.ApiPathMaxLength)
            sanitized = sanitized[..ApimNamingRules.ApiPathMaxLength];

        return sanitized.ToLowerInvariant();
    }
}
