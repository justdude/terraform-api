using Microsoft.OpenApi.Models;
using Microsoft.OpenApi.Readers;
using TerraformApi.Domain.Interfaces;
using TerraformApi.Domain.Models;

namespace TerraformApi.Application.Services;

public sealed class OpenApiParserService : IOpenApiParser
{
    private readonly IApimNamingValidator _namingValidator;

    public OpenApiParserService(IApimNamingValidator namingValidator)
    {
        _namingValidator = namingValidator;
    }

    public ApimConfiguration Parse(string openApiJson, ConversionSettings settings)
    {
        OpenApiDocument openApiDoc;
        OpenApiDiagnostic diagnostic;

        try
        {
            var reader = new OpenApiStringReader();
            openApiDoc = reader.Read(openApiJson, out diagnostic);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to parse OpenAPI document: {ex.Message}", ex);
        }

        if (diagnostic.Errors.Count > 0)
        {
            var errors = string.Join("; ", diagnostic.Errors.Select(e => e.Message));
            throw new InvalidOperationException($"Failed to parse OpenAPI document: {errors}");
        }

        var apiTitle = openApiDoc.Info?.Title ?? "Untitled API";
        var apiName = settings.ApiName ?? _namingValidator.SanitizeApiName(apiTitle);
        var apiDisplayName = settings.ApiDisplayName ?? apiTitle;
        var operationPrefix = settings.OperationPrefix ?? _namingValidator.SanitizeApiName(apiTitle);
        var env = settings.Environment;

        var policy = settings.IncludeCorsPolicy
            ? BuildCorsPolicy(settings)
            : null;

        var apiPath = $"{settings.ApiPathPrefix}.{env}/{settings.ApiVersion}/{settings.ApiPathSuffix}";
        var serviceUrl = $"https://{settings.ApiGatewayHost}/{settings.ApiVersion}/{settings.BackendServicePath}/";

        var api = new ApimApi
        {
            ApimResourceGroupName = settings.StageGroupName,
            ApimName = settings.ApimName,
            Name = $"{apiName}-{env}",
            DisplayName = $"{apiDisplayName} - {env}",
            Path = _namingValidator.SanitizeApiPath(apiPath),
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

        if (openApiDoc.Paths != null)
        {
            foreach (var pathItem in openApiDoc.Paths)
            {
                var urlTemplate = ConvertPathToTemplate(pathItem.Key);

                foreach (var operation in pathItem.Value.Operations)
                {
                    var method = operation.Key.ToString().ToUpperInvariant();
                    var op = operation.Value;

                    var opIdRaw = op.OperationId
                        ?? $"{method.ToLowerInvariant()}-{urlTemplate.Replace("/", "-").Replace("{", "").Replace("}", "").Trim('-')}";

                    var operationId = _namingValidator.SanitizeOperationId($"{operationPrefix}-{opIdRaw}-{env}");
                    var displayName = op.Summary ?? op.OperationId ?? $"{method} {pathItem.Key}";

                    var requests = BuildRequests(op, pathItem.Value);
                    var responses = BuildResponses(op);
                    var primaryStatusCode = GetPrimarySuccessStatusCode(op);

                    operations.Add(new ApimApiOperation
                    {
                        OperationId = operationId,
                        ApimResourceGroupName = settings.StageGroupName,
                        ApimName = settings.ApimName,
                        ApiName = $"{apiName}-{env}",
                        DisplayName = displayName,
                        Method = method,
                        UrlTemplate = urlTemplate,
                        StatusCode = primaryStatusCode,
                        Description = op.Description ?? "",
                        Requests = requests,
                        Responses = responses
                    });
                }
            }
        }

        return new ApimConfiguration
        {
            ApiGroupName = settings.ApiGroupName,
            Products = [],
            Api = api,
            ApiOperations = operations
        };
    }

    private static string ConvertPathToTemplate(string openApiPath)
    {
        // OpenAPI uses {param}, Terraform APIM uses the same format
        var template = openApiPath.TrimStart('/');
        return string.IsNullOrEmpty(template) ? "/" : template;
    }

    private static List<ApimOperationRequest> BuildRequests(OpenApiOperation operation, OpenApiPathItem pathItem)
    {
        var headers = new List<ApimParameter>();
        var queryParams = new List<ApimParameter>();
        var representations = new List<ApimRepresentation>();

        // Combine path-level and operation-level parameters
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

        // Check for security schemes that require Authorization header
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
