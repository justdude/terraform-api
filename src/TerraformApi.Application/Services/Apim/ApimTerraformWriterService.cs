using System.Text;
using TerraformApi.Application.Services.Hcl;
using TerraformApi.Domain.Interfaces;
using TerraformApi.Domain.Models;
using TerraformApi.Domain.Models.Apim;
using TerraformApi.Domain.Models.Hcl;
using TerraformApi.Domain.Models.Sync;

namespace TerraformApi.Application.Services.Apim;

/// <summary>
/// Writes a parsed APIM document back to HCL (delegating to <see cref="IHclWriter"/>)
/// and builds fresh APIM documents from an <see cref="ApimConfiguration"/> using a
/// template profile — placing interpolations for templated fields and literals
/// for everything else, with leading comments per operation.
/// </summary>
public sealed class ApimTerraformWriterService : IApimTerraformWriter
{
    private readonly IHclWriter _hclWriter;
    private readonly IApimTerraformReader _reader;
    private readonly IOperationCommentBuilder _commentBuilder;

    public ApimTerraformWriterService(
        IHclWriter hclWriter,
        IApimTerraformReader reader,
        IOperationCommentBuilder commentBuilder)
    {
        _hclWriter = hclWriter;
        _reader = reader;
        _commentBuilder = commentBuilder;
    }

    /// <inheritdoc />
    public string Write(ParsedApimDocument parsed, HclWriteOptions? options = null) =>
        _hclWriter.Write(parsed.Ast, options);

    /// <inheritdoc />
    public ParsedApimDocument BuildFromConfiguration(ApimConfiguration configuration, BuildOptions options)
    {
        var profile = options.Profile;

        var groupItems = new List<HclObjectItem>
        {
            new HclAssignment { Key = "product", Value = BuildProductArray(configuration) },
            new HclAssignment { Key = "api", Value = BuildApiArray(configuration, profile) }
        };

        var operationsArray = BuildOperationsArray(configuration, options);

        if (options.AddReplaceBeforeApplyHeader)
        {
            var placeholders = CollectGroupPlaceholders(groupItems, operationsArray);
            if (placeholders.Count > 0)
                groupItems.AddRange(BuildReplaceBeforeApplyHeader(placeholders));
        }

        groupItems.Add(new HclAssignment { Key = "api_operations", Value = operationsArray });

        var groupObject = new HclObject { Items = groupItems };
        var groupAssignment = new HclAssignment
        {
            Key = configuration.ApiGroupName,
            KeyIsQuoted = NeedsQuotedKey(configuration.ApiGroupName),
            Value = groupObject
        };

        // Wrap in the parent path chain (apis.bpc_apis.backend_apis ...).
        HclObjectItem current = groupAssignment;
        var parentPath = options.ApiGroupParentPath ?? [];
        for (var i = parentPath.Count - 1; i >= 0; i--)
        {
            current = new HclAssignment
            {
                Key = parentPath[i],
                Value = new HclObject { Items = [current] }
            };
        }

        var document = new HclDocument { RootItems = [current] };
        return _reader.Read(document);
    }

    // -----------------------------------------------------------------
    // product / api / operations construction
    // -----------------------------------------------------------------

    private static HclArray BuildProductArray(ApimConfiguration configuration)
    {
        var array = new HclArray();
        foreach (var product in configuration.Products)
        {
            array.Items.Add(new HclArrayItem
            {
                Value = new HclObject
                {
                    Items =
                    [
                        Assign("apim_resource_group_name", Str(product.ApimResourceGroupName)),
                        Assign("apim_name", Str(product.ApimName)),
                        Assign("product_id", Str(product.ProductId)),
                        Assign("display_name", Str(product.DisplayName)),
                        Assign("subscription_required", Bool(product.SubscriptionRequired)),
                        Assign("approval_required", Bool(product.ApprovalRequired)),
                        Assign("published", Bool(product.Published)),
                        Assign("description", Str(product.Description))
                    ]
                }
            });
        }
        return array;
    }

