using TerraformApi.Application.Services;
using TerraformApi.Application.Services.OpenApi;
using TerraformApi.Domain.Models;

namespace TerraformApi.Application.Tests.OpenApi;

/// <summary>
/// Regression tests for the OpenAPI 3.1 compatibility-mode reader, the
/// warning-surfacing pipeline, and the tolerant-parsing gate — the defects
/// confirmed by the adversarial review of the complex-type/3.1 change.
/// </summary>
public class OpenApiReaderCompatibilityTests
{
    private readonly OpenApiDocumentReader _reader = new();
    private readonly OpenApiFacadeService _facade = new(new ApimNamingValidatorService());

    private static ConversionSettings Settings() => new()
    {
        Environment = "dev",
        ApiGroupName = "g",
        StageGroupName = "rg-apim-dev",
        ApimName = "apim",
        ApiPathPrefix = "x",
        ApiPathSuffix = "api",
        ApiGatewayHost = "host",
        BackendServicePath = "svc"
    };

    private static ConversionOrchestratorService BuildOrchestrator()
    {
        var validator = new ApimNamingValidatorService();
        var facade = new OpenApiFacadeService(validator);
        var generator = new TerraformGeneratorService();
        return new ConversionOrchestratorService(facade, generator, new TerraformMergerService(generator), validator);
    }

    // ---------------- 3.1 reader: format-agnostic + root-anchored ----------------

    [Fact]
    public void Read_Yaml31Document_DowngradedAndParsed()
    {
        var yaml = """
            openapi: 3.1.0
            info:
              title: YAML API
              version: 1.0.0
            paths:
              /ping:
                get:
                  operationId: ping
                  responses:
                    '200':
                      description: ok
            """;

        var read = _reader.Read(yaml);

        Assert.NotNull(read.Document);
        Assert.True(read.DowngradedFrom31);
        Assert.Single(read.Document!.Paths);
        Assert.Contains(read.Warnings, w => w.Contains("compatibility"));
    }

    [Fact]
    public void Read_Genuine30Doc_WithEmbedded31Example_NotDowngraded()
    {
        // A real 3.0 doc whose request example literally contains "openapi":"3.1.0".
        // The root version is 3.0.0 — no downgrade, no spurious warning.
        var spec = """
            {
              "openapi": "3.0.0",
              "info": { "title": "T", "version": "1" },
              "paths": {
                "/x": {
                  "post": {
                    "operationId": "postX",
                    "requestBody": { "content": { "application/json": { "example": { "openapi": "3.1.0" } } } },
                    "responses": { "200": { "description": "ok" } }
                  }
                }
              }
            }
            """;

        var read = _reader.Read(spec);

        Assert.False(read.DowngradedFrom31);
        Assert.Empty(read.Warnings);
        Assert.NotNull(read.Document);
    }

    [Fact]
    public void Read_Json31Document_SetsDowngradeFlagAndWarning()
    {
        var read = _reader.Read("""
            { "openapi": "3.1.1", "info": { "title": "T", "version": "1" },
              "paths": { "/x": { "get": { "operationId": "getX", "responses": { "200": { "description": "ok" } } } } } }
            """);

        Assert.True(read.DowngradedFrom31);
        Assert.Contains(read.Warnings, w => w.Contains("3.1"));
    }

    // ---------------- warning surfacing through the pipeline ----------------

    [Fact]
    public void Parse_31Document_SurfacesWarningOnConfiguration()
    {
        var config = _facade.Parse("""
            { "openapi": "3.1.0", "info": { "title": "T", "version": "1" },
              "paths": { "/x": { "get": { "operationId": "getX", "responses": { "200": { "description": "ok" } } } } } }
            """, Settings());

        Assert.Contains(config.Warnings, w => w.Contains("compatibility"));
    }

    [Fact]
    public void Convert_31Document_WarningReachesConversionResult()
    {
        var result = BuildOrchestrator().Convert("""
            { "openapi": "3.1.0", "info": { "title": "T", "version": "1" },
              "paths": { "/x": { "get": { "operationId": "getX", "summary": "Get", "responses": { "200": { "description": "ok" } } } } } }
            """, Settings());

        Assert.True(result.Success, string.Join("; ", result.Errors));
        Assert.Contains(result.Warnings, w => w.Contains("compatibility"));
    }

    [Fact]
    public void Convert_30Document_HasNoCompatibilityWarning()
    {
        var result = BuildOrchestrator().Convert("""
            { "openapi": "3.0.1", "info": { "title": "T", "version": "1" },
              "paths": { "/x": { "get": { "operationId": "getX", "summary": "Get", "responses": { "200": { "description": "ok" } } } } } }
            """, Settings());

        Assert.True(result.Success);
        Assert.DoesNotContain(result.Warnings, w => w.Contains("compatibility"));
    }

    [Fact]
    public void ParseOperations_31Document_SurfacesWarning()
    {
        var result = _facade.ParseOperations("""
            { "openapi": "3.1.0", "info": { "title": "T", "version": "1" },
              "paths": { "/x": { "get": { "operationId": "getX", "responses": { "200": { "description": "ok" } } } } } }
            """);

        Assert.True(result.Success);
        Assert.Contains(result.Warnings, w => w.Contains("compatibility"));
    }

    // ---------------- tolerant-parsing gate ----------------

    [Fact]
    public void Parse_30DocWithMissingParameterName_Throws()
    {
        // Genuine 3.0 spec violation (parameter 'name' is REQUIRED) must NOT be
        // tolerated — it would otherwise emit invalid Terraform.
        var yaml = """
            openapi: 3.0.0
            info: { title: T, version: '1' }
            paths:
              /foo:
                get:
                  operationId: getFoo
                  parameters:
                    - in: query
                      schema: { type: string }
                  responses:
                    '200': { description: ok }
            """;

        var ex = Assert.Throws<InvalidOperationException>(() => _facade.Parse(yaml, Settings()));
        Assert.StartsWith("Failed to parse OpenAPI document:", ex.Message);
    }

    [Fact]
    public void Parse_31DocWithNamelessParameter_ToleratedButParameterSkipped()
    {
        // A downgraded 3.1 doc is tolerated; the nameless parameter is skipped
        // by the builder guard so no invalid `name = ""` is emitted.
        var spec = """
            {
              "openapi": "3.1.0",
              "info": { "title": "T", "version": "1" },
              "paths": {
                "/foo": {
                  "get": {
                    "operationId": "getFoo",
                    "parameters": [ { "in": "query", "schema": { "type": "string" } } ],
                    "responses": { "200": { "description": "ok" } }
                  }
                }
              }
            }
            """;

        var config = _facade.Parse(spec, Settings());
        var op = config.ApiOperations.Single();

        // No request block at all (the only param was nameless and skipped).
        Assert.DoesNotContain(op.Requests, r => r.QueryParameters.Any(p => string.IsNullOrEmpty(p.Name)));
    }

    [Fact]
    public void Parse_31DocWithTypeArrays_ToleratedAndConverts()
    {
        // 3.1 nullable type arrays produce a diagnostic but must still convert.
        var spec = """
            {
              "openapi": "3.1.0",
              "info": { "title": "T", "version": "1" },
              "paths": {
                "/x": {
                  "get": {
                    "operationId": "getX",
                    "parameters": [ { "name": "q", "in": "query", "schema": { "type": ["string", "null"] } } ],
                    "responses": { "200": { "description": "ok" } }
                  }
                }
              }
            }
            """;

        var config = _facade.Parse(spec, Settings());
        Assert.Contains(config.ApiOperations.Single().Requests.Single().QueryParameters, p => p.Name == "q");
    }
}
