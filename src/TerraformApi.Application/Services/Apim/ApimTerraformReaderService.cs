using TerraformApi.Domain.Interfaces;
using TerraformApi.Domain.Models.Apim;
using TerraformApi.Domain.Models.Hcl;
using TerraformApi.Domain.Models.Sync;

namespace TerraformApi.Application.Services.Apim;

/// <summary>
/// Navigates a parsed HCL AST and extracts the APIM structure
/// (api groups → api blocks + operations) with references back into the AST.
/// Works only through <see cref="HclObject"/> nodes — content inside policy
/// heredocs is a single <see cref="HclHeredoc"/> and is never mistaken for
/// operation fields.
/// </summary>
public sealed class ApimTerraformReaderService : IApimTerraformReader
{
    private readonly IHclParser _parser;

    public ApimTerraformReaderService(IHclParser parser) => _parser = parser;

    /// <inheritdoc />
    public IReadOnlyList<IReadOnlyList<string>> KnownApiGroupPaths { get; } =
    [
        ["apis", "bpc_apis", "backend_apis"],
        ["apis", "backend_apis"],
        []
    ];

    /// <inheritdoc />
    public ParsedApimDocument Read(string terraformSource) =>
        Read(_parser.Parse(terraformSource));

    /// <inheritdoc />
    public ParsedApimDocument Read(HclDocument document)
    {
        foreach (var path in KnownApiGroupPaths)
        {
            var parent = NavigatePath(document, path);
            if (parent is null)
                continue;

            var groups = ExtractGroups(parent);
            if (groups.Count > 0)
            {
                return new ParsedApimDocument
                {
                    Ast = document,
                    ApiGroupParentPath = path.Count > 0 ? path : null,
                    ApiGroups = groups,
                    ApisByGroupKey = BuildGroupIndex(groups)
                };
            }
        }

        return new ParsedApimDocument { Ast = document };
    }

    /// <summary>
    /// Walks the document from the root along the given key path.
    /// An empty path returns a pseudo-object over the root assignments.
    /// </summary>
    private static HclObject? NavigatePath(HclDocument document, IReadOnlyList<string> path)
    {
        if (path.Count == 0)
            return new HclObject { Items = document.RootItems };

        HclObject? current = null;
        foreach (var key in path)
        {
            var source = current?.Assignments ?? document.RootAssignments;
            var next = source.FirstOrDefault(a => a.Key == key)?.Value as HclObject;
            if (next is null)
                return null;
            current = next;
        }
        return current;
    }

    /// <summary>
    /// Returns the api groups inside <paramref name="parent"/>: assignments whose
    /// value is an object containing an <c>api</c> or <c>api_operations</c> array.
    /// </summary>
    private static List<ParsedApiGroup> ExtractGroups(HclObject parent)
    {
        var groups = new List<ParsedApiGroup>();

        foreach (var assignment in parent.Assignments)
        {
            if (assignment.Value is not HclObject groupObject)
                continue;

            var hasApi = groupObject.Get("api") is HclArray;
            var hasOperations = groupObject.Get("api_operations") is HclArray;
            if (!hasApi && !hasOperations)
                continue;

            var group = new ParsedApiGroup
            {
                ApiGroupName = assignment.Key,
                KeyIsQuoted = assignment.KeyIsQuoted,
                AstNode = groupObject
            };

            if (groupObject.Get("api") is HclArray apiArray)
            {
                foreach (var item in apiArray.Items)
                {
                    if (item.Value is HclObject apiObject)
                        group.Apis.Add(ParseApi(apiObject));
                }
            }

            if (groupObject.Get("api_operations") is HclArray opsArray)
            {
                foreach (var item in opsArray.Items)
                {
                    if (item.Value is HclObject opObject)
                        group.Operations.Add(ParseOperation(opObject, item));
                }
            }

            groups.Add(group);
        }

        return groups;
    }

    private static ParsedApi ParseApi(HclObject node) => new()
    {
        AstNode = node,
        Name = Ref(node, "name"),
        DisplayName = Ref(node, "display_name"),
        ApimResourceGroupName = Ref(node, "apim_resource_group_name"),
        ApimName = Ref(node, "apim_name"),
        Path = Ref(node, "path"),
        ServiceUrl = Ref(node, "service_url"),
        Revision = Ref(node, "revision"),
        Policy = node.Get("policy") is { } policy ? new HclValueRef { Node = policy } : null
    };

    private static ParsedApiOperation ParseOperation(HclObject node, HclArrayItem arrayItem) => new()
    {
        AstNode = node,
        ArrayItem = arrayItem,
        OperationId = Ref(node, "operation_id"),
        Method = Ref(node, "method"),
        UrlTemplate = Ref(node, "url_template"),
        ApimResourceGroupName = Ref(node, "apim_resource_group_name"),
        ApiName = Ref(node, "api_name"),
        DisplayName = Ref(node, "display_name"),
        StatusCode = Ref(node, "status_code"),
        Description = Ref(node, "description"),
        RequestArray = node.Get("request") as HclArray,
        ResponsesArray = node.Get("response") as HclArray
    };

    private static HclValueRef Ref(HclObject node, string key) =>
        new() { Node = node.Get(key) };

    /// <summary>
    /// Groups api blocks and operations by (apim_resource_group_name, api_name)
    /// so sync can target the correct API without ambiguity.
    /// </summary>
    private static Dictionary<ApimApiGroupKey, ApiGroupBundle> BuildGroupIndex(List<ParsedApiGroup> groups)
    {
        var index = new Dictionary<ApimApiGroupKey, ApiGroupBundle>();

        ApiGroupBundle GetOrAdd(ApimApiGroupKey key, ParsedApiGroup owner)
        {
            if (!index.TryGetValue(key, out var bundle))
            {
                bundle = new ApiGroupBundle { Key = key, OwnerGroup = owner };
                index[key] = bundle;
            }
            return bundle;
        }

        foreach (var group in groups)
        {
            foreach (var api in group.Apis)
            {
                var rg = api.ApimResourceGroupName.StructuralText;
                var name = api.Name.StructuralText;
                if (rg is null || name is null)
                    continue;

                var key = new ApimApiGroupKey { ApimResourceGroupNameRaw = rg, ApiNameRaw = name };
                var bundle = GetOrAdd(key, group);
                bundle.Api ??= api;
            }

            foreach (var op in group.Operations)
            {
                var rg = op.ApimResourceGroupName?.StructuralText;
                var apiName = op.ApiName?.StructuralText;
                if (rg is null || apiName is null)
                    continue;

                var key = new ApimApiGroupKey { ApimResourceGroupNameRaw = rg, ApiNameRaw = apiName };
                GetOrAdd(key, group).Operations.Add(op);
            }
        }

        return index;
    }
}
