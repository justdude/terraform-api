using Microsoft.OpenApi.Models;
using TerraformApi.Domain.Interfaces;
using TerraformApi.Domain.Models;

namespace TerraformApi.Application.Services.OpenApi;

/// <summary>
/// Static helper that maps a parsed <see cref="OpenApiDocument"/> onto the
/// <see cref="ApimConfiguration"/> Terraform model: api block, one operation
/// per path+method (headers, query parameters, request body representations,
/// responses), optional CORS policy and product. Pure mapping — document
/// reading lives in <see cref="OpenApiDocumentReader"/>.
/// </summary>
internal static class ApimConfigurationBuilder
{
    /// <summary>
    /// Builds the APIM configuration. <paramref name="settings"/> must already
    /// be placeholder-normalized (see <see cref="ApimPlaceholders.Normalize"/>).
    /// </summary>
    public static ApimConfiguration Build(
        OpenApiDocument document,
        ConversionSettings settings,
        IApimNamingValidator namingValidator)
    {
        var apiTitle = document.Info?.Title ?? "Untitled API";
        var apiName = settings.ApiName ?? namingValidator.SanitizeApiName(apiTitle);
        var apiDisplayName = settings.ApiDisplayName ?? apiTitle;
        var operationPrefix = settings.OperationPrefix ?? namingValidator.SanitizeApiName(apiTitle);
        var env = settings.Environment!;

        var policy = settings.IncludeCorsPolicy
            ? BuildCorsPolicy(settings)
            : null;

        var apiPath = $"{settings.ApiPathPrefix}.{env}/{settings.ApiVersion}/{settings.ApiPathSuffix}";
        var serviceUrl = $"https://{settings.ApiGatewayHost}/{settings.ApiVersion}/{settings.BackendServicePath}/";

        var api = new ApimApi
        {
            ApimResourceGroupName = settings.StageGroupName!,
            ApimName = settings.ApimName!,
            Name = $"{apiName}-{env}",
            DisplayName = $"{apiDisplayName} - {env}",
            Path = ApimPlaceholders.ContainsPlaceholder(apiPath)
                ? apiPath
                : namingValidator.SanitizeApiPath(apiPath),
            ServiceUrl = serviceUrl,
            Protocols = ["https"],
            Revision = settings.Revision,
            SoapPassThrough = false,
            SubscriptionRequired = settings.SubscriptionRequired,
            ProductId = settings.ProductId,
            SubscriptionKeyParameterNames = null,
            Policy = policy
        };

        var operations = new List<ApimApiOperation>();

        if (document.Paths != null)
        {
            foreach (var pathItem in document.Paths)
            {
                var urlTemplate = ConvertPathToTemplate(pathItem.Key);

                foreach (var operation in pathItem.Value.Operations)
                {
                    var method = operation.Key.ToString().ToUpperInvariant();
                    var op = operation.Value;

                    var opIdRaw = op.OperationId
                        ?? $"{method.ToLowerInvariant()}-{urlTemplate.Replace("/", "-").Replace("{", "").Replace("}", "").Trim('-')}";

                    // Keep placeholder tags intact — the sanitizer would strip
                    // their braces. Only the spec-derived part is sanitized then.
                    var operationId = ApimPlaceholders.ContainsPlaceholder($"{operationPrefix}-{env}")
                        ? $"{operationPrefix}-{namingValidator.SanitizeOperationId(opIdRaw)}-{env}"
                        : namingValidator.SanitizeOperationId($"{operationPrefix}-{opIdRaw}-{env}");

                    var displayName = op.Summary ?? op.OperationId ?? $"{method} {pathItem.Key}";

                    operations.Add(new ApimApiOperation
                    {
                        OperationId = operationId,
                        ApimResourceGroupName = settings.StageGroupName!,
                        ApimName = settings.ApimName!,
                        ApiName = $"{apiName}-{env}",
                        DisplayName = displayName,
                        Method = method,
                        UrlTemplate = urlTemplate,
                        StatusCode = GetPrimarySuccessStatusCode(op),
                        Description = op.Description ?? "",
                        Requests = BuildRequests(op, pathItem.Value),
                        Responses = BuildResponses(op)
                    });
                }
            }
        }

        var products = new List<ApimProduct>();
        if (settings.GenerateProduct)
        {
            var productId = settings.ProductId ?? $"{apiName}-{env}";
            products.Add(new ApimProduct
            {
                ApimResourceGroupName = settings.StageGroupName!,
                ApimName = settings.ApimName!,
                ProductId = productId,
                DisplayName = settings.ProductDisplayName ?? $"{apiDisplayName} - {env}",
                SubscriptionRequired = settings.ProductSubscriptionRequired,
                ApprovalRequired = settings.ProductApprovalRequired,
                Published = true,
                SubscriptionsLimit = null,
                Description = settings.ProductDescription ?? ""
            });
        }

        return new ApimConfiguration
        {
            ApiGroupName = settings.ApiGroupName!,
            Products = products,
            Api = api,
            ApiOperations = operations
        };
    }

    /// <summary>
    /// Strips the leading slash from an OpenAPI path so it can be used directly
    /// as an APIM <c>url_template</c>. Both OpenAPI and APIM use the same
    /// <c>{param}</c> placeholder syntax, so no further transformation is needed.
    /// </summary>
    private static string ConvertPathToTemplate(string openApiPath)
    {
        var template = openApiPath.TrimStart('/');
        return string.IsNullOrEmpty(template) ? "/" : template;
    }

