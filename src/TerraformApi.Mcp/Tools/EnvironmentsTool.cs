using System.ComponentModel;
using System.Text;
using System.Text.Json;
using ModelContextProtocol.Server;

namespace TerraformApi.Mcp.Tools;

/// <summary>
/// MCP tool that lists available APIM environment presets loaded from appsettings.json.
/// Provides auto-fill values for resource group names, APIM instance names, gateway hosts, etc.
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
        [Description("Optional: name of a specific environment to retrieve (e.g. 'dev', 'staging', 'prod'). " +
                     "If omitted, all environments are listed.")] string? environmentName = null)
    {
        return ListEnvironmentsFromPath(FindAppsettingsPath(), environmentName);
    }

    /// <summary>
    /// Core implementation that reads environment presets from a given config file path.
    /// Separated from the MCP entry point so tests can provide a custom path.
    /// </summary>
    internal static string ListEnvironmentsFromPath(string? configPath, string? environmentName = null)
    {
        try
        {
            if (configPath == null)
            {
                return "No appsettings.json found. Environment presets are not configured.\n\n" +
                       "To use environment presets, create an appsettings.json with an 'ApimEnvironments' section " +
                       "in the same directory as the MCP server executable.";
            }

            var json = File.ReadAllText(configPath);
            using var doc = JsonDocument.Parse(json);

            if (!doc.RootElement.TryGetProperty("ApimEnvironments", out var envSection))
            {
                return "No 'ApimEnvironments' section found in appsettings.json.\n\n" +
                       "Add an 'ApimEnvironments' section with named environment presets.";
            }

            if (environmentName is not null)
            {
                if (envSection.TryGetProperty(environmentName, out var env))
                {
                    return $"Environment: {environmentName}\n\n{FormatEnvironment(environmentName, env)}";
                }

                var available = envSection.EnumerateObject().Select(p => p.Name);
                return $"Environment '{environmentName}' not found.\n" +
                       $"Available environments: {string.Join(", ", available)}";
            }

            var sb = new StringBuilder();
            sb.AppendLine("Available APIM Environment Presets:");
            sb.AppendLine(new string('=', 50));

            foreach (var prop in envSection.EnumerateObject())
            {
                sb.AppendLine();
                sb.AppendLine(FormatEnvironment(prop.Name, prop.Value));
                sb.AppendLine(new string('-', 50));
            }

            return sb.ToString();
        }
        catch (Exception ex)
        {
            return $"Failed to load environment presets: {ex.Message}";
        }
    }

    /// <summary>
    /// Searches for appsettings.json in the executable directory and parent directories.
    /// </summary>
    internal static string? FindAppsettingsPath()
    {
        // Check alongside the MCP server executable
        var exeDir = AppContext.BaseDirectory;
        var path = Path.Combine(exeDir, "appsettings.json");
        if (File.Exists(path)) return path;

        // Walk up from current directory to find a project root with appsettings.json
        var dir = Directory.GetCurrentDirectory();
        for (var i = 0; i < 5; i++)
        {
            path = Path.Combine(dir, "appsettings.json");
            if (File.Exists(path)) return path;

            // Also check in src/TerraformApi.Api/ (the web project)
            path = Path.Combine(dir, "src", "TerraformApi.Api", "appsettings.json");
            if (File.Exists(path)) return path;

            var parent = Directory.GetParent(dir);
            if (parent == null) break;
            dir = parent.FullName;
        }

        return null;
    }

    /// <summary>
    /// Formats a single environment preset into a human-readable block.
    /// </summary>
    private static string FormatEnvironment(string name, JsonElement env)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"[{name}]");

        foreach (var prop in env.EnumerateObject())
        {
            var value = prop.Value.ValueKind switch
            {
                JsonValueKind.String => prop.Value.GetString(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                JsonValueKind.Number => prop.Value.GetRawText(),
                JsonValueKind.Array => string.Join(", ", prop.Value.EnumerateArray().Select(e => e.GetString())),
                _ => prop.Value.GetRawText()
            };

            sb.AppendLine($"  {prop.Name}: {value}");
        }

        return sb.ToString();
    }
}
