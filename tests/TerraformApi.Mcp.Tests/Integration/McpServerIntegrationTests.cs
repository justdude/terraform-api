using System.Text.Json;

namespace TerraformApi.Mcp.Tests.Integration;

/// <summary>
/// End-to-end tests against the REAL MCP server process over stdio JSON-RPC —
/// the same transport an AI client (VS Code, Claude Desktop) or a CI automation
/// script uses. Each test encodes one use case:
///
///  UC1  Tool discovery        — a client connects and lists all 11 tools.
///  UC2  Post-build conversion — CI converts a freshly built service's
///                               swagger.json with NO settings; placeholder
///                               tags make the output usable immediately.
///  UC3  Full conversion       — developer converts with complete settings.
///  UC4  Analyze → sync flow   — inspect an existing file, then append-only
///                               sync a new spec into it.
///  UC5  Naming validation     — pre-flight check of a spec against APIM rules.
///  UC6  Product generation    — parameterless product block with tags.
///  UC7  Environment presets   — appsettings presets are served (proves config
///                               loads from the binary directory).
///  UC8  Error robustness      — a bad request returns a structured error and
///                               the server stays responsive for the next call.
/// </summary>
public class McpServerIntegrationTests : IClassFixture<McpServerFixture>
{
    private readonly McpServerFixture _server;

    public McpServerIntegrationTests(McpServerFixture server) => _server = server;

    private const string PetstoreSpec = """
        {
          "openapi": "3.0.1",
          "info": { "title": "Pet Store", "version": "1.0.0" },
          "paths": {
            "/pets": {
              "get": {
                "operationId": "listPets",
                "summary": "List pets",
                "responses": { "200": { "description": "OK" } }
              },
              "post": {
                "operationId": "createPet",
                "summary": "Create pet",
                "responses": { "201": { "description": "Created" } }
              }
            }
          }
        }
        """;

    // UC1 — Tool discovery
    [Fact]
    public void Uc1_ToolsList_ContainsAllElevenTools()
    {
        var tools = _server.ListToolNames();

        string[] expected =
        [
            "analyze_terraform_apim", "apply_template_profile", "convert_openapi_to_terraform",
            "fetch_openapi_operations", "generate_apim_product", "list_environment_presets",
            "parse_terraform_operations", "sync_openapi_with_terraform",
            "transform_environment", "update_terraform_from_openapi", "validate_openapi_for_apim"
        ];

        Assert.Equal(expected, tools);
    }

    // UC2 — Post-build conversion with zero settings (the CI automation case)
    [Fact]
    public void Uc2_ConvertWithoutSettings_ProducesTaggedTerraform()
    {
        var result = _server.CallTool("convert_openapi_to_terraform", new
        {
            openApiJson = PetstoreSpec
        });

        Assert.Contains("GENERATED WITH PLACEHOLDER TAGS", result);
        Assert.Contains("{api-group}", result);
        Assert.Contains("{stage-group-name}", result);
        Assert.Contains("api_operations = [", result);
        Assert.Contains("listpets", result.ToLowerInvariant());
    }

    // UC3 — Full conversion with complete settings
    [Fact]
    public void Uc3_ConvertWithFullSettings_NoPlaceholders()
    {
        var result = _server.CallTool("convert_openapi_to_terraform", new
        {
            openApiJson = PetstoreSpec,
            environment = "dev",
            apiGroupName = "pet-store-group",
            stageGroupName = "rg-apim-dev",
            apimName = "apim-company-dev",
            apiPathPrefix = "petstore",
            apiPathSuffix = "api",
            apiGatewayHost = "api.dev.company.com",
            backendServicePath = "pet-service"
        });

        Assert.Contains("pet-store-group = {", result);
        Assert.Contains("rg-apim-dev", result);
        Assert.DoesNotContain("GENERATED WITH PLACEHOLDER TAGS", result);
    }

