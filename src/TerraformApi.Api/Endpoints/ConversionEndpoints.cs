using Microsoft.Extensions.Options;
using Microsoft.OpenApi.Readers;
using TerraformApi.Api.Dtos;
using TerraformApi.Domain.Interfaces;
using TerraformApi.Domain.Models;

namespace TerraformApi.Api.Endpoints;

/// <summary>
/// Maps all API endpoints for the OpenAPI-to-Terraform conversion service.
/// Endpoints cover conversion, update (merge), validation, transform, environment presets, and health.
/// </summary>
public static class ConversionEndpoints
{
    public static void MapConversionEndpoints(this WebApplication app)
    {
        var api = app.MapGroup("/api");

        api.MapPost("/convert", Convert)
            .WithName("ConvertOpenApiToTerraform")
            .WithDescription("Converts an OpenAPI JSON specification to Azure APIM Terraform configuration")
            .Produces<ConvertResponse>()
            .ProducesProblem(400);

        api.MapPost("/convert/update", Update)
            .WithName("UpdateTerraformFromOpenApi")
            .WithDescription("Updates an existing Terraform configuration with changes from an OpenAPI specification, preserving custom operations")
            .Produces<ConvertResponse>()
            .ProducesProblem(400);

        api.MapPost("/transform-environment", TransformEnvironment)
            .WithName("TransformEnvironment")
            .WithDescription("Transforms a Terraform APIM configuration from one environment to another, " +
                             "optionally merging with an existing target environment's config. " +
                             "Operations are matched by url_template + HTTP method across environments.")
            .Produces<TransformResponse>()
            .ProducesProblem(400);

        api.MapPost("/fetch-operations", FetchOperations)
            .WithName("FetchOpenApiOperations")
            .WithDescription("Fetches an OpenAPI/Swagger specification from a URL and returns a structured " +
                             "operations list. Output format matches parse-terraform-operations for comparison.")
            .Produces<OperationsResponse>()
            .ProducesProblem(400);

        api.MapPost("/parse-terraform-operations", ParseTerraformOperations)
            .WithName("ParseTerraformOperations")
            .WithDescription("Parses a Terraform APIM configuration and returns a structured operations list " +
                             "in the same format as the OpenAPI operations endpoint, enabling side-by-side comparison")
            .Produces<OperationsResponse>()
            .ProducesProblem(400);

        api.MapPost("/validate", Validate)
            .WithName("ValidateOpenApi")
            .WithDescription("Validates an OpenAPI JSON specification against Azure APIM naming rules")
            .Produces<ValidateResponse>()
            .ProducesProblem(400);

        api.MapGet("/environments", GetEnvironments)
            .WithName("GetEnvironments")
            .WithDescription("Returns pre-configured APIM environment presets from appsettings.json");

        api.MapGet("/health", () => Results.Ok(new { status = "healthy", timestamp = DateTime.UtcNow }))
            .WithName("HealthCheck")
            .WithDescription("Health check endpoint");
    }

    /// <summary>
    /// Returns the dictionary of environment presets so the frontend
    /// can auto-fill settings when the user picks an environment.
    /// Optionally filters to a single environment by name.
    /// </summary>
    private static IResult GetEnvironments(
        IOptions<Dictionary<string, ApimEnvironmentConfig>> options, string? environmentName = null)
    {
        var environments = options.Value;

        if (environmentName is not null)
        {
            if (environments.TryGetValue(environmentName, out var config))
                return Results.Ok(new Dictionary<string, ApimEnvironmentConfig> { [environmentName] = config });

            return Results.NotFound(new
            {
                error = $"Environment '{environmentName}' not found.",
                available = environments.Keys.ToList()
            });
        }

        return Results.Ok(environments);
    }

    /// <summary>
    /// Resolves OpenAPI JSON from either direct input or a URL.
    /// Shared by Convert and Validate endpoints.
    /// </summary>
    private static async Task<string> ResolveOpenApiJson(
        string? openApiJson, string? openApiUrl, IHttpClientFactory httpClientFactory)
    {
        if (!string.IsNullOrWhiteSpace(openApiJson))
            return openApiJson;

        if (!string.IsNullOrWhiteSpace(openApiUrl))
        {
            if (!Uri.TryCreate(openApiUrl, UriKind.Absolute, out var uri) ||
                (uri.Scheme != "http" && uri.Scheme != "https"))
            {
                throw new InvalidOperationException(
                    $"Invalid URL: '{openApiUrl}'. Must be an absolute HTTP(S) URL.");
            }

            try
            {
                var client = httpClientFactory.CreateClient();
                var content = await client.GetStringAsync(uri);

                if (string.IsNullOrWhiteSpace(content))
                    throw new InvalidOperationException($"Empty response received from {openApiUrl}");

                return content;
            }
            catch (HttpRequestException ex)
            {
                throw new InvalidOperationException(
                    $"Failed to fetch OpenAPI specification from '{openApiUrl}': {ex.Message}", ex);
            }
            catch (TaskCanceledException)
            {
                throw new InvalidOperationException($"Request to '{openApiUrl}' timed out.");
            }
        }

        throw new InvalidOperationException("Either 'openApiJson' or 'openApiUrl' must be provided.");
    }

