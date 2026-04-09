namespace TerraformApi.Domain.Models;

public sealed class ConversionResult
{
    public bool Success { get; init; }
    public string TerraformConfig { get; init; } = "";
    public ApimConfiguration? Configuration { get; init; }
    public List<string> Warnings { get; init; } = [];
    public List<string> Errors { get; init; } = [];
}
