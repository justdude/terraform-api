using System.Text.RegularExpressions;

namespace TerraformApi.Domain.Validation;

/// <summary>
/// Azure APIM naming constraints per Microsoft documentation:
/// https://learn.microsoft.com/en-us/azure/api-management/azure-apim-apis-overview
/// https://learn.microsoft.com/en-us/rest/api/apimanagement/
/// </summary>
public static partial class ApimNamingRules
{
    // API name: 1-256 chars, alphanumeric + hyphens, must start/end with alphanumeric
    public const int ApiNameMaxLength = 256;
    public const int ApiNameMinLength = 1;

    // Operation ID: 1-80 chars, alphanumeric + hyphens + underscores
    public const int OperationIdMaxLength = 80;
    public const int OperationIdMinLength = 1;

    // Display name: 1-300 chars, most printable characters allowed
    public const int DisplayNameMaxLength = 300;
    public const int DisplayNameMinLength = 1;

    // API path: 0-400 chars, relative URL
    public const int ApiPathMaxLength = 400;

    // Resource group name: 1-90 chars
    public const int ResourceGroupNameMaxLength = 90;
    public const int ResourceGroupNameMinLength = 1;

    // APIM service name: 1-50 chars, alphanumeric + hyphens
    public const int ApimServiceNameMaxLength = 50;

    [GeneratedRegex(@"^[a-zA-Z0-9][a-zA-Z0-9\-]*[a-zA-Z0-9]$|^[a-zA-Z0-9]$")]
    public static partial Regex ApiNamePattern();

    [GeneratedRegex(@"^[a-zA-Z0-9][a-zA-Z0-9\-_]*[a-zA-Z0-9]$|^[a-zA-Z0-9]$")]
    public static partial Regex OperationIdPattern();

    [GeneratedRegex(@"^[^*#&+:<>?]+$")]
    public static partial Regex DisplayNamePattern();

    [GeneratedRegex(@"^[a-zA-Z0-9\-._/]*$")]
    public static partial Regex ApiPathPattern();

    [GeneratedRegex(@"^[a-zA-Z0-9\-._()]+$")]
    public static partial Regex ResourceGroupNamePattern();

    [GeneratedRegex(@"[^a-zA-Z0-9\-]")]
    public static partial Regex NonApiNameChars();

    [GeneratedRegex(@"[^a-zA-Z0-9\-_]")]
    public static partial Regex NonOperationIdChars();

    [GeneratedRegex(@"[^a-zA-Z0-9\-._/]")]
    public static partial Regex NonApiPathChars();

    [GeneratedRegex(@"-{2,}")]
    public static partial Regex ConsecutiveHyphens();
}

public sealed class NamingValidationResult
{
    public bool IsValid { get; init; }
    public List<string> Errors { get; init; } = [];

    public static NamingValidationResult Valid() => new() { IsValid = true };

    public static NamingValidationResult Invalid(params string[] errors) =>
        new() { IsValid = false, Errors = [.. errors] };
}
