using TerraformApi.Application.Services.Apim;
using TerraformApi.Application.Services.Hcl;
using TerraformApi.Application.Services.Sync;
using TerraformApi.Domain.Models.Apim;
using TerraformApi.Domain.Models.Hcl;
using TerraformApi.Domain.Models.Sync;

namespace TerraformApi.Application.Tests.Sync;

/// <summary>Detector tests DT1–DT5 (§REV-1.7 Phase 4a).</summary>
public class ApimTemplateProfileDetectorServiceTests
{
    private readonly ApimTerraformReaderService _reader = new(new HclParserService());
    private readonly ApimTemplateProfileDetectorService _detector = new();

    private static string LoadFixture() =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "example-existing.tf"));

    private const string LiteralTerraform = """
        my-api = {
          api = [
            {
              apim_resource_group_name = "rg-apim-dev"
              apim_name                = "apim-company-dev"
              name                     = "my-api-dev"
              display_name             = "My API - dev"
              path                     = "myapp.dev/v1/api"
              service_url              = "https://api-dev.company.com/v1/svc/"
            },
          ]
          api_operations = [
            {
              operation_id             = "get-users-dev"
              apim_resource_group_name = "rg-apim-dev"
              apim_name                = "apim-company-dev"
              api_name                 = "my-api-dev"
              display_name             = "Get users"
              method                   = "GET"
              url_template             = "users"
              status_code              = "200"
              description              = ""
            },
          ]
        }
        """;

    // DT1
    [Fact]
    public void Detect_EmptyDocument_ConfidenceEmpty()
    {
        var parsed = _reader.Read("");
        var detected = _detector.Detect(parsed);

        Assert.Equal(StylingConfidence.Empty, detected.Confidence);
    }

    // DT2
    [Fact]
    public void Detect_UserExample_HighlyTemplatedWithCorrectPlaceholders()
    {
        var parsed = _reader.Read(LoadFixture());
        var detected = _detector.Detect(parsed);

        Assert.Equal(StylingConfidence.HighlyTemplated, detected.Confidence);
        Assert.Equal("${stage_group_name}", detected.InferredProfile.ApiFieldTemplates["apim_resource_group_name"]);
        Assert.Equal("${apim_name}", detected.InferredProfile.ApiFieldTemplates["apim_name"]);
        Assert.Equal("${api_name}-${env}", detected.InferredProfile.ApiFieldTemplates["name"]);
        Assert.Equal("${operation_prefix}-${env}", detected.InferredProfile.OperationFieldTemplates["operation_id"]);
        Assert.Equal("UserExampleProfile", detected.ClosestKnownProfileName);
    }

    // DT3
    [Fact]
    public void Detect_LiteralFile_MostlyLiteralEmptyTemplates()
    {
        var parsed = _reader.Read(LiteralTerraform);
        var detected = _detector.Detect(parsed);

        Assert.Equal(StylingConfidence.MostlyLiteral, detected.Confidence);
        Assert.Empty(detected.InferredProfile.ApiFieldTemplates);
        Assert.Empty(detected.InferredProfile.OperationFieldTemplates);
        Assert.Equal("LiteralProfile", detected.ClosestKnownProfileName);
    }

    // DT4
    [Fact]
    public void Detect_MixedFile_OnlyDominantTemplatedFieldsInferred()
    {
        // operation_id templated in 2 of 2 ops; everything else literal.
        var mixed = """
            my-api = {
              api_operations = [
                {
                  operation_id             = "${operation_prefix}-${env}"
                  apim_resource_group_name = "rg-apim-dev"
                  apim_name                = "apim-company-dev"
                  api_name                 = "my-api-dev"
                  display_name             = "One"
                  method                   = "GET"
                  url_template             = "one"
                  status_code              = "200"
                  description              = ""
                },
                {
                  operation_id             = "${operation_prefix}-${env}"
                  apim_resource_group_name = "rg-apim-dev"
                  apim_name                = "apim-company-dev"
                  api_name                 = "my-api-dev"
                  display_name             = "Two"
                  method                   = "POST"
                  url_template             = "two"
                  status_code              = "200"
                  description              = ""
                },
              ]
            }
            """;
        var detected = _detector.Detect(_reader.Read(mixed));

        Assert.Equal(StylingConfidence.MostlyLiteral, detected.Confidence);
        Assert.Equal("${operation_prefix}-${env}", detected.InferredProfile.OperationFieldTemplates["operation_id"]);
        Assert.False(detected.InferredProfile.OperationFieldTemplates.ContainsKey("apim_name"));
    }

    // DT5
    [Fact]
    public void Detect_AllReferencedVariables_UniqueSetFromWholeFile()
    {
        var detected = _detector.Detect(_reader.Read(LoadFixture()));

        // From the fixture: api fields + operation fields + CORS policy heredoc.
        string[] expected =
        [
            "stage_group_name", "apim_name", "api_name", "env", "operation_prefix",
            "operation_path", "frontend_host", "company_domain", "local_dev_host",
            "local_dev_port", "api_path_prefix", "api_path_suffix", "api_gateway_host",
            "api_version", "backend_service_path", "api_revision", "product_id",
            "api_display_name", "operation_display_name"
        ];

        foreach (var name in expected)
            Assert.Contains(name, detected.AllReferencedVariables);
    }

    [Fact]
    public void Detect_LiteralValues_CollectedByField()
    {
        var detected = _detector.Detect(_reader.Read(LiteralTerraform));

        Assert.Contains("apim-company-dev", detected.LiteralValuesByField["api.apim_name"]);
        Assert.Contains("get-users-dev", detected.LiteralValuesByField["api_operation.operation_id"]);
    }
}
