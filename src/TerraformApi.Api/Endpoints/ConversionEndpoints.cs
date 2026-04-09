using Microsoft.OpenApi.Readers;
using TerraformApi.Api.Dtos;
using TerraformApi.Domain.Interfaces;

namespace TerraformApi.Api.Endpoints;

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
            .WithDescription("Updates an existing Terraform configuration with changes from an OpenAPI specification")
            .Produces<ConvertResponse>()
            .ProducesProblem(400);

        api.MapPost("/validate", Validate)
            .WithName("ValidateOpenApi")
            .WithDescription("Validates an OpenAPI JSON specification for APIM compatibility")
            .Produces<ValidateResponse>()
            .ProducesProblem(400);

        api.MapGet("/health", () => Results.Ok(new { status = "healthy", timestamp = DateTime.UtcNow }))
            .WithName("HealthCheck")
            .WithDescription("Health check endpoint");
    }

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
        catch (Exception ex)
        {
            errors.Add($"Validation failed: {ex.Message}");
            return Results.BadRequest(new ValidateResponse
            {
                IsValid = false,
                Errors = errors
            });
        }
    }
}
