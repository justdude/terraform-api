using System.ComponentModel;
using System.Text;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Server;
using TerraformApi.Domain.Models;

namespace TerraformApi.Mcp.Tools;

/// <summary>
/// MCP tool that lists available APIM environment presets from configuration.
/// Uses IOptions to read from appsettings.json through the standard configuration system,
/// consistent with how the API endpoint reads the same data.
/// </summary>
[McpServerToolType]
public static class EnvironmentsTool
{
    [McpServerTool(Name = "list_environment_presets")]
    [Description("Lists all available APIM environment presets configured in appsettings.json. " +
                 "Each preset contains pre-configured values for resource group name, APIM instance name, " +
                 "API gateway host, CORS settings, and other APIM configuration. Use these presets to " +
                 "auto-fill parameters when calling the convert or update tools.")]
    public static string ListEnvironments(
        IOptions<Dictionary<string, ApimEnvironmentConfig>> options,
        [Description("Optional: name of a specific environment to retrieve (e.g. 'dev', 'staging', 'prod'). " +
                     "If omitted, all environments are listed.")] string? environmentName = null)
    {
        return FormatEnvironments(options.Value, environmentName);
    }

    /// <summary>
    /// Core formatting logic. Separated from the MCP entry point for testability.
    /// </summary>
    internal static string FormatEnvironments(Dictionary<string, ApimEnvironmentConfig> environments, string? environmentName = null)
    {
        if (environments.Count == 0)
        {
            return "No environment presets configured.\n\n" +
                   "To use environment presets, add an 'ApimEnvironments' section to appsettings.json.";
        }

        if (environmentName is not null)
        {
            if (environments.TryGetValue(environmentName, out var env))
            {
                return $"Environment: {environmentName}\n\n{FormatSingleEnvironment(environmentName, env)}";
            }

            return $"Environment '{environmentName}' not found.\n" +
                   $"Available environments: {string.Join(", ", environments.Keys)}";
        }

        var sb = new StringBuilder();
        sb.AppendLine("Available APIM Environment Presets:");
        sb.AppendLine(new string('=', 50));

        foreach (var (name, config) in environments)
        {
            sb.AppendLine();
            sb.AppendLine(FormatSingleEnvironment(name, config));
            sb.AppendLine(new string('-', 50));
        }

        return sb.ToString();
    }

    /// <summary>
    /// Formats a single environment preset into a human-readable block.
    /// </summary>
    private static string FormatSingleEnvironment(string name, ApimEnvironmentConfig config)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"[{name}]");

        if (config.StageGroupName is not null)
            sb.AppendLine($"  StageGroupName: {config.StageGroupName}");
        if (config.ApimName is not null)
            sb.AppendLine($"  ApimName: {config.ApimName}");
        if (config.ApiGatewayHost is not null)
            sb.AppendLine($"  ApiGatewayHost: {config.ApiGatewayHost}");
        if (config.FrontendHost is not null)
            sb.AppendLine($"  FrontendHost: {config.FrontendHost}");
        if (config.CompanyDomain is not null)
            sb.AppendLine($"  CompanyDomain: {config.CompanyDomain}");
        if (config.LocalDevHost is not null)
            sb.AppendLine($"  LocalDevHost: {config.LocalDevHost}");
        if (config.LocalDevPort is not null)
            sb.AppendLine($"  LocalDevPort: {config.LocalDevPort}");

        sb.AppendLine($"  SubscriptionRequired: {config.SubscriptionRequired.ToString().ToLowerInvariant()}");
        sb.AppendLine($"  IncludeCorsPolicy: {config.IncludeCorsPolicy.ToString().ToLowerInvariant()}");

        if (config.AllowedMethods.Count > 0)
            sb.AppendLine($"  AllowedMethods: {string.Join(", ", config.AllowedMethods)}");

        return sb.ToString();
    }
}
