using TerraformMerge.Engine;

namespace TerraformMerge.Tests;

public class MergeFieldCatalogTests
{
    private static OperationNode Node(
        string method, string url, string apiName = "", string rg = "", string apim = "") => new()
    {
        Source = OperationSource.OriginalTerraform,
        Method = method,
        UrlTemplate = url,
        ApiName = apiName,
        ApimResourceGroupName = rg,
        ApimName = apim
    };

    [Fact]
    public void Build_UnionsDistinctValuesFromBothSides()
    {
        var left = new[] { Node("GET", "orders", rg: "rg-apim-dev") };
        var right = new[] { Node("GET", "orders", rg: "rg-apim-staging") };

        var catalog = MergeFieldCatalog.Build(left, right);

        Assert.Equal(["rg-apim-dev", "rg-apim-staging"],
            catalog.Values(OperationField.ApimResourceGroupName));
    }

    [Fact]
    public void Build_DedupesAndSortsOrdinal()
    {
        var left = new[] { Node("POST", "orders"), Node("GET", "orders") };
        var right = new[] { Node("GET", "orders"), Node("DELETE", "orders") };

        var catalog = MergeFieldCatalog.Build(left, right);

        Assert.Equal(["DELETE", "GET", "POST"], catalog.Values(OperationField.Method));
    }

    [Fact]
    public void Build_ExcludesEmptyValues()
    {
        var left = new[] { Node("GET", "orders", apiName: "orders-api-dev") };
        var right = new[] { Node("GET", "orders") }; // no api name

        var catalog = MergeFieldCatalog.Build(left, right);

        Assert.Equal(["orders-api-dev"], catalog.Values(OperationField.ApiName));
    }

    [Fact]
    public void Values_UnknownlyEmptyField_ReturnsEmpty()
    {
        var catalog = MergeFieldCatalog.Build([], []);
        Assert.Empty(catalog.Values(OperationField.Description));
    }
}
