using Microsoft.Extensions.Options;
using Microsoft.OpenApi.Readers;
using TerraformApi.Api.Dtos;
using TerraformApi.Domain.Interfaces;

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
    /// </summary>
    private static IResult GetEnvironments(IOptions<Dictionary<string, ApimEnvironmentConfig>> options)
    {
        return Results.Ok(options.Value);
    }

    /// <summary>
    /// Parses an OpenAPI JSON spec and generates a fresh APIM Terraform config block.
    /// </summary>
    private static IResult Convert(ConvertRequest request, IConversionOrchestrator orchestrator)
    {
        if (string.IsNullOrWhiteSpace(request.OpenApiJson))
            return Results.BadRequest(new { error = "OpenAPI JSON is required." });

        var settings = DtoMapper.ToSettings(request);
        var result = orchestrator.Convert(request.OpenApiJson, settings);

        return result.Success
            ? Results.Ok(DtoMapper.ToResponse(result))
            : Results.BadRequest(DtoMapper.ToResponse(result));
    }

    /// <summary>
    /// Merges a new OpenAPI spec into an existing Terraform config.
    /// Operations present in both are replaced; operations only in
    /// the existing config are preserved.
    /// </summary>
    private static IResult Update(UpdateRequest request, IConversionOrchestrator orchestrator)
    {
        if (string.IsNullOrWhiteSpace(request.OpenApiJson))
            return Results.BadRequest(new { error = "OpenAPI JSON is required." });

        if (string.IsNullOrWhiteSpace(request.ExistingTerraform))
            return Results.BadRequest(new { error = "Existing Terraform configuration is required." });

        var settings = DtoMapper.ToSettings(request);
        var result = orchestrator.Update(request.OpenApiJson, request.ExistingTerraform, settings);

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
    /// Validates an OpenAPI JSON document and reports APIM naming violations
    /// without generating any Terraform output.
    /// </summary>
    private static IResult Validate(ConvertRequest request, IOpenApiParser parser, IApimNamingValidator validator)
    {
        var errors = new List<string>();

        try
        {
            var reader = new OpenApiStringReader();
            var doc = reader.Read(request.OpenApiJson, out var diagnostic);

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
                        var validationResult = validator.ValidateOperationId(sanitized);

                        if (!validationResult.IsValid)
                            errors.AddRange(validationResult.Errors);

                        operations.Add(new OperationSummary
                        {
                            OperationId = sanitized,
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
        catch (Exception)
        {
            errors.Add("Validation failed: could not parse the provided OpenAPI document.");
            return Results.BadRequest(new ValidateResponse
            {
                IsValid = false,
                Errors = errors
            });
        }
    }
}