    // UC4 — Analyze an existing file, then append-only sync a new spec into it
    [Fact]
    public void Uc4_AnalyzeThenSync_AppendsWithoutTouchingExisting()
    {
        const string existing = """
            pet-store-group = {
              api_operations = [
                {
                  operation_id             = "listpets-dev"
                  apim_resource_group_name = "rg-apim-dev"
                  apim_name                = "apim-company-dev"
                  api_name                 = "pet-store-dev"
                  display_name             = "List pets"
                  method                   = "GET"
                  url_template             = "/pets"
                  status_code              = "200"
                  description              = ""
                },
              ]
            }
            """;

        // Step 1: analyze
        var analysis = JsonDocument.Parse(_server.CallTool("analyze_terraform_apim", new
        {
            existingTerraform = existing
        }));
        Assert.True(analysis.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal(1, analysis.RootElement.GetProperty("totalOperations").GetInt32());

        // Step 2: sync — POST /pets is new, GET /pets matches
        var sync = JsonDocument.Parse(_server.CallTool("sync_openapi_with_terraform", new
        {
            existingTerraform = existing,
            openApiJson = PetstoreSpec,
            environment = "dev",
            apiGroupName = "pet-store-group",
            stageGroupName = "rg-apim-dev",
            apimName = "apim-company-dev",
            apiPathPrefix = "petstore",
            apiPathSuffix = "api",
            apiGatewayHost = "api.dev.company.com",
            backendServicePath = "pet-service",
            apiName = "pet-store-dev"
        }));

        Assert.True(sync.RootElement.GetProperty("success").GetBoolean());
        var report = sync.RootElement.GetProperty("report");
        Assert.Equal(1, report.GetProperty("operationsAdded").GetInt32());

        var hcl = sync.RootElement.GetProperty("terraformConfig").GetString()!;
        Assert.Contains("listpets-dev", hcl); // existing op untouched
        Assert.Contains("POST", hcl);          // new op appended
        Assert.NotNull(sync.RootElement.GetProperty("executionGraph"));
    }

    // UC5 — Pre-flight naming validation
    [Fact]
    public void Uc5_Validate_ReportsNamingResult()
    {
        var result = _server.CallTool("validate_openapi_for_apim", new
        {
            openApiJson = PetstoreSpec,
            environment = "dev"
        });

        Assert.Contains("API Title: Pet Store", result);
        Assert.Contains("Result: VALID", result);
    }

    // UC6 — Parameterless product generation
    [Fact]
    public void Uc6_GenerateProduct_NoParameters_TaggedBlock()
    {
        var result = JsonDocument.Parse(_server.CallTool("generate_apim_product"));

        Assert.True(result.RootElement.GetProperty("success").GetBoolean());
        var hcl = result.RootElement.GetProperty("terraformConfig").GetString()!;
        Assert.Contains("product = [", hcl);
        Assert.Contains("{product-id}", hcl);
        Assert.Contains("GENERATED WITH PLACEHOLDER TAGS", hcl);
    }

    // UC7 — Environment presets load from the server's binary directory
    [Fact]
    public void Uc7_EnvironmentPresets_ServedFromAppSettings()
    {
        var result = _server.CallTool("list_environment_presets");

        Assert.Contains("dev", result);
        Assert.Contains("staging", result);
        Assert.Contains("prod", result);
        Assert.Contains("rg-apim-dev", result);
    }

    // UC8 — Errors are structured and the server survives them
    [Fact]
    public void Uc8_InvalidInput_StructuredErrorAndServerStaysResponsive()
    {
        var bad = JsonDocument.Parse(_server.CallTool("sync_openapi_with_terraform", new
        {
            existingTerraform = "",
            openApiJson = "this is not json"
        }));
        Assert.False(bad.RootElement.GetProperty("success").GetBoolean());

        // The server must still answer the next request normally.
        var ok = _server.CallTool("validate_openapi_for_apim", new { openApiJson = PetstoreSpec });
        Assert.Contains("Result: VALID", ok);
    }
}
