namespace TerraformApi.Api.Dtos;

/// <summary>
/// Represents a pre-configured APIM environment preset loaded from appsettings.json.
/// Users can select an environment in the UI to auto-fill APIM settings fields.
/// </summary>
public sealed class ApimEnvironmentConfig
{
    public string? StageGroupName { get; init; }
    public string? ApimName { get; init; }
    public string? ApiGatewayHost { get; init; }
    public string? FrontendHost { get; init; }
    public string? CompanyDomain { get; init; }
    public string? LocalDevHost { get; init; }
    public string? LocalDevPort { get; init; }
    public bool SubscriptionRequired { get; init; }
    public bool IncludeCorsPolicy { get; init; } = true;
    public List<string> AllowedMethods { get; init; } = ["GET", "POST", "PUT", "DELETE", "OPTIONS"];
}