    /// <summary>
    /// Parses an OpenAPI JSON spec and generates a fresh APIM Terraform config block.
    /// Supports either direct JSON input or fetching from a URL.
    /// </summary>
    private static async Task<IResult> Convert(
        ConvertRequest request, IConversionOrchestrator orchestrator, IHttpClientFactory httpClientFactory)
    {
        string openApiJson;
        try
        {
            openApiJson = await ResolveOpenApiJson(request.OpenApiJson, request.OpenApiUrl, httpClientFactory);
        }
        catch (InvalidOperationException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }

        var settings = DtoMapper.ToSettings(request);
        var result = orchestrator.Convert(openApiJson, settings);

        return result.Success
            ? Results.Ok(DtoMapper.ToResponse(result))
            : Results.BadRequest(DtoMapper.ToResponse(result));
    }

    /// <summary>
    /// Merges a new OpenAPI spec into an existing Terraform config.
    /// Operations present in both are replaced; operations only in
    /// the existing config are preserved.
    /// </summary>
    private static async Task<IResult> Update(
        UpdateRequest request, IConversionOrchestrator orchestrator, IHttpClientFactory httpClientFactory)
    {
        if (string.IsNullOrWhiteSpace(request.ExistingTerraform))
            return Results.BadRequest(new { error = "Existing Terraform configuration is required." });

        string openApiJson;
        try
        {
            openApiJson = await ResolveOpenApiJson(request.OpenApiJson, request.OpenApiUrl, httpClientFactory);
        }
        catch (InvalidOperationException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }

        var settings = DtoMapper.ToSettings(request);
        var result = orchestrator.Update(openApiJson, request.ExistingTerraform, settings);

        return result.Success
            ? Results.Ok(DtoMapper.ToResponse(result))
            : Results.BadRequest(DtoMapper.ToResponse(result));
    }

    /// <summary>
    /// Transforms a source environment's Terraform to a target environment.
    /// Optionally merges with an existing target config, matching operations by
    /// url_template + HTTP method (not operation_id, since IDs differ across environments).
    /// </summary>
    private static IResult TransformEnvironment(TransformRequest request, IEnvironmentTransformer transformer)
    {
        if (string.IsNullOrWhiteSpace(request.SourceTerraform))
            return Results.BadRequest(new { error = "Source Terraform content is required." });

        var settings = DtoMapper.ToTransformSettings(request);
        var result = transformer.Transform(request.SourceTerraform, settings, request.ExistingTargetTerraform);

        return result.Success
            ? Results.Ok(DtoMapper.ToTransformResponse(result))
            : Results.BadRequest(DtoMapper.ToTransformResponse(result));
    }

    /// <summary>
    /// Fetches an OpenAPI spec from a URL and returns the structured operations list.
    /// Uses the shared <see cref="IOpenApiOperationsFetcher"/> for parsing.
    /// </summary>
    private static async Task<IResult> FetchOperations(
        FetchOperationsRequest request, IOpenApiOperationsFetcher fetcher, IHttpClientFactory httpClientFactory)
    {
        if (string.IsNullOrWhiteSpace(request.OpenApiUrl))
            return Results.BadRequest(new OperationsResponse { Success = false, Error = "OpenAPI URL is required." });

        if (!Uri.TryCreate(request.OpenApiUrl, UriKind.Absolute, out var uri) ||
            (uri.Scheme != "http" && uri.Scheme != "https"))
            return Results.BadRequest(new OperationsResponse
                { Success = false, Error = $"Invalid URL: '{request.OpenApiUrl}'. Must be an absolute HTTP(S) URL." });

        string json;
        try
        {
            var client = httpClientFactory.CreateClient();
            json = await client.GetStringAsync(uri);
        }
        catch (HttpRequestException ex)
        {
            return Results.BadRequest(new OperationsResponse
                { Success = false, Error = $"Failed to fetch OpenAPI spec from '{request.OpenApiUrl}': {ex.Message}" });
        }
        catch (TaskCanceledException)
        {
            return Results.BadRequest(new OperationsResponse
                { Success = false, Error = $"Request to '{request.OpenApiUrl}' timed out." });
        }

        var result = fetcher.ParseOperations(json, request.OpenApiUrl);
        return ToOperationsResult(result);
    }

    /// <summary>
    /// Parses a Terraform APIM configuration and returns the operations list
    /// in the same format as the OpenAPI fetch tool, enabling comparison.
    /// </summary>
    private static IResult ParseTerraformOperations(ParseTerraformRequest request, ITerraformOperationsParser parser)
    {
        if (string.IsNullOrWhiteSpace(request.Terraform))
            return Results.BadRequest(new OperationsResponse { Success = false, Error = "Terraform content is required." });

        var result = parser.Parse(request.Terraform);
        return ToOperationsResult(result);
    }

