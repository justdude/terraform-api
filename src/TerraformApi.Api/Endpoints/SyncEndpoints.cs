using System.Text.Json;
using System.Text.Json.Serialization;
using TerraformApi.Api.Dtos;
using TerraformApi.Domain.Interfaces;
using TerraformApi.Domain.Models.Sync;

namespace TerraformApi.Api.Endpoints;

/// <summary>
/// Endpoints for the append-only sync engine:
/// POST /api/sync, POST /api/analyze-terraform, POST /api/apply-template-profile.
/// </summary>
public static class SyncEndpoints
{
    /// <summary>Domain enums (diff kinds, severities, confidence) serialize as strings.</summary>
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static void MapSyncEndpoints(this WebApplication app)
    {
        var api = app.MapGroup("/api");

        api.MapPost("/sync", Sync)
            .WithName("SyncOpenApiWithTerraform")
            .WithDescription("Append-only sync of an existing Terraform config with an OpenAPI spec: " +
                             "new operations are appended in the file's detected templating style, " +
                             "existing operations are never modified or removed (configurable per-field policy). " +
                             "Empty existingTerraform generates a fresh config from the template profile.")
            .ProducesProblem(400);

        api.MapPost("/analyze-terraform", Analyze)
            .WithName("AnalyzeTerraformApim")
            .WithDescription("Analyzes an existing APIM Terraform config without modifying it: " +
                             "API groups, operation counts, detected templating profile, " +
                             "referenced variables, and duplicate operations.")
            .ProducesProblem(400);

        api.MapPost("/apply-template-profile", ApplyProfile)
            .WithName("ApplyTemplateProfile")
            .WithDescription("One-time conversion between literal and templated styles: " +
                             "direction 'Templatize' replaces literals with profile placeholders, " +
                             "'Resolve' substitutes variable values into placeholders.")
            .ProducesProblem(400);
    }

    private static IResult Sync(SyncRequestDto request, ISyncOrchestrator orchestrator)
    {
        if (string.IsNullOrWhiteSpace(request.OpenApiJson))
            return Results.BadRequest(new { error = "OpenAPI JSON is required." });

        ApimTemplateProfile? overrideProfile = null;
        if (request.TemplateProfileName is { Length: > 0 } name && name != "Auto")
        {
            overrideProfile = ApimTemplateProfile.GetByName(name);
            if (overrideProfile is null)
            {
                return Results.BadRequest(new
                {
                    error = $"Unknown template profile '{name}'.",
                    available = new[] { "Auto", "UserExampleProfile", "ExtendedProfile", "LiteralProfile" }
                });
            }
        }

        MergePolicy? policy = null;
        if (request.OperationFieldOverrides is { Count: > 0 })
        {
            policy = new MergePolicy();
            foreach (var (field, value) in request.OperationFieldOverrides)
            {
                if (!Enum.TryParse<FieldMergePolicy>(value, ignoreCase: true, out var fieldPolicy))
                    return Results.BadRequest(new { error = $"Unknown field policy '{value}' for '{field}'." });
                policy = policy.WithOverride(field, fieldPolicy);
            }
        }

        OperationMatchStrategy? strategy = null;
        if (request.MatchKeys is { Count: > 0 } || request.VariableContext is { Count: > 0 })
        {
            var keys = new List<OperationMatchKey>();
            foreach (var key in request.MatchKeys ?? ["MethodAndUrl", "OperationId", "Tag"])
            {
                if (!Enum.TryParse<OperationMatchKey>(key, ignoreCase: true, out var matchKey))
                    return Results.BadRequest(new { error = $"Unknown match key '{key}'." });
                keys.Add(matchKey);
            }
            strategy = new OperationMatchStrategy
            {
                Keys = keys,
                VariableContext = request.VariableContext
            };
        }

        var result = orchestrator.Sync(new SyncRequest
        {
            OpenApiJson = request.OpenApiJson,
            ExistingTerraform = request.ExistingTerraform,
            Settings = DtoMapper.ToSettings(request),
            MergePolicy = policy,
            MatchStrategy = strategy,
            Options = new SyncOptions
            {
                OverrideProfile = overrideProfile,
                AddOperationComments = request.AddOperationComments,
                AddReplaceBeforeApplyHeader = request.AddReplaceBeforeApplyHeader
            }
        });

        return result.Success
            ? Results.Json(result, JsonOptions)
            : Results.Json(result, JsonOptions, statusCode: 400);
    }

    private static IResult Analyze(AnalyzeTerraformRequest request, ISyncOrchestrator orchestrator)
    {
        var result = orchestrator.Analyze(request.Terraform);
        return result.Success
            ? Results.Json(result, JsonOptions)
            : Results.Json(result, JsonOptions, statusCode: 400);
    }

    private static IResult ApplyProfile(ApplyTemplateProfileRequest request, ISyncOrchestrator orchestrator)
    {
        var resolve = request.Direction.Equals("Resolve", StringComparison.OrdinalIgnoreCase);
        if (!resolve && !request.Direction.Equals("Templatize", StringComparison.OrdinalIgnoreCase))
            return Results.BadRequest(new { error = "Direction must be 'Templatize' or 'Resolve'." });

        ApimTemplateProfile? profile = null;
        if (!resolve)
        {
            profile = ApimTemplateProfile.GetByName(request.ProfileName ?? "");
            if (profile is null)
            {
                return Results.BadRequest(new
                {
                    error = $"Unknown template profile '{request.ProfileName}'.",
                    available = new[] { "UserExampleProfile", "ExtendedProfile", "LiteralProfile" }
                });
            }
        }

        var result = orchestrator.ApplyProfile(
            request.ExistingTerraform,
            profile,
            new ApplyProfileOptions { OverwriteExisting = request.OverwriteExisting },
            request.VariableContext,
            resolve);

        return result.Success
            ? Results.Json(result, JsonOptions)
            : Results.Json(result, JsonOptions, statusCode: 400);
    }
}