    private HclArray BuildApiArray(ApimConfiguration configuration, ApimTemplateProfile profile)
    {
        var api = configuration.Api;
        var items = new List<HclObjectItem>
        {
            Assign("apim_resource_group_name", Templated(profile.ApiFieldTemplates, "apim_resource_group_name", api.ApimResourceGroupName)),
            Assign("apim_name", Templated(profile.ApiFieldTemplates, "apim_name", api.ApimName)),
            Assign("name", Templated(profile.ApiFieldTemplates, "name", api.Name)),
            Assign("display_name", Templated(profile.ApiFieldTemplates, "display_name", api.DisplayName)),
            Assign("path", Templated(profile.ApiFieldTemplates, "path", api.Path)),
            Assign("service_url", Templated(profile.ApiFieldTemplates, "service_url", api.ServiceUrl)),
            Assign("protocols", ScalarArray(api.Protocols)),
            Assign("revision", Templated(profile.ApiFieldTemplates, "revision", api.Revision)),
            Assign("soap_pass_through", Bool(api.SoapPassThrough)),
            Assign("subscription_required", profile.ApiFieldTemplates.ContainsKey("subscription_required")
                ? Templated(profile.ApiFieldTemplates, "subscription_required", api.SubscriptionRequired.ToString().ToLowerInvariant())
                : Bool(api.SubscriptionRequired)),
            Assign("product_id", api.ProductId is null && !profile.ApiFieldTemplates.ContainsKey("product_id")
                ? Null()
                : Templated(profile.ApiFieldTemplates, "product_id", api.ProductId ?? "")),
            Assign("subscription_key_parameter_names", api.SubscriptionKeyParameterNames is null
                ? Null()
                : Str(api.SubscriptionKeyParameterNames))
        };

        if (!string.IsNullOrWhiteSpace(api.Policy))
        {
            items.Add(Assign("policy", new HclHeredoc
            {
                Marker = "XML",
                Content = api.Policy.TrimEnd('\n', '\r')
            }));
        }

        var array = new HclArray();
        array.Items.Add(new HclArrayItem { Value = new HclObject { Items = items } });
        return array;
    }

    private HclArray BuildOperationsArray(ApimConfiguration configuration, BuildOptions options)
    {
        var array = new HclArray();
        foreach (var operation in configuration.ApiOperations)
        {
            var node = BuildOperationObject(operation, options.Profile);

            var leading = new List<HclComment>();
            if (options.AddOperationComments)
            {
                var placeholders = _commentBuilder.ExtractPlaceholders(node);
                var opIdText = (node.Get("operation_id") as HclInterpolation)?.InnerText
                               ?? (node.Get("operation_id") as HclLiteral)?.RawValue
                               ?? operation.OperationId;
                leading = _commentBuilder.Build(new OperationCommentSpec
                {
                    Method = operation.Method,
                    UrlTemplate = operation.UrlTemplate,
                    OperationId = opIdText,
                    DisplayName = operation.DisplayName,
                    Source = options.CommentSource,
                    PlaceholdersToReplace = placeholders
                });
            }

            array.Items.Add(new HclArrayItem { LeadingComments = leading, Value = node });
        }
        return array;
    }

