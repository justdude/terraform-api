using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi.Readers;
using TerraformApi.Api.Dtos;
using TerraformApi.Application.Services;
using TerraformApi.Domain.Interfaces;
using TerraformApi.Domain.Models;

namespace TerraformApi.Api.Controllers;

/// <summary>
/// OpenAPI-to-Terraform conversion endpoints: convert, update (merge),
/// environment transform, operations listing, validation, environment presets
/// and health.
/// </summary>
[ApiController]
[Route("api")]
[Produces("application/json")]
public sealed class ConversionController : ControllerBase
{
    private readonly IConversionOrchestrator _orchestrator;
    private readonly IEnvironmentTransformer _transformer;
    private readonly IOpenApiOperationsFetcher _operationsFetcher;
    private readonly ITerraformOperationsParser _terraformParser;
    private readonly IOpenApiParser _openApiParser;
    private readonly IApimNamingValidator _namingValidator;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptions<Dictionary<string, ApimEnvironmentConfig>> _environments;

    public ConversionController(
        IConversionOrchestrator orchestrator,
        IEnvironmentTransformer transformer,
        IOpenApiOperationsFetcher operationsFetcher,
        ITerraformOperationsParser terraformParser,
        IOpenApiParser openApiParser,
        IApimNamingValidator namingValidator,
        IHttpClientFactory httpClientFactory,
        IOptions<Dictionary<string, ApimEnvironmentConfig>> environments)
    {
        _orchestrator = orchestrator;
        _transformer = transformer;
        _operationsFetcher = operationsFetcher;
        _terraformParser = terraformParser;
        _openApiParser = openApiParser;
        _namingValidator = namingValidator;
        _httpClientFactory = httpClientFactory;
        _environments = environments;
    }

