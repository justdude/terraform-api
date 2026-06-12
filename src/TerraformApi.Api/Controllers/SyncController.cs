using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using TerraformApi.Api.Dtos;
using TerraformApi.Domain.Interfaces;
using TerraformApi.Domain.Models.Sync;

namespace TerraformApi.Api.Controllers;

/// <summary>
/// Append-only sync engine endpoints: sync, read-only analysis, and template
/// profile application (Templatize ⇄ Resolve).
/// </summary>
[ApiController]
[Route("api")]
[Produces("application/json")]
public sealed class SyncController : ControllerBase
{
    /// <summary>Domain enums (diff kinds, severities, confidence) serialize as strings.</summary>
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly ISyncOrchestrator _orchestrator;

    public SyncController(ISyncOrchestrator orchestrator) => _orchestrator = orchestrator;

    /// <summary>
    /// Append-only sync of an existing Terraform config with an OpenAPI spec.
    /// </summary>
    /// <remarks>
    /// New operations are appended in the file's detected templating style;
    /// existing operations are never modified or removed (configurable per-field
    /// policy). Empty <c>existingTerraform</c> generates a fresh config from the
    /// template profile.
    /// </remarks>
    [HttpPost("sync")]
    [ProducesResponseType(typeof(SyncResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(SyncResult), StatusCodes.Status400BadRequest)]
    public IActionResult Sync([FromBody] SyncRequestDto request)
    {
        if (string.IsNullOrWhiteSpace(request.OpenApiJson))
            return BadRequest(new { error = "OpenAPI JSON is required." });

        ApimTemplateProfile? overrideProfile = null;
        if (request.TemplateProfileName is { Length: > 0 } name && name != "Auto")
        {
            overrideProfile = ApimTemplateProfile.GetByName(name);
            if (overrideProfile is null)
            {
                return BadRequest(new
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
                    return BadRequest(new { error = $"Unknown field policy '{value}' for '{field}'." });
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
                    return BadRequest(new { error = $"Unknown match key '{key}'." });
                keys.Add(matchKey);
            }
            strategy = new OperationMatchStrategy
            {
                Keys = keys,
                VariableContext = request.VariableContext
            };
        }

        var result = _orchestrator.Sync(new SyncRequest
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

        return Json(result, result.Success ? StatusCodes.Status200OK : StatusCodes.Status400BadRequest);
    }

    /// <summary>
    /// Analyzes an existing APIM Terraform config without modifying it:
    /// API groups, operation counts, detected templating profile, referenced
    /// variables, and duplicate operations.
    /// </summary>
    [HttpPost("analyze-terraform")]
    [ProducesResponseType(typeof(AnalyzeResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(AnalyzeResult), StatusCodes.Status400BadRequest)]
    public IActionResult Analyze([FromBody] AnalyzeTerraformRequest request)
    {
        var result = _orchestrator.Analyze(request.Terraform);
        return Json(result, result.Success ? StatusCodes.Status200OK : StatusCodes.Status400BadRequest);
    }

    /// <summary>
    /// One-time conversion between literal and templated styles:
    /// direction <c>Templatize</c> replaces literals with profile placeholders,
    /// <c>Resolve</c> substitutes variable values into placeholders.
    /// </summary>
    [HttpPost("apply-template-profile")]
    [ProducesResponseType(typeof(ApplyProfileResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApplyProfileResult), StatusCodes.Status400BadRequest)]
    public IActionResult ApplyProfile([FromBody] ApplyTemplateProfileRequest request)
    {
        var resolve = request.Direction.Equals("Resolve", StringComparison.OrdinalIgnoreCase);
        if (!resolve && !request.Direction.Equals("Templatize", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { error = "Direction must be 'Templatize' or 'Resolve'." });

        ApimTemplateProfile? profile = null;
        if (!resolve)
        {
            profile = ApimTemplateProfile.GetByName(request.ProfileName ?? "");
            if (profile is null)
            {
                return BadRequest(new
                {
                    error = $"Unknown template profile '{request.ProfileName}'.",
                    available = new[] { "UserExampleProfile", "ExtendedProfile", "LiteralProfile" }
                });
            }
        }

        var result = _orchestrator.ApplyProfile(
            request.ExistingTerraform,
            profile,
            new ApplyProfileOptions { OverwriteExisting = request.OverwriteExisting },
            request.VariableContext,
            resolve);

        return Json(result, result.Success ? StatusCodes.Status200OK : StatusCodes.Status400BadRequest);
    }

    /// <summary>Serializes with the sync engine's enum-as-string options.</summary>
    private JsonResult Json(object value, int statusCode) =>
        new(value, JsonOptions) { StatusCode = statusCode };
}
