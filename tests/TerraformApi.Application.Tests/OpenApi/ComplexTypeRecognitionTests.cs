using TerraformApi.Application.Services;
using TerraformApi.Application.Services.OpenApi;
using TerraformApi.Domain.Models;

namespace TerraformApi.Application.Tests.OpenApi;

/// <summary>
/// Complex-type recognition policy (see docs/openapi-complex-types.md):
/// well-defined shapes (direct $ref, allOf-wrapped single $ref, arrays of
/// $refs, single-branch oneOf/anyOf) are RECOGNIZED into schema_id/type_name;
/// ambiguous shapes (multi-branch unions, multi-$ref compositions, inline
/// schemas) are IGNORED by design — without ever failing the conversion.
/// Also covers the OpenAPI 3.1 compatibility mode of the reader.
/// </summary>
public class ComplexTypeRecognitionTests
{
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

    private static string Spec(string requestSchema, string responseSchema = """{ "type": "string" }""") => $$"""
        {
          "openapi": "3.0.1",
          "info": { "title": "T", "version": "1" },
          "components": {
            "schemas": {
              "Order":    { "type": "object", "properties": { "id": { "type": "string" } } },
              "Customer": { "type": "object", "properties": { "name": { "type": "string" } } }
            }
          },
          "paths": {
            "/orders": {
              "post": {
                "operationId": "createOrder",
                "requestBody": { "content": { "application/json": { "schema": {{requestSchema}} } } },
                "responses": {
                  "201": { "description": "Created", "content": { "application/json": { "schema": {{responseSchema}} } } }
                }
              }
            }
          }
        }
        """;

    private ApimRepresentation RequestRepresentation(string requestSchema, string responseSchema = """{ "type": "string" }""")
    {
        var config = _facade.Parse(Spec(requestSchema, responseSchema), Settings());
        return config.ApiOperations.Single().Requests.Single().Representations.Single();
    }

    // ---------------- RECOGNIZED shapes ----------------

    [Fact]
    public void DirectRef_Recognized()
    {
        var rep = RequestRepresentation("""{ "$ref": "#/components/schemas/Order" }""");
        Assert.Equal("Order", rep.SchemaId);
        Assert.Equal("Order", rep.TypeName);
    }

    [Fact]
    public void AllOfWrappedSingleRef_Recognized()
    {
        // Swashbuckle's nullable-reference pattern.
        var rep = RequestRepresentation(
            """{ "allOf": [ { "$ref": "#/components/schemas/Order" } ], "nullable": true }""");
        Assert.Equal("Order", rep.SchemaId);
        Assert.Equal("Order", rep.TypeName);
    }

    [Fact]
    public void ArrayOfRef_Recognized_WithArrayTypeName()
    {
        var rep = RequestRepresentation(
            """{ "type": "array", "items": { "$ref": "#/components/schemas/Order" } }""");
        Assert.Equal("Order", rep.SchemaId);
        Assert.Equal("Order[]", rep.TypeName);
    }

    [Fact]
    public void OneOfSingleBranch_Recognized()
    {
        var rep = RequestRepresentation(
            """{ "oneOf": [ { "$ref": "#/components/schemas/Order" } ] }""");
        Assert.Equal("Order", rep.SchemaId);
    }

    [Fact]
    public void ResponseRepresentations_UseSameRecognition()
    {
        var config = _facade.Parse(
            Spec("""{ "$ref": "#/components/schemas/Order" }""",
                 """{ "type": "array", "items": { "$ref": "#/components/schemas/Order" } }"""),
            Settings());

        var response = config.ApiOperations.Single().Responses.Single(r => r.StatusCode == 201);
        var rep = response.Representations.Single();
        Assert.Equal("Order", rep.SchemaId);
        Assert.Equal("Order[]", rep.TypeName);
    }

    // ---------------- IGNORED-by-design shapes ----------------