    /// <summary>
    /// Builds the HCL object for a single operation using the profile's field
    /// templates ({op} is substituted with the kebab-cased operationId).
    /// Shared with the synchronizer for appending new operations.
    /// </summary>
    internal static HclObject BuildOperationObject(ApimApiOperation operation, ApimTemplateProfile profile)
    {
        var op = ToKebabCase(operation.OperationId);

        HclValue OperationField(string field, string literal)
        {
            if (profile.OperationFieldTemplates.TryGetValue(field, out var template))
                return Interp(ApplyOpSubstitution(template, op));
            return Str(literal);
        }

        HclValue operationId;
        if (profile.OperationIdTemplate is not null)
            operationId = Interp(ApplyOpSubstitution(profile.OperationIdTemplate, op));
        else
            operationId = OperationField("operation_id", operation.OperationId);

        var items = new List<HclObjectItem>
        {
            Assign("operation_id", operationId),
            Assign("apim_resource_group_name", OperationField("apim_resource_group_name", operation.ApimResourceGroupName)),
            Assign("apim_name", OperationField("apim_name", operation.ApimName)),
            Assign("api_name", OperationField("api_name", operation.ApiName)),
            Assign("display_name", profile.TemplatizeDisplayName
                ? OperationField("display_name", operation.DisplayName)
                : Str(operation.DisplayName)),
            Assign("method", Str(operation.Method.ToUpperInvariant())),
            Assign("url_template", Str(operation.UrlTemplate)),
            Assign("status_code", Str(operation.StatusCode.ToString())),
            Assign("description", Str(operation.Description))
        };

        if (operation.Requests.Count > 0)
            items.Add(Assign("request", BuildRequestArray(operation.Requests)));

        if (operation.Responses.Count > 0)
            items.Add(Assign("response", BuildResponseArray(operation.Responses)));

        return new HclObject { Items = items };
    }

    private static HclArray BuildRequestArray(List<ApimOperationRequest> requests)
    {
        var array = new HclArray();
        foreach (var request in requests)
        {
            var items = new List<HclObjectItem>();

            if (request.Headers.Count > 0)
                items.Add(Assign("header", ParameterArray(request.Headers)));

            if (request.QueryParameters.Count > 0)
                items.Add(Assign("query_parameter", ParameterArray(request.QueryParameters)));

            if (request.Representations.Count > 0)
                items.Add(Assign("representation", RepresentationArray(request.Representations)));

            array.Items.Add(new HclArrayItem { Value = new HclObject { Items = items } });
        }
        return array;
    }

    private static HclArray BuildResponseArray(List<ApimOperationResponse> responses)
    {
        var array = new HclArray();
        foreach (var response in responses)
        {
            var items = new List<HclObjectItem>
            {
                Assign("status_code", Num(response.StatusCode.ToString())),
                Assign("description", Str(response.Description))
            };
            if (response.Representations.Count > 0)
                items.Add(Assign("representation", RepresentationArray(response.Representations)));

            array.Items.Add(new HclArrayItem { Value = new HclObject { Items = items } });
        }
        return array;
    }

    private static HclArray ParameterArray(List<ApimParameter> parameters)
    {
        var array = new HclArray();
        foreach (var parameter in parameters)
        {
            array.Items.Add(new HclArrayItem
            {
                Value = new HclObject
                {
                    Items =
                    [
                        Assign("name", Str(parameter.Name)),
                        Assign("required", Bool(parameter.Required)),
                        Assign("type", Str(parameter.Type)),
                        Assign("description", Str(parameter.Description))
                    ]
                }
            });
        }
        return array;
    }

    private static HclArray RepresentationArray(List<ApimRepresentation> representations)
    {
        var array = new HclArray();
        foreach (var representation in representations)
        {
            var items = new List<HclObjectItem> { Assign("content_type", Str(representation.ContentType)) };
            if (representation.SchemaId is not null)
                items.Add(Assign("schema_id", Str(representation.SchemaId)));
            if (representation.TypeName is not null)
                items.Add(Assign("type_name", Str(representation.TypeName)));
            array.Items.Add(new HclArrayItem { Value = new HclObject { Items = items } });
        }
        return array;
    }

    // -----------------------------------------------------------------
    // REPLACE BEFORE APPLY header
    // -----------------------------------------------------------------

