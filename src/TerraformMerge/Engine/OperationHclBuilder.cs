using TerraformApi.Domain.Models.Hcl;

namespace TerraformMerge.Engine;

/// <summary>
/// Builds a fresh <c>api_operation</c> HCL object from an
/// <see cref="OperationNode"/>. Used when the node did not come from the file
/// being rewritten (an OpenAPI operation, or an operation from another
/// Terraform file), so its AST cannot be reused verbatim — a canonical block is
/// generated in the target file's style via <see cref="OperationTemplateContext"/>.
/// </summary>
public static class OperationHclBuilder
{
    public static HclArrayItem BuildArrayItem(OperationNode node, OperationTemplateContext ctx) =>
        new() { Value = Build(node, ctx) };

    public static HclObject Build(OperationNode node, OperationTemplateContext ctx)
    {
        var items = new List<HclObjectItem>
        {
            Assign("operation_id", Str(string.IsNullOrEmpty(node.OperationId) ? "{operation-id}" : node.OperationId)),
            Assign("apim_resource_group_name", Str(ctx.ResourceGroup)),
            Assign("apim_name", Str(ctx.ApimName)),
            Assign("api_name", Str(ctx.ApiName)),
            Assign("display_name", Str(node.DisplayName)),
            Assign("method", Str(node.Method.ToUpperInvariant())),
            Assign("url_template", Str(node.UrlTemplate)),
            Assign("status_code", Str((node.StatusCode ?? 200).ToString())),
            Assign("description", Str(node.Description))
        };

        var request = BuildRequest(node);
        if (request is not null)
            items.Add(Assign("request", request));

        var response = BuildResponse(node);
        if (response is not null)
            items.Add(Assign("response", response));

        return new HclObject { Items = items };
    }

    private static HclArray? BuildRequest(OperationNode node)
    {
        var headers = ParamObjects(node, "header:");
        var queries = ParamObjects(node, "query:");
        var representations = RepresentationObjects(node);

        if (headers.Count == 0 && queries.Count == 0 && representations.Count == 0)
            return null;

        var requestFields = new List<HclObjectItem>();
        if (headers.Count > 0)
            requestFields.Add(Assign("header", ArrayOf(headers)));
        if (queries.Count > 0)
            requestFields.Add(Assign("query_parameter", ArrayOf(queries)));
        if (representations.Count > 0)
            requestFields.Add(Assign("representation", ArrayOf(representations)));

        return ArrayOf([new HclObject { Items = requestFields }]);
    }

    private static List<HclObject> ParamObjects(OperationNode node, string prefix)
    {
        var result = new List<HclObject>();
        foreach (var key in node.ParameterKeys)
        {
            if (!key.StartsWith(prefix, StringComparison.Ordinal))
                continue;

            var name = key[prefix.Length..];
            result.Add(new HclObject
            {
                Items =
                [
                    Assign("name", Str(name)),
                    Assign("required", Bool(false)),
                    Assign("type", Str("string")),
                    Assign("description", Str(""))
                ]
            });
        }
        return result;
    }

    private static List<HclObject> RepresentationObjects(OperationNode node)
    {
        var contentTypes = node.OpenApiOperation?.RequestBodyContentTypes;
        if (contentTypes is null || contentTypes.Count == 0)
            return [];

        return contentTypes.Select(ct => new HclObject
        {
            Items = [Assign("content_type", Str(ct))]
        }).ToList();
    }

    private static HclArray? BuildResponse(OperationNode node)
    {
        if (node.ResponseCodes.Count == 0)
            return null;

        var objects = node.ResponseCodes.Select(code => new HclObject
        {
            Items =
            [
                Assign("status_code", Num(code.ToString())),
                Assign("description", Str(""))
            ]
        }).ToList();

        return ArrayOf(objects);
    }

    // -- small AST helpers --
    private static HclAssignment Assign(string key, HclValue value) => new() { Key = key, Value = value };
    private static HclLiteral Str(string value) => new() { RawValue = value, Kind = HclLiteralKind.String };
    private static HclLiteral Num(string value) => new() { RawValue = value, Kind = HclLiteralKind.Number };
    private static HclLiteral Bool(bool value) => new() { RawValue = value ? "true" : "false", Kind = HclLiteralKind.Bool };

    private static HclArray ArrayOf(IEnumerable<HclObject> objects)
    {
        var array = new HclArray();
        foreach (var obj in objects)
            array.Items.Add(new HclArrayItem { Value = obj });
        return array;
    }
}