    /// <summary>
    /// Builds the APIM request model from an OpenAPI operation, collecting headers,
    /// query parameters, and request body representations (including complex
    /// $ref'd schemas — the reference id becomes schema_id/type_name).
    /// Path-level parameters are merged with operation-level parameters;
    /// operation-level takes precedence.
    /// </summary>
    private static List<ApimOperationRequest> BuildRequests(OpenApiOperation operation, OpenApiPathItem pathItem)
    {
        var headers = new List<ApimParameter>();
        var queryParams = new List<ApimParameter>();
        var representations = new List<ApimRepresentation>();

        var allParameters = (pathItem.Parameters ?? [])
            .Concat(operation.Parameters ?? [])
            .GroupBy(p => (p.Name, p.In))
            .Select(g => g.Last()) // operation-level overrides path-level
            .ToList();

        foreach (var param in allParameters)
        {
            var apimParam = new ApimParameter
            {
                Name = param.Name,
                Required = param.Required,
                Type = MapSchemaType(param.Schema),
                Description = param.Description ?? ""
            };

            switch (param.In)
            {
                case ParameterLocation.Header:
                    headers.Add(apimParam);
                    break;
                case ParameterLocation.Query:
                    queryParams.Add(apimParam);
                    break;
            }
        }

        // Operations with a security requirement get an Authorization header.
        if (operation.Security?.Count > 0 || headers.All(h => h.Name != "Authorization"))
        {
            var hasSecurityRequirement = operation.Security?.Any(s => s.Count > 0) == true;
            if (hasSecurityRequirement && headers.All(h => h.Name != "Authorization"))
            {
                headers.Insert(0, new ApimParameter
                {
                    Name = "Authorization",
                    Required = true,
                    Type = "string",
                    Description = "Authorization Header containing Oauth credentials"
                });
            }
        }

        if (operation.RequestBody?.Content != null)
        {
            foreach (var content in operation.RequestBody.Content)
            {
                representations.Add(new ApimRepresentation
                {
                    ContentType = content.Key,
                    SchemaId = content.Value.Schema?.Reference?.Id,
                    TypeName = content.Value.Schema?.Reference?.Id
                });
            }
        }

        if (headers.Count == 0 && queryParams.Count == 0 && representations.Count == 0)
            return [];

        return
        [
            new ApimOperationRequest
            {
                Headers = headers,
                QueryParameters = queryParams,
                Representations = representations
            }
        ];
    }

    /// <summary>
    /// Builds APIM response models from all numeric response codes defined in the
    /// OpenAPI operation, including their content-type representations.
    /// </summary>
    private static List<ApimOperationResponse> BuildResponses(OpenApiOperation operation)
    {
        var responses = new List<ApimOperationResponse>();

        if (operation.Responses == null) return responses;

        foreach (var response in operation.Responses)
        {
            if (!int.TryParse(response.Key, out var statusCode))
                continue;

            var representations = new List<ApimRepresentation>();

            if (response.Value.Content != null)
            {
                foreach (var content in response.Value.Content)
                {
                    representations.Add(new ApimRepresentation
                    {
                        ContentType = content.Key,
                        SchemaId = content.Value.Schema?.Reference?.Id,
                        TypeName = content.Value.Schema?.Reference?.Id
                    });
                }
            }

            responses.Add(new ApimOperationResponse
            {
                StatusCode = statusCode,
                Description = response.Value.Description ?? "",
                Representations = representations
            });
        }

        return responses;
    }

    /// <summary>
    /// Returns the first 2xx status code declared on the operation, falling back
    /// to 200. Used as the operation's primary <c>status_code</c>.
    /// </summary>
    private static int GetPrimarySuccessStatusCode(OpenApiOperation operation)
    {
        if (operation.Responses == null) return 200;

        foreach (var response in operation.Responses)
        {
            if (int.TryParse(response.Key, out var code) && code >= 200 && code < 300)
                return code;
        }

        return 200;
    }

    private static string MapSchemaType(OpenApiSchema? schema)
    {
        if (schema == null) return "string";

        return schema.Type switch
        {
            "integer" => "integer",
            "number" => "number",
            "boolean" => "boolean",
            "array" => "array",
            "object" => "object",
            _ => "string"
        };
    }

    /// <summary>
    /// Builds the CORS policy XML embedded in the Terraform <c>policy</c> heredoc.
    /// Returns an empty string when no allowed origins can be derived.
    /// </summary>
    private static string BuildCorsPolicy(ConversionSettings settings)
    {
        var origins = new List<string>();

        if (!string.IsNullOrEmpty(settings.FrontendHost) && !string.IsNullOrEmpty(settings.CompanyDomain))
        {
            origins.Add($"https://{settings.FrontendHost}.{settings.Environment}.{settings.CompanyDomain}");
        }

        if (!string.IsNullOrEmpty(settings.LocalDevHost) && !string.IsNullOrEmpty(settings.LocalDevPort))
        {
            origins.Add($"https://{settings.LocalDevHost}:{settings.LocalDevPort}");
        }

        origins.AddRange(settings.AllowedOrigins);

        if (origins.Count == 0) return "";

        var originsXml = string.Join("\n        ", origins.Select(o => $"<origin>{o}</origin>"));
        var methodsXml = string.Join("\n        ", settings.AllowedMethods.Select(m => $"<method>{m}</method>"));

        return $"""
            <policies>
              <inbound>
                <cors allow-credentials="true">
                  <allowed-origins>
                    {originsXml}
                  </allowed-origins>
                  <allowed-methods>
                    {methodsXml}
                  </allowed-methods>
                  <allowed-headers>
                    <header>*</header>
                  </allowed-headers>
                </cors>
              </inbound>
              <backend>
                <base />
              </backend>
              <outbound>
                <base />
              </outbound>
              <on-error>
                <base />
              </on-error>
            </policies>
            """;
    }
}