    private List<string> CollectGroupPlaceholders(List<HclObjectItem> groupItems, HclArray operationsArray)
    {
        var holder = new HclObject
        {
            Items = [.. groupItems, new HclAssignment { Key = "api_operations", Value = operationsArray }]
        };
        return _commentBuilder.ExtractPlaceholders(holder).ToList();
    }

    /// <summary>Builds the REPLACE BEFORE APPLY comment block (§REV-1.5.6).</summary>
    internal static List<HclComment> BuildReplaceBeforeApplyHeader(IReadOnlyList<string> placeholders)
    {
        const string ruler = " ============================================================================";
        var comments = new List<HclComment>
        {
            new() { Kind = HclCommentKind.LineHash, Text = ruler },
            new() { Kind = HclCommentKind.LineHash, Text = " REPLACE BEFORE APPLY: define these variables in .tfvars or via -var:" }
        };

        foreach (var chunk in placeholders.Chunk(6))
        {
            var line = new StringBuilder(" ");
            foreach (var name in chunk)
                line.Append("${").Append(name).Append("} ");
            comments.Add(new HclComment { Kind = HclCommentKind.LineHash, Text = line.ToString().TrimEnd() });
        }

        comments.Add(new HclComment { Kind = HclCommentKind.LineHash, Text = ruler });
        return comments;
    }

    // -----------------------------------------------------------------
    // Small AST factory helpers
    // -----------------------------------------------------------------

    private static HclValue Templated(IReadOnlyDictionary<string, string> templates, string field, string literal) =>
        templates.TryGetValue(field, out var template) ? Interp(template) : Str(literal);

    private static HclAssignment Assign(string key, HclValue value) => new() { Key = key, Value = value };

    private static HclLiteral Str(string value) => new() { RawValue = value, Kind = HclLiteralKind.String };

    private static HclLiteral Num(string value) => new() { RawValue = value, Kind = HclLiteralKind.Number };

    private static HclLiteral Bool(bool value) => new() { RawValue = value ? "true" : "false", Kind = HclLiteralKind.Bool };

    private static HclLiteral Null() => new() { RawValue = "null", Kind = HclLiteralKind.Null };

    private static HclInterpolation Interp(string innerText) => new()
    {
        InnerText = innerText,
        ReferencedExpressions = HclParserService.ExtractReferences(innerText)
    };

    private static HclArray ScalarArray(IEnumerable<string> values)
    {
        var array = new HclArray();
        foreach (var value in values)
            array.Items.Add(new HclArrayItem { Value = Str(value) });
        return array;
    }

    /// <summary>
    /// True when the key must be quoted to stay valid HCL: anything that is not
    /// a plain identifier — interpolations (${...}), placeholder tags ({...}),
    /// dots, spaces, etc. An unquoted "{api-group}" would be unparseable.
    /// </summary>
    internal static bool NeedsQuotedKey(string key) =>
        !System.Text.RegularExpressions.Regex.IsMatch(key, "^[A-Za-z_][A-Za-z0-9_-]*$");

    /// <summary>Replaces {op} in a template with the kebab-cased operation id.</summary>
    internal static string ApplyOpSubstitution(string template, string kebabOperationId) =>
        template.Replace("{op}", kebabOperationId);

    /// <summary>listUserById → list-user-by-id; non-alphanumerics become dashes.</summary>
    internal static string ToKebabCase(string value)
    {
        if (string.IsNullOrEmpty(value))
            return value;

        var sb = new StringBuilder();
        for (var i = 0; i < value.Length; i++)
        {
            var ch = value[i];
            if (char.IsUpper(ch))
            {
                if (i > 0 && sb.Length > 0 && sb[^1] != '-')
                    sb.Append('-');
                sb.Append(char.ToLowerInvariant(ch));
            }
            else if (char.IsLetterOrDigit(ch))
            {
                sb.Append(ch);
            }
            else if (sb.Length > 0 && sb[^1] != '-')
            {
                sb.Append('-');
            }
        }
        return sb.ToString().Trim('-');
    }
}
