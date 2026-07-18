using System.Text;
using System.Text.Json;
using System.Text.Encodings.Web;
using System.Text.Json.Nodes;

namespace Apitf.Core;

/// <summary>
/// Generates Terraform JSON (.tf.json) for Azure APIM. Output is built from a node
/// tree and serialized by System.Text.Json — no HCL string templating, no escaping bugs.
/// </summary>
public static class TerraformGenerator
{
    private static readonly JsonSerializerOptions Pretty = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// <summary>Single-environment resource (apim_name + resource_group as variables).</summary>
    public static string GenerateTfJson(SpecModel spec, string apiName, string specFilePath)
    {
        var contentFormat = SpecModel.IsYaml(specFilePath) ? "openapi" : "openapi+json";
        var apiBody = new JsonObject
        {
            ["name"] = apiName,
            ["resource_group_name"] = "${var.resource_group_name}",
            ["api_management_name"] = "${var.apim_name}",
            ["revision"] = "1",
            ["display_name"] = spec.Title,
            ["path"] = apiName,
            ["protocols"] = new JsonArray("https"),
            ["import"] = new JsonObject
            {
                ["content_format"] = contentFormat,
                ["content_value"] = $"${{file(\"{specFilePath.Replace("\\", "/")}\")}}"
            }
        };
        var doc = new JsonObject
        {
            ["resource"] = new JsonObject
            {
                ["azurerm_api_management_api"] = new JsonObject { [apiName] = apiBody }
            }
        };
        return doc.ToJsonString(Pretty) + "\n";
    }

    /// <summary>
    /// Environment-aware Terraform: ONE shared spec file, but per-environment APIM instance,
    /// resource group, subscription, API name and revision. Because the subscription differs
    /// per environment, each env gets its own aliased azurerm provider and its own resource
    /// block (a single for_each cannot pick a provider per instance). All env values come
    /// from a typed `var.environments` map, so secrets/ids stay out of the resource bodies.
    /// </summary>
    public static string GenerateMultiEnvTfJson(SpecModel spec, string apiBaseName,
        string specFilePath, IReadOnlyList<string> environments)
    {
        if (environments.Count == 0)
            return GenerateTfJson(spec, apiBaseName, specFilePath);

        var contentFormat = SpecModel.IsYaml(specFilePath) ? "openapi" : "openapi+json";
        var baseKey = Sanitize(apiBaseName);
        var specRef = $"${{file(\"{specFilePath.Replace("\\", "/")}\")}}";

        // variable "environments" : typed skeleton + per-env default placeholders
        var defaults = new JsonObject();
        foreach (var env in environments)
        {
            defaults[env] = new JsonObject
            {
                ["api_name"] = $"{apiBaseName}-{env}",
                ["apim_name"] = $"apim-{env}",
                ["resource_group"] = $"rg-{env}",
                ["subscription_id"] = "00000000-0000-0000-0000-000000000000",
                ["revision"] = "1"
            };
        }
        var variables = new JsonObject
        {
            ["environments"] = new JsonObject
            {
                ["type"] = "map(object({\n    api_name        = string\n    apim_name       = string\n    resource_group  = string\n    subscription_id = string\n    revision        = string\n  }))",
                ["default"] = defaults
            }
        };

        // one aliased azurerm provider per environment (handles differing subscriptions)
        var providers = new JsonArray();
        foreach (var env in environments)
        {
            providers.Add(new JsonObject
            {
                ["alias"] = env,
                ["subscription_id"] = $"${{var.environments[\"{env}\"].subscription_id}}",
                ["features"] = new JsonObject()
            });
        }

        // one resource per environment; every env imports the SAME shared spec
        var apis = new JsonObject();
        foreach (var env in environments)
        {
            apis[$"{baseKey}_{env}"] = new JsonObject
            {
                ["provider"] = $"azurerm.{env}",
                ["name"] = $"${{var.environments[\"{env}\"].api_name}}",
                ["resource_group_name"] = $"${{var.environments[\"{env}\"].resource_group}}",
                ["api_management_name"] = $"${{var.environments[\"{env}\"].apim_name}}",
                ["revision"] = $"${{var.environments[\"{env}\"].revision}}",
                ["display_name"] = spec.Title,
                ["path"] = apiBaseName,
                ["protocols"] = new JsonArray("https"),
                ["import"] = new JsonObject
                {
                    ["content_format"] = contentFormat,
                    ["content_value"] = specRef
                }
            };
        }

        var doc = new JsonObject
        {
            ["variable"] = variables,
            ["provider"] = new JsonObject { ["azurerm"] = providers },
            ["resource"] = new JsonObject { ["azurerm_api_management_api"] = apis }
        };
        return doc.ToJsonString(Pretty) + "\n";
    }

    private static string Sanitize(string s)
    {
        var sb = new StringBuilder();
        foreach (var ch in s)
            sb.Append(char.IsLetterOrDigit(ch) ? ch : '_');
        var r = sb.ToString().Trim('_');
        return r.Length == 0 ? "api" : r;
    }
}
