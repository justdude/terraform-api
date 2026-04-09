namespace TerraformApi.Api.Dtos;

public sealed record ConvertResponse
{
    public bool Success { get; init; }
    public string TerraformConfig { get; init; } = "";
    public List<string> Warnings { get; init; } = [];
    public List<string> Errors { get; init; } = [];
    public ApiSummary? Summary { get; init; }
}

public sealed class ApiSummary
{
    public string ApiName { get; init; } = "";
    public string DisplayName { get; init; } = "";
    public string Path { get; init; } = "";
    public int OperationCount { get; init; }
    public List<OperationSummary> Operations { get; init; } = [];
}

public sealed class OperationSummary
{
    public string OperationId { get; init; } = "";
    public string Method { get; init; } = "";
    public string UrlTemplate { get; init; } = "";
}

public sealed class ValidateResponse
{
    public bool IsValid { get; init; }
    public List<string> Errors { get; init; } = [];
    public ApiSummary? Summary { get; init; }
}