    /// <summary>
    /// Converts an OpenAPI JSON specification to an Azure APIM Terraform configuration.
    /// </summary>
    /// <remarks>Provide either <c>openApiJson</c> directly or <c>openApiUrl</c> to fetch from.</remarks>
    [HttpPost("convert")]
    [ProducesResponseType(typeof(ConvertResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ConvertResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Convert([FromBody] ConvertRequest request)
    {
        string openApiJson;
        try
        {
            openApiJson = await OpenApiDocumentResolver.ResolveAsync(
                _httpClientFactory.CreateClient(), request.OpenApiJson, request.OpenApiUrl, HttpContext.RequestAborted);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }

        var settings = DtoMapper.ToSettings(request);
        var result = _orchestrator.Convert(openApiJson, settings);

        return result.Success
            ? Ok(DtoMapper.ToResponse(result))
            : BadRequest(DtoMapper.ToResponse(result));
    }

    /// <summary>
    /// Merges a new OpenAPI spec into an existing Terraform configuration,
    /// preserving custom operations not present in the updated spec.
    /// </summary>
    [HttpPost("convert/update")]
    [ProducesResponseType(typeof(ConvertResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ConvertResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Update([FromBody] UpdateRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.ExistingTerraform))
            return BadRequest(new { error = "Existing Terraform configuration is required." });

        string openApiJson;
        try
        {
            openApiJson = await OpenApiDocumentResolver.ResolveAsync(
                _httpClientFactory.CreateClient(), request.OpenApiJson, request.OpenApiUrl, HttpContext.RequestAborted);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }

        var settings = DtoMapper.ToSettings(request);
        var result = _orchestrator.Update(openApiJson, request.ExistingTerraform, settings);

        return result.Success
            ? Ok(DtoMapper.ToResponse(result))
            : BadRequest(DtoMapper.ToResponse(result));
    }

    /// <summary>
    /// Transforms a Terraform APIM configuration from one environment to another,
    /// optionally merging with an existing target environment's config.
    /// Operations are matched by url_template + HTTP method across environments.
    /// </summary>
    [HttpPost("transform-environment")]
    [ProducesResponseType(typeof(TransformResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(TransformResponse), StatusCodes.Status400BadRequest)]
    public IActionResult TransformEnvironment([FromBody] TransformRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.SourceTerraform))
            return BadRequest(new { error = "Source Terraform content is required." });

        var settings = DtoMapper.ToTransformSettings(request);
        var result = _transformer.Transform(request.SourceTerraform, settings, request.ExistingTargetTerraform);

        return result.Success
            ? Ok(DtoMapper.ToTransformResponse(result))
            : BadRequest(DtoMapper.ToTransformResponse(result));
    }

    /// <summary>
    /// Fetches an OpenAPI/Swagger specification from a URL and returns a
    /// structured operations list. Output format matches
    /// /api/parse-terraform-operations for side-by-side comparison.
    /// </summary>
    [HttpPost("fetch-operations")]
    [ProducesResponseType(typeof(OperationsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(OperationsResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> FetchOperations([FromBody] FetchOperationsRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.OpenApiUrl))
            return BadRequest(new OperationsResponse { Success = false, Error = "OpenAPI URL is required." });

        if (!Uri.TryCreate(request.OpenApiUrl, UriKind.Absolute, out var uri) ||
            (uri.Scheme != "http" && uri.Scheme != "https"))
        {
            return BadRequest(new OperationsResponse
            { Success = false, Error = $"Invalid URL: '{request.OpenApiUrl}'. Must be an absolute HTTP(S) URL." });
        }

        string json;
        try
        {
            var client = _httpClientFactory.CreateClient();
            json = await client.GetStringAsync(uri, HttpContext.RequestAborted);
        }
        catch (HttpRequestException ex)
        {
            return BadRequest(new OperationsResponse
            { Success = false, Error = $"Failed to fetch OpenAPI spec from '{request.OpenApiUrl}': {ex.Message}" });
        }
        catch (TaskCanceledException)
        {
            return BadRequest(new OperationsResponse
            { Success = false, Error = $"Request to '{request.OpenApiUrl}' timed out." });
        }

        var result = _operationsFetcher.ParseOperations(json, request.OpenApiUrl);
        return ToOperationsResult(result);
    }

    /// <summary>
    /// Parses a Terraform APIM configuration and returns a structured operations
    /// list in the same format as /api/fetch-operations.
    /// </summary>
    [HttpPost("parse-terraform-operations")]
    [ProducesResponseType(typeof(OperationsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(OperationsResponse), StatusCodes.Status400BadRequest)]
    public IActionResult ParseTerraformOperations([FromBody] ParseTerraformRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Terraform))
            return BadRequest(new OperationsResponse { Success = false, Error = "Terraform content is required." });

        var result = _terraformParser.Parse(request.Terraform);
        return ToOperationsResult(result);
    }

    /// <summary>
    /// Validates an OpenAPI JSON specification against Azure APIM naming rules
    /// without generating Terraform output.
    /// </summary>
    [HttpPost("validate")]
    [ProducesResponseType(typeof(ValidateResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidateResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Validate([FromBody] ConvertRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Environment))
        {
            return BadRequest(new ValidateResponse
            {
                IsValid = false,
                Errors = ["Environment is required."]
            });
        }

        var errors = new List<string>();

        try
        {
            string openApiJson;
            try
            {
                openApiJson = await OpenApiDocumentResolver.ResolveAsync(
                    _httpClientFactory.CreateClient(), request.OpenApiJson, request.OpenApiUrl, HttpContext.RequestAborted);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new ValidateResponse
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
                    return BadRequest(new ValidateResponse
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
                        var sanitized = _namingValidator.SanitizeOperationId(opId);
                        var sanitizedWithEnv = $"{sanitized}-{request.Environment}";
                        var validationResult = _namingValidator.ValidateOperationId(sanitizedWithEnv);

                        if (!validationResult.IsValid)
                            errors.AddRange(validationResult.Errors);

                        var displayName = op.Value.Summary ?? op.Value.OperationId ?? path.Key;
                        var displayResult = _namingValidator.ValidateDisplayName(displayName);

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

                return Ok(new ValidateResponse
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

            return Ok(new ValidateResponse
            {
                IsValid = errors.Count == 0,
                Errors = errors
            });
        }
        catch (Exception ex)
        {
            errors.Add($"Validation failed: could not parse the provided OpenAPI document. {ex.Message}");
            return BadRequest(new ValidateResponse
            {
                IsValid = false,
                Errors = errors
            });
        }
    }

    /// <summary>
    /// Returns the configured APIM environment presets so clients can auto-fill
    /// settings. Optionally filters to a single environment by name.
    /// </summary>
    [HttpGet("environments")]
    [ProducesResponseType(typeof(Dictionary<string, ApimEnvironmentConfig>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public IActionResult GetEnvironments([FromQuery] string? environmentName = null)
    {
        var environments = _environments.Value;

        if (environmentName is not null)
        {
            if (environments.TryGetValue(environmentName, out var config))
                return Ok(new Dictionary<string, ApimEnvironmentConfig> { [environmentName] = config });

            return NotFound(new
            {
                error = $"Environment '{environmentName}' not found.",
                available = environments.Keys.ToList()
            });
        }

        return Ok(environments);
    }

    /// <summary>Health check.</summary>
    [HttpGet("health")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult Health() =>
        Ok(new { status = "healthy", timestamp = DateTime.UtcNow });

    /// <summary>
    /// Maps the unified <see cref="OperationsListResult"/> to the API response.
    /// Shared by fetch-operations and parse-terraform-operations.
    /// </summary>
    private IActionResult ToOperationsResult(OperationsListResult result)
    {
        if (!result.Success)
            return BadRequest(new OperationsResponse { Success = false, Error = result.Error });

        return Ok(new OperationsResponse
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
}