    [Fact]
    public void OneOfMultipleBranches_IgnoredButConversionSucceeds()
    {
        var rep = RequestRepresentation(
            """{ "oneOf": [ { "$ref": "#/components/schemas/Order" }, { "$ref": "#/components/schemas/Customer" } ] }""");
        Assert.Null(rep.SchemaId);                       // ambiguous union — no single name
        Assert.Equal("application/json", rep.ContentType); // but the representation survives
    }

    [Fact]
    public void AllOfMultipleRefs_IgnoredButConversionSucceeds()
    {
        var rep = RequestRepresentation(
            """{ "allOf": [ { "$ref": "#/components/schemas/Order" }, { "$ref": "#/components/schemas/Customer" } ] }""");
        Assert.Null(rep.SchemaId); // merged composite has no name
    }

    [Fact]
    public void InlineObject_IgnoredButConversionSucceeds()
    {
        var rep = RequestRepresentation(
            """{ "type": "object", "properties": { "x": { "type": "string" } } }""");
        Assert.Null(rep.SchemaId); // anonymous schema
    }

    // ---------------- OpenAPI 3.1 compatibility mode ----------------

    [Fact]
    public void OpenApi31Document_ReadInCompatibilityMode_WithWarning()
    {
        var spec31 = """
            {
              "openapi": "3.1.1",
              "info": { "title": "Net10 API", "version": "1.0.0" },
              "paths": {
                "/x": {
                  "get": { "operationId": "getX", "responses": { "200": { "description": "OK" } } }
                }
              }
            }
            """;

        var read = new OpenApiDocumentReader().Read(spec31);

        Assert.NotNull(read.Document);
        Assert.Contains(read.Warnings, w => w.Contains("3.1") && w.Contains("compatibility"));
        Assert.Single(read.Document!.Paths);
    }

    [Fact]
    public void OpenApi31Document_ConvertsEndToEnd_IncludingComplexRef()
    {
        // What .NET 10's built-in generator emits: 3.1 version + a $ref'd body.
        var spec31 = """
            {
              "openapi": "3.1.1",
              "info": { "title": "Net10 API", "version": "1.0.0" },
              "components": {
                "schemas": {
                  "Widget": { "type": "object", "properties": { "id": { "type": "string" } } }
                }
              },
              "paths": {
                "/widgets": {
                  "post": {
                    "operationId": "createWidget",
                    "requestBody": { "content": { "application/json": { "schema": { "$ref": "#/components/schemas/Widget" } } } },
                    "responses": { "201": { "description": "Created" } }
                  }
                }
              }
            }
            """;

        var config = _facade.Parse(spec31, Settings());

        var op = config.ApiOperations.Single();
        Assert.Equal("POST", op.Method);
        Assert.Equal("Widget", op.Requests.Single().Representations.Single().SchemaId);
    }

    [Fact]
    public void OpenApi31TypeArrays_ToleratedAsDiagnostics()
    {
        // 3.1 nullable syntax: "type": ["string", "null"] — invalid under 3.0
        // parsing, produces diagnostics, but the document stays usable.
        var spec31 = """
            {
              "openapi": "3.1.0",
              "info": { "title": "T", "version": "1" },
              "paths": {
                "/x": {
                  "get": {
                    "operationId": "getX",
                    "parameters": [
                      { "name": "q", "in": "query", "schema": { "type": ["string", "null"] } }
                    ],
                    "responses": { "200": { "description": "OK" } }
                  }
                }
              }
            }
            """;

        var config = _facade.Parse(spec31, Settings());

        var op = config.ApiOperations.Single();
        // The parameter survives with the fallback type.
        Assert.Contains(op.Requests.Single().QueryParameters, p => p.Name == "q");
    }

    [Fact]
    public void GarbageInput_StillThrowsContractException()
    {
        Assert.Throws<InvalidOperationException>(() => _facade.Parse("{ broken", Settings()));
    }
}
