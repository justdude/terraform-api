using System.Text.Json;
using Apitf.Core;
using Xunit;

namespace Apitf.Core.Tests;

public class MultiEnvTests
{
    private static SpecModel Sample()
    {
        var s = SpecEditor.NewSpec("Orders API");
        SpecEditor.AddOperation(s, "GET", "/orders", "List");
        return s;
    }

    [Fact]
    public void MultiEnv_WellFormed_PerEnvProviderAndResource()
    {
        var tf = TerraformGenerator.GenerateMultiEnvTfJson(Sample(), "orders", "specs/orders.json",
            new[] { "dev", "prod" });
        using var doc = JsonDocument.Parse(tf);
        var root = doc.RootElement;

        Assert.True(root.GetProperty("variable").TryGetProperty("environments", out _));
        Assert.Equal(2, root.GetProperty("provider").GetProperty("azurerm").GetArrayLength());

        var apis = root.GetProperty("resource").GetProperty("azurerm_api_management_api");
        Assert.True(apis.TryGetProperty("orders_dev", out var dev));
        Assert.True(apis.TryGetProperty("orders_prod", out _));
        Assert.Equal("azurerm.dev", dev.GetProperty("provider").GetString());
    }

    [Fact]
    public void MultiEnv_AllEnvsShareOneSpecFile()
    {
        var envs = new[] { "dev", "stage", "prod" };
        var tf = TerraformGenerator.GenerateMultiEnvTfJson(Sample(), "orders", "specs/orders.json", envs);
        using var doc = JsonDocument.Parse(tf);
        var apis = doc.RootElement.GetProperty("resource").GetProperty("azurerm_api_management_api");
        foreach (var env in envs)
        {
            var cv = apis.GetProperty($"orders_{env}").GetProperty("import")
                         .GetProperty("content_value").GetString();
            Assert.Contains("specs/orders.json", cv);
        }
    }

    [Fact]
    public void MultiEnv_ReferencesEnvScopedValues()
    {
        var tf = TerraformGenerator.GenerateMultiEnvTfJson(Sample(), "orders", "specs/orders.json",
            new[] { "dev" });
        using var doc = JsonDocument.Parse(tf);
        var dev = doc.RootElement.GetProperty("resource")
                     .GetProperty("azurerm_api_management_api").GetProperty("orders_dev");
        Assert.Contains("var.environments[\"dev\"].apim_name", dev.GetProperty("api_management_name").GetString());
        Assert.Contains("var.environments[\"dev\"].revision", dev.GetProperty("revision").GetString());
        Assert.Contains("var.environments[\"dev\"].api_name", dev.GetProperty("name").GetString());
    }

    [Fact]
    public void MultiEnv_EmptyEnvs_FallsBackToSingle()
    {
        var tf = TerraformGenerator.GenerateMultiEnvTfJson(Sample(), "orders", "specs/orders.json",
            System.Array.Empty<string>());
        Assert.DoesNotContain("\"variable\"", tf);
        Assert.Contains("azurerm_api_management_api", tf);
    }

    [Fact]
    public void MultiEnv_SanitizesResourceKey()
    {
        var tf = TerraformGenerator.GenerateMultiEnvTfJson(Sample(), "my-api.v2", "specs/x.json",
            new[] { "dev" });
        using var doc = JsonDocument.Parse(tf);
        var apis = doc.RootElement.GetProperty("resource").GetProperty("azurerm_api_management_api");
        Assert.True(apis.TryGetProperty("my_api_v2_dev", out _));
    }
}