    /// <summary>
    /// Maps a unified <see cref="OperationsListResult"/> to an API response.
    /// Shared by both fetch-operations and parse-terraform-operations endpoints.
    /// </summary>
    private static IResult ToOperationsResult(OperationsListResult result)
    {
        if (!result.Success)
            return Results.BadRequest(new OperationsResponse { Success = false, Error = result.Error });

        return Results.Ok(new OperationsResponse
        {
            Success = true,
            Api = result.Api != null
                ? new OperationsApiInfoDto
                {
                    Title = result.Api.Title,
                    Version = result.Api.Version,
                    Description = result.Api.Description,
                    SourceUrl = result.Api.SourceUrl,
                    Name = result.Api.Name,
                    Path = result.Api.Path,
                    ServiceUrl = result.Api.ServiceUrl,
                    Environment = result.Api.Environment,
                    Source = result.Api.Source
                }
                : null,
            TotalOperations = result.TotalOperations,
            Operations = result.Operations.Select(op => new OperationDto
            {
                Method = op.Method,
                UrlTemplate = op.UrlTemplate,
                Path = op.Path,
                OperationId = op.OperationId,
                Description = op.Description,
                Tags = op.Tags,
                Parameters = op.Parameters?.Select(p => new ParameterDto
                {
                    Name = p.Name,
                    In = p.In,
                    Type = p.Type,
                    Required = p.Required,
                    Description = p.Description
                }).ToList(),
                RequestBodyContentTypes = op.RequestBodyContentTypes,
                ResponseCodes = op.ResponseCodes
            }).ToList()
        });
    }

    /// <summary>
    /// Validates an OpenAPI JSON document and reports APIM naming violations
    /// without generating any Terraform output.
    /// Supports either direct JSON input or fetching from a URL.
    /// </summary>
    private static async Task<IResult> Validate(
        ConvertRequest request, IOpenApiParser parser, IApimNamingValidator validator, IHttpClientFactory httpClientFactory)
    {
        var errors = new List<string>();

        try
        {
            string openApiJson;
            try
            {
                openApiJson = await ResolveOpenApiJson(request.OpenApiJson, request.OpenApiUrl, httpClientFactory);
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new ValidateResponse
                {
                    IsValid = false,
                    Errors = [ex.Message]
                });
            }

            var reader = new OpenApiStringReader();
            var doc = reader.Read(openApiJson, out var diagnostic);

            if (diagnostic.Errors.Count > 0)
            {
                errors.AddRange(diagnostic.Errors.Select(e => e.Message));
                // Fatal parse errors (e.g. completely invalid JSON) result in no
                // usable document, so return 400 immediately rather than 200+IsValid=false.
                if (doc?.Paths == null)
                {
                    return Results.BadRequest(new ValidateResponse
                    {
                        IsValid = false,
                        Errors = errors
                    });
                }
            }

            if (doc?.Paths != null)
            {
                var operations = new List<OperationSummary>();
                foreach (var path in doc.Paths)
                {
                    foreach (var op in path.Value.Operations)
                    {
                        var opId = op.Value.OperationId ?? $"{op.Key}-{path.Key}";
                        var sanitized = validator.SanitizeOperationId(opId);
                        var sanitizedWithEnv = $"{sanitized}-{request.Environment}";
                        var validationResult = validator.ValidateOperationId(sanitizedWithEnv);

                        if (!validationResult.IsValid)
                            errors.AddRange(validationResult.Errors);

                        var displayName = op.Value.Summary ?? op.Value.OperationId ?? path.Key;
                        var displayResult = validator.ValidateDisplayName(displayName);

                        if (!displayResult.IsValid)
                            errors.AddRange(displayResult.Errors);

                        operations.Add(new OperationSummary
                        {
                            OperationId = sanitizedWithEnv,
                            Method = op.Key.ToString().ToUpperInvariant(),
                            UrlTemplate = path.Key.TrimStart('/')
                        });
                    }
                }

                return Results.Ok(new ValidateResponse
                {
                    IsValid = errors.Count == 0,
                    Errors = errors,
                    Summary = new ApiSummary
                    {
                        ApiName = doc.Info?.Title ?? "Unknown",
                        DisplayName = doc.Info?.Title ?? "Unknown",
                        OperationCount = operations.Count,
                        Operations = operations
                    }
                });
            }

            return Results.Ok(new ValidateResponse
            {
                IsValid = errors.Count == 0,
                Errors = errors
            });
        }
        catch (Exception ex)
        {
            errors.Add($"Validation failed: could not parse the provided OpenAPI document. {ex.Message}");
            return Results.BadRequest(new ValidateResponse
            {
                IsValid = false,
                Errors = errors
            });
        }
    }
}
