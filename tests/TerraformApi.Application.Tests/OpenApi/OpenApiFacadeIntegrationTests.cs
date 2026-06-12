using Microsoft.Extensions.DependencyInjection;
using TerraformApi.Application;
using TerraformApi.Application.Services;
using TerraformApi.Application.Services.OpenApi;
using TerraformApi.Domain.Interfaces;
using TerraformApi.Domain.Models;
using TerraformApi.Domain.Models.Sync;

namespace TerraformApi.Application.Tests.OpenApi;

/// <summary>
/// Integration tests for the OpenAPI facade rewrite, driven by the
/// example-openapi.json fixture (Order Management API with complex types:
/// $ref'd nested objects, arrays of objects, enums, additionalProperties
/// dictionaries, multiple request content types and a bearer security scheme).
///
/// Covers the acceptance criteria:
///  ACC1 — all reading goes through OpenApiDocumentReader (Microsoft.OpenApi.Readers);
///  ACC2 — one facade instance serves IOpenApiParser and IOpenApiOperationsFetcher;
///  ACC3 — end-to-end conversion OpenAPI → ApimConfiguration → Terraform with
///         complex POST request bodies;
///  ACC4 — the TerraformApiFacade package entry point covers the functionality.
/// </summary>
public class OpenApiFacadeIntegrationTests
{
    private static string LoadFixture() =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "example-openapi.json"));

    private static ConversionSettings FullSettings() => new()
    {
        Environment = "dev",
        ApiGroupName = "order-api-group",
        StageGroupName = "rg-apim-dev",
        ApimName = "apim-company-dev",
        ApiPathPrefix = "orders",
        ApiPathSuffix = "api",
        ApiGatewayHost = "api.dev.company.com",
        BackendServicePath = "order-service"
    };

    // -----------------------------------------------------------------
    // ACC2 — facade registration: one instance behind both interfaces
    // -----------------------------------------------------------------

    [Fact]
    public void Acc2_DiResolvesBothInterfacesToTheSameFacadeInstance()
    {
        var services = new ServiceCollection();
        services.AddApplicationServices();
        using var provider = services.BuildServiceProvider();

        var parser = provider.GetRequiredService<IOpenApiParser>();
        var fetcher = provider.GetRequiredService<IOpenApiOperationsFetcher>();
        var facade = provider.GetRequiredService<OpenApiFacadeService>();

        Assert.Same(facade, parser);
        Assert.Same(facade, fetcher);
    }

    [Fact]
    public void Acc2_FacadeImplementsBothContracts()
    {
        var facade = new OpenApiFacadeService(new ApimNamingValidatorService());

        Assert.IsAssignableFrom<IOpenApiParser>(facade);
        Assert.IsAssignableFrom<IOpenApiOperationsFetcher>(facade);
    }

    // -----------------------------------------------------------------
    // ACC1 — centralized reader behavior
    // -----------------------------------------------------------------

    [Fact]
    public void Acc1_DocumentReader_ReadsFixtureCleanly()
    {
        var result = OpenApiDocumentReader.Read(LoadFixture());

        Assert.True(result.IsClean, string.Join("; ", result.Errors));
        Assert.Equal("Order Management API", result.Document!.Info.Title);
        Assert.Equal(2, result.Document.Paths.Count);
    }

    [Fact]
    public void Acc1_DocumentReader_NeverThrows_CollectsErrors()
    {
        var garbage = OpenApiDocumentReader.Read("{ not openapi at all !!");
        Assert.False(garbage.IsClean);
        Assert.NotEmpty(garbage.Errors);

        var empty = OpenApiDocumentReader.Read("");
        Assert.False(empty.IsClean);
        Assert.Contains("empty", empty.Errors[0], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Acc1_Parser_ThrowsContractExceptionOnInvalidDocument()
    {
        var facade = new OpenApiFacadeService(new ApimNamingValidatorService());

        var ex = Assert.Throws<InvalidOperationException>(
            () => facade.Parse("{ broken", FullSettings()));
        Assert.StartsWith("Failed to parse OpenAPI document:", ex.Message);
    }

    // -----------------------------------------------------------------
    // ACC3 — complex-type conversion: OpenAPI → ApimConfiguration
    // -----------------------------------------------------------------

    [Fact]
    public void Acc3_Parse_ComplexPostBody_MapsRepresentationsWithSchemaIds()
    {
        var facade = new OpenApiFacadeService(new ApimNamingValidatorService());
        var config = facade.Parse(LoadFixture(), FullSettings());

        Assert.Equal(4, config.ApiOperations.Count);

        var createOrder = config.ApiOperations.Single(o => o.OperationId.Contains("createorder"));
        Assert.Equal("POST", createOrder.Method);
        Assert.Equal("orders", createOrder.UrlTemplate);

        var request = Assert.Single(createOrder.Requests);

        // The complex $ref'd Order schema flows into the representations.
        Assert.Equal(2, request.Representations.Count);
        var json = request.Representations.Single(r => r.ContentType == "application/json");
        Assert.Equal("Order", json.SchemaId);
        Assert.Equal("Order", json.TypeName);
        Assert.Contains(request.Representations, r => r.ContentType == "application/xml");

        // The bearer security requirement injects an Authorization header.
        Assert.Contains(request.Headers, h => h.Name == "Authorization" && h.Required);

        // Responses: 201 with the Order representation + 400 ValidationProblem.
        Assert.Equal(201, createOrder.StatusCode);
        var created = createOrder.Responses.Single(r => r.StatusCode == 201);
        Assert.Contains(created.Representations, r => r.SchemaId == "Order");
        var badRequest = createOrder.Responses.Single(r => r.StatusCode == 400);
        Assert.Contains(badRequest.Representations, r => r.SchemaId == "ValidationProblem");
    }

    [Fact]
    public void Acc3_Parse_InlineComplexPutBody_AndPathLevelParameters()
    {
        var facade = new OpenApiFacadeService(new ApimNamingValidatorService());
        var config = facade.Parse(LoadFixture(), FullSettings());

        // PUT has an inline (non-$ref) object schema — representation without schema id.
        var replaceOrder = config.ApiOperations.Single(o => o.OperationId.Contains("replaceorder"));
        var putRequest = Assert.Single(replaceOrder.Requests);
        var putJson = putRequest.Representations.Single(r => r.ContentType == "application/json");
        Assert.Null(putJson.SchemaId);

        // GET inherits the path-level orderId parameter; query/header params map types.
        var listOrders = config.ApiOperations.Single(o => o.OperationId.Contains("listorders"));
        var listRequest = Assert.Single(listOrders.Requests);
        Assert.Contains(listRequest.QueryParameters, p => p.Name == "status" && p.Type == "string");
        Assert.Contains(listRequest.QueryParameters, p => p.Name == "pageSize" && p.Type == "integer");
        Assert.Contains(listRequest.Headers, p => p.Name == "X-Correlation-Id");
    }

    // -----------------------------------------------------------------
    // ACC3 — end-to-end: OpenAPI → Terraform HCL
    // -----------------------------------------------------------------

    [Fact]
    public void Acc3_EndToEnd_ConversionProducesTerraformWithComplexBody()
    {
        var validator = new ApimNamingValidatorService();
        var facade = new OpenApiFacadeService(validator);
        var generator = new TerraformGeneratorService();
        var orchestrator = new ConversionOrchestratorService(
            facade, generator, new TerraformMergerService(generator), validator);

        var result = orchestrator.Convert(LoadFixture(), FullSettings());

        Assert.True(result.Success, string.Join("; ", result.Errors));
        Assert.Contains("order-api-group = {", result.TerraformConfig);
        Assert.Contains("api_operations = [", result.TerraformConfig);

        // The complex POST body surfaces in the generated HCL.
        Assert.Contains("application/json", result.TerraformConfig);
        Assert.Contains("Order", result.TerraformConfig);
        Assert.Contains("Authorization", result.TerraformConfig);
        Assert.Contains("createorder", result.TerraformConfig.ToLowerInvariant());
    }

    // -----------------------------------------------------------------
    // ACC3 — unified operations list from the same facade instance
    // -----------------------------------------------------------------

    [Fact]
    public void Acc3_ParseOperations_ComplexFixture_UnifiedListComplete()
    {
        var facade = new OpenApiFacadeService(new ApimNamingValidatorService());
        var result = facade.ParseOperations(LoadFixture(), "https://example.com/openapi.json");

        Assert.True(result.Success);
        Assert.Equal(4, result.TotalOperations);
        Assert.Equal("Order Management API", result.Api!.Title);

        var post = result.Operations.Single(o => o.Method == "POST");
        Assert.Equal(["application/json", "application/xml"], post.RequestBodyContentTypes);
        Assert.Equal([201, 400], post.ResponseCodes);
        Assert.Contains("orders", post.Tags!);

        // Path-level uuid parameter on the GET-by-id operation.
        var getById = result.Operations.Single(o => o.OperationId == "getOrderById");
        Assert.Contains(getById.Parameters!, p => p.Name == "orderId" && p.In == "path" && p.Required);
    }

    // -----------------------------------------------------------------
    // ACC4 — the package facade covers the functionality end to end
    // -----------------------------------------------------------------

    [Fact]
    public void Acc4_TerraformApiFacade_Create_ConvertsWithoutAnyHost()
    {
        var facade = TerraformApiFacade.Create();

        var result = facade.ConvertOpenApiToTerraform(LoadFixture(), FullSettings());

        Assert.True(result.Success, string.Join("; ", result.Errors));
        Assert.Contains("createorder", result.TerraformConfig.ToLowerInvariant());
    }

    [Fact]
    public void Acc4_TerraformApiFacade_CoversSyncAnalyzeProductAndListings()
    {
        var facade = TerraformApiFacade.Create();

        // Sync from scratch (no settings → placeholder tags).
        var sync = facade.Sync(new SyncRequest
        {
            OpenApiJson = LoadFixture(),
            ExistingTerraform = null,
            Settings = new ConversionSettings()
        });
        Assert.True(sync.Success, string.Join("; ", sync.Errors));
        Assert.Equal(4, sync.Report.OperationsAdded);
        Assert.NotNull(sync.ExecutionGraph);

        // Analyze the produced Terraform.
        var analysis = facade.AnalyzeTerraform(sync.TerraformConfig);
        Assert.True(analysis.Success, string.Join("; ", analysis.Errors));
        Assert.Equal(4, analysis.TotalOperations);

        // Operations listings from both sides.
        Assert.Equal(4, facade.ListOpenApiOperations(LoadFixture()).TotalOperations);
        Assert.Equal(4, facade.ParseTerraformOperations(sync.TerraformConfig).TotalOperations);

        // Product generation.
        var product = facade.GenerateProduct(new ApimProductRequest { ProductId = "orders", DisplayName = "Orders" });
        Assert.True(product.Success);
        Assert.Contains("product = [", product.TerraformConfig);
    }

    [Fact]
    public void Acc4_DiHostsCanInjectThePackageFacade()
    {
        var services = new ServiceCollection();
        services.AddApplicationServices();
        using var provider = services.BuildServiceProvider();

        var facade = provider.GetRequiredService<TerraformApiFacade>();
        var analysis = facade.GenerateProduct();
        Assert.True(analysis.Success);
    }
}
