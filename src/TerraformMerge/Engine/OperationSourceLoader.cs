using System.Text.Json;
using TerraformApi.Application.Services;
using TerraformApi.Application.Services.Apim;
using TerraformApi.Application.Services.Hcl;
using TerraformApi.Application.Services.OpenApi;
using TerraformApi.Domain.Models.Apim;
using TerraformApi.Domain.Models.Hcl;

namespace TerraformMerge.Engine;

/// <summary>
/// Loads a Terraform APIM config or an OpenAPI document into
/// <see cref="OperationNode"/>s. Terraform is parsed on the shared graph-based
/// HCL AST (each <c>api_operation</c> block becomes a node with its AST kept);
/// OpenAPI is parsed through the shared facade.
/// </summary>
public sealed class OperationSourceLoader
{
    private readonly ApimTerraformReaderService _reader = new(new HclParserService());
    private readonly OpenApiFacadeService _openApi = new(new ApimNamingValidatorService());

    /// <summary>True when the text looks like an OpenAPI JSON document.</summary>
    public static bool LooksLikeOpenApi(string text)
    {
        var trimmed = text.TrimStart();
        if (trimmed.Length == 0 || trimmed[0] != '{')
            return false;

        try
        {
            using var doc = JsonDocument.Parse(text);
            var root = doc.RootElement;
            return root.ValueKind == JsonValueKind.Object &&
                   (root.TryGetProperty("openapi", out _) ||
                    root.TryGetProperty("swagger", out _) ||
                    root.TryGetProperty("paths", out _));
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>Auto-detects the format and loads the operations.</summary>
    public LoadedOperations Load(string text, OperationSource terraformSource, OperationSource openApiSource, string sourceLabel = "inline") =>
        LooksLikeOpenApi(text)
            ? LoadOpenApi(text, openApiSource, sourceLabel)
            : LoadTerraform(text, terraformSource);

    /// <summary>Parses a Terraform APIM config into operation nodes, keeping the AST.</summary>
    public LoadedOperations LoadTerraform(string text, OperationSource source)
    {
        var parsed = _reader.Read(text);
        var nodes = new List<OperationNode>();

        foreach (var group in parsed.ApiGroups)
        {
            foreach (var op in group.Operations)
            {
                nodes.Add(new OperationNode
                {
                    Source = source,
                    OperationId = op.OperationId.StructuralText ?? "",
                    Method = (op.Method.StructuralText ?? "").ToUpperInvariant(),
                    UrlTemplate = op.UrlTemplate.StructuralText ?? "",
                    DisplayName = op.DisplayName?.StructuralText ?? "",
                    Description = op.Description?.StructuralText ?? "",
                    StatusCode = ParseStatus(op.StatusCode?.StructuralText),
                    ApiName = op.ApiName?.StructuralText ?? "",
                    ApimResourceGroupName = op.ApimResourceGroupName?.StructuralText ?? "",
                    ApimName = FieldText(op.AstNode, "apim_name"),
                    ParameterKeys = ExtractParameterKeys(op.RequestArray),
                    ResponseCodes = ExtractResponseCodes(op.ResponsesArray),
                    ArrayItem = op.ArrayItem,
                    ApiGroupName = group.ApiGroupName
                });
            }
        }

        return new LoadedOperations
        {
            Nodes = nodes,
            TerraformDocument = parsed,
            SourceText = text
        };
    }

    /// <summary>Parses an OpenAPI document into operation nodes.</summary>
    public LoadedOperations LoadOpenApi(string json, OperationSource source, string sourceLabel = "inline")
    {
        var result = _openApi.ParseOperations(json, sourceLabel);
        if (!result.Success)
            throw new InvalidOperationException(result.Error ?? "Failed to parse OpenAPI document.");

        var nodes = result.Operations.Select(op => new OperationNode
        {
            Source = source,
            OperationId = op.OperationId ?? "",
            Method = op.Method.ToUpperInvariant(),
            UrlTemplate = op.UrlTemplate,
            DisplayName = op.Description ?? "",
            Description = op.Description ?? "",
            StatusCode = op.ResponseCodes?.FirstOrDefault(c => c is >= 200 and < 300),
            ParameterKeys = (op.Parameters ?? [])
                .Where(p => !string.IsNullOrEmpty(p.Name))
                .Select(p => $"{p.In.ToLowerInvariant()}:{p.Name.ToLowerInvariant()}")
                .OrderBy(k => k, StringComparer.Ordinal)
                .ToList(),
            ResponseCodes = op.ResponseCodes ?? [],
            OpenApiOperation = op
        }).ToList();

        return new LoadedOperations { Nodes = nodes, SourceText = json };
    }

    private static int? ParseStatus(string? text) =>
        int.TryParse(text, out var code) ? code : null;

    private static string FieldText(HclObject node, string key) =>
        new HclValueRef { Node = node.Get(key) }.StructuralText ?? "";

    private static IReadOnlyList<string> ExtractParameterKeys(HclArray? requestArray)
    {
        if (requestArray is null)
            return [];

        var keys = new List<string>();
        foreach (var item in requestArray.Items)
        {
            if (item.Value is not HclObject request)
                continue;

            CollectNames(request, "header", "header", keys);
            CollectNames(request, "query_parameter", "query", keys);
        }

        keys.Sort(StringComparer.Ordinal);
        return keys;
    }

    private static void CollectNames(HclObject request, string arrayKey, string location, List<string> keys)
    {
        if (request.Get(arrayKey) is not HclArray array)
            return;

        foreach (var item in array.Items)
        {
            if (item.Value is HclObject param && param.Get("name") is HclLiteral name)
                keys.Add($"{location}:{name.RawValue.ToLowerInvariant()}");
        }
    }

    private static IReadOnlyList<int> ExtractResponseCodes(HclArray? responsesArray)
    {
        if (responsesArray is null)
            return [];

        var codes = new List<int>();
        foreach (var item in responsesArray.Items)
        {
            if (item.Value is HclObject response &&
                response.Get("status_code") is HclLiteral literal &&
                int.TryParse(literal.RawValue, out var code))
            {
                codes.Add(code);
            }
        }
        return codes;
    }
}
