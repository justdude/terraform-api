using System.Text;
using TerraformApi.Domain.Interfaces;
using TerraformApi.Domain.Models;

namespace TerraformApi.Application.Services;

public sealed class TerraformGeneratorService : ITerraformGenerator
{
    public string Generate(ApimConfiguration configuration)
    {
        var sb = new StringBuilder();
        var indent = "        ";

        sb.AppendLine($"{configuration.ApiGroupName} = {{");
        sb.AppendLine($"  product = [{FormatStringList(configuration.Products)}]");

        // API block
        sb.AppendLine("  api = [");
        sb.AppendLine("    {");
        WriteApiBlock(sb, configuration.Api, indent);
        sb.AppendLine("    },");
        sb.AppendLine("  ]");
        sb.AppendLine();

        // API Operations block
        sb.AppendLine("  api_operations = [");
        for (var i = 0; i < configuration.ApiOperations.Count; i++)
        {
            sb.AppendLine("    {");
            WriteOperationBlock(sb, configuration.ApiOperations[i], indent);
            sb.AppendLine("    },");
        }
        sb.AppendLine("  ]");

        sb.AppendLine("}");

        return sb.ToString();
    }

    private static void WriteApiBlock(StringBuilder sb, ApimApi api, string indent)
    {
        sb.AppendLine($"{indent}apim_resource_group_name         = \"{api.ApimResourceGroupName}\"");
        sb.AppendLine($"{indent}apim_name                        = \"{api.ApimName}\"");
        sb.AppendLine($"{indent}name                             = \"{api.Name}\"");
        sb.AppendLine($"{indent}display_name                     = \"{api.DisplayName}\"");
        sb.AppendLine($"{indent}path                             = \"{api.Path}\"");
        sb.AppendLine($"{indent}service_url                      = \"{api.ServiceUrl}\"");
        sb.AppendLine($"{indent}protocols                        = [{FormatStringList(api.Protocols)}]");
        sb.AppendLine($"{indent}revision                         = \"{api.Revision}\"");
        sb.AppendLine($"{indent}soap_pass_through                = {FormatBool(api.SoapPassThrough)}");
        sb.AppendLine($"{indent}subscription_required            = {FormatBool(api.SubscriptionRequired)}");
        sb.AppendLine($"{indent}product_id                       = {FormatNullableString(api.ProductId)}");
        sb.AppendLine($"{indent}subscription_key_parameter_names = {FormatNullableString(api.SubscriptionKeyParameterNames)}");

        if (!string.IsNullOrEmpty(api.Policy))
        {
            sb.AppendLine();
            sb.AppendLine($"{indent}policy = <<XML");
            sb.Append(api.Policy);
            if (!api.Policy.EndsWith('\n'))
                sb.AppendLine();
            sb.AppendLine("XML");
        }
    }

    private static void WriteOperationBlock(StringBuilder sb, ApimApiOperation op, string indent)
    {
        sb.AppendLine($"{indent}operation_id             = \"{op.OperationId}\"");
        sb.AppendLine($"{indent}apim_resource_group_name = \"{op.ApimResourceGroupName}\"");
        sb.AppendLine($"{indent}apim_name                = \"{op.ApimName}\"");
        sb.AppendLine($"{indent}api_name                 = \"{op.ApiName}\"");
        sb.AppendLine($"{indent}display_name             = \"{op.DisplayName}\"");
        sb.AppendLine($"{indent}method                   = \"{op.Method}\"");
        sb.AppendLine($"{indent}url_template             = \"{op.UrlTemplate}\"");
        sb.AppendLine($"{indent}status_code              = \"{op.StatusCode}\"");
        sb.AppendLine($"{indent}description              = \"{EscapeString(op.Description)}\"");

        if (op.Requests.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine($"{indent}request = [");
            foreach (var request in op.Requests)
            {
                sb.AppendLine($"{indent}  {{");
                WriteRequestBlock(sb, request, indent + "    ");
                sb.AppendLine($"{indent}  }}");
            }
            sb.AppendLine($"{indent}]");
        }

        if (op.Responses.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine($"{indent}response = [");
            foreach (var response in op.Responses)
            {
                sb.AppendLine($"{indent}  {{");
                sb.AppendLine($"{indent}    status_code  = {response.StatusCode}");
                sb.AppendLine($"{indent}    description  = \"{EscapeString(response.Description)}\"");
                if (response.Representations.Count > 0)
                {
                    sb.AppendLine($"{indent}    representation = [");
                    foreach (var rep in response.Representations)
                    {
                        sb.AppendLine($"{indent}      {{");
                        sb.AppendLine($"{indent}        content_type = \"{rep.ContentType}\"");
                        sb.AppendLine($"{indent}      }}");
                    }
                    sb.AppendLine($"{indent}    ]");
                }
                sb.AppendLine($"{indent}  }}");
            }
            sb.AppendLine($"{indent}]");
        }
    }

    private static void WriteRequestBlock(StringBuilder sb, ApimOperationRequest request, string indent)
    {
        if (request.Headers.Count > 0)
        {
            sb.AppendLine($"{indent}header = [");
            foreach (var header in request.Headers)
            {
                sb.AppendLine($"{indent}  {{");
                sb.AppendLine($"{indent}    name        = \"{header.Name}\"");
                sb.AppendLine($"{indent}    required    = {FormatBool(header.Required)}");
                sb.AppendLine($"{indent}    type        = \"{header.Type}\"");
                sb.AppendLine($"{indent}    description = \"{EscapeString(header.Description)}\"");
                sb.AppendLine($"{indent}  }}");
            }
            sb.AppendLine($"{indent}]");
        }

        if (request.QueryParameters.Count > 0)
        {
            sb.AppendLine($"{indent}query_parameter = [");
            foreach (var param in request.QueryParameters)
            {
                sb.AppendLine($"{indent}  {{");
                sb.AppendLine($"{indent}    name        = \"{param.Name}\"");
                sb.AppendLine($"{indent}    required    = {FormatBool(param.Required)}");
                sb.AppendLine($"{indent}    type        = \"{param.Type}\"");
                sb.AppendLine($"{indent}    description = \"{EscapeString(param.Description)}\"");
                sb.AppendLine($"{indent}  }}");
            }
            sb.AppendLine($"{indent}]");
        }

        if (request.Representations.Count > 0)
        {
            sb.AppendLine($"{indent}representation = [");
            foreach (var rep in request.Representations)
            {
                sb.AppendLine($"{indent}  {{");
                sb.AppendLine($"{indent}    content_type = \"{rep.ContentType}\"");
                sb.AppendLine($"{indent}  }}");
            }
            sb.AppendLine($"{indent}]");
        }
    }

    private static string FormatBool(bool value) => value ? "true" : "false";

    private static string FormatNullableString(string? value) =>
        value == null ? "null" : $"\"{value}\"";

    private static string FormatStringList(List<string> items) =>
        items.Count == 0 ? "" : string.Join(", ", items.Select(i => $"\"{i}\""));

    private static string EscapeString(string value) =>
        value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n");
}
