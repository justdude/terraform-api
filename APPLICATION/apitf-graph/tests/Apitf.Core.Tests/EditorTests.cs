using Apitf.Core;
using Xunit;

namespace Apitf.Core.Tests;

public class EditorTests
{
    [Fact]
    public void GenerateFromScratch_IsValidShape()
    {
        var s = SpecEditor.NewSpec("Orders API");
        SpecEditor.AddOperation(s, "GET", "/orders", "List orders");
        SpecEditor.AddOperation(s, "POST", "/orders", "Create order");
        SpecEditor.AddOperation(s, "GET", "/orders/{id}", "Get order");

        Assert.Equal(3, s.Operations().Count());
        Assert.Equal("Orders API", s.Title);
    }

    [Fact]
    public void PathParameters_DerivedFromTemplate()
    {
        var s = SpecEditor.NewSpec("T");
        SpecEditor.AddOperation(s, "GET", "/orders/{id}", "Get");
        var op = s.OperationMap()[new OperationKey("/orders/{id}", "get")];
        Assert.Contains("\"name\":\"id\"", op.Canonical);
        Assert.Contains("\"in\":\"path\"", op.Canonical);
    }

    [Fact]
    public void Add_IsIdempotent()
    {
        var s = SpecEditor.NewSpec("T");
        Assert.True(SpecEditor.AddOperation(s, "GET", "/orders", "x"));
        Assert.False(SpecEditor.AddOperation(s, "GET", "/orders", "x")); // no change
        Assert.Single(s.Operations());
    }

    [Fact]
    public void AddThenRemove_RoundTripsToOriginal()
    {
        var s = SpecEditor.NewSpec("T");
        SpecEditor.AddOperation(s, "GET", "/orders", "List");
        var before = s.ToJson();

        SpecEditor.AddOperation(s, "DELETE", "/orders/{id}", "Del");
        SpecEditor.RemoveOperation(s, "DELETE", "/orders/{id}");
        var after = s.ToJson();

        Assert.Equal(before, after);
    }

    [Fact]
    public void Canonicalize_IsDeterministic()
    {
        var a = SpecEditor.NewSpec("T");
        SpecEditor.AddOperation(a, "POST", "/orders", "Create");
        SpecEditor.AddOperation(a, "GET", "/orders", "List");

        var b = SpecEditor.NewSpec("T");
        SpecEditor.AddOperation(b, "GET", "/orders", "List");
        SpecEditor.AddOperation(b, "POST", "/orders", "Create");

        // Different insertion order -> identical canonical output.
        Assert.Equal(a.ToJson(), b.ToJson());
    }

    [Fact]
    public void Terraform_Generates_BulkImport_WellFormedJson()
    {
        var s = SpecEditor.NewSpec("Orders API");
        SpecEditor.AddOperation(s, "GET", "/orders", "List");

        var tf = TerraformGenerator.GenerateTfJson(s, "orders", "specs/orders.json");

        Assert.Contains("azurerm_api_management_api", tf);
        Assert.Contains("openapi+json", tf);
        // must parse as JSON (no HCL escaping issues)
        using var doc = System.Text.Json.JsonDocument.Parse(tf);
        Assert.True(doc.RootElement.TryGetProperty("resource", out _));
    }

    [Fact]
    public void Differ_ClassifiesChanges()
    {
        var left = SpecEditor.NewSpec("T");
        SpecEditor.AddOperation(left, "GET", "/orders", "List");
        SpecEditor.AddOperation(left, "POST", "/orders", "Create");

        var right = SpecEditor.NewSpec("T");
        SpecEditor.AddOperation(right, "GET", "/orders", "List CHANGED");      // modified
        SpecEditor.AddOperation(right, "DELETE", "/orders/{id}", "Del");        // added
        // POST removed

        var diff = Differ.Diff(left, right);
        Assert.Equal(ChangeType.Modified, diff.First(c => c.Key == new OperationKey("/orders", "get")).Type);
        Assert.Equal(ChangeType.Removed, diff.First(c => c.Key == new OperationKey("/orders", "post")).Type);
        Assert.Equal(ChangeType.Added, diff.First(c => c.Key == new OperationKey("/orders/{id}", "delete")).Type);
    }

    [Fact]
    public void YamlRoundTrip_PreservesOperations()
    {
        var s = SpecEditor.NewSpec("T");
        SpecEditor.AddOperation(s, "GET", "/orders", "List");
        SpecEditor.AddOperation(s, "GET", "/orders/{id}", "Get");

        var yaml = s.ToYaml();
        var reparsed = SpecModel.FromYaml(yaml);
        Assert.Equal(2, reparsed.Operations().Count());
    }
}
