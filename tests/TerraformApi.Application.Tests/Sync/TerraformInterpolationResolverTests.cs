using TerraformApi.Application.Services.Sync;

namespace TerraformApi.Application.Tests.Sync;

/// <summary>Resolver tests from plan §6 Phase 4.</summary>
public class TerraformInterpolationResolverTests
{
    private readonly TerraformInterpolationResolver _resolver = new();

    [Fact]
    public void Resolve_SimpleVariable_ReturnsValue()
    {
        var result = _resolver.Resolve("${env}", new Dictionary<string, string> { ["env"] = "dev" });
        Assert.Equal("dev", result);
    }

    [Fact]
    public void Resolve_MultipleVariables_AllSubstituted()
    {
        var result = _resolver.Resolve("${a}-${b}", new Dictionary<string, string> { ["a"] = "1", ["b"] = "2" });
        Assert.Equal("1-2", result);
    }

    [Fact]
    public void Resolve_MissingVariable_LeftAsIs()
    {
        var result = _resolver.ResolveWithReport("${unknown}", new Dictionary<string, string>());
        Assert.Equal("${unknown}", result.Value);
        Assert.Equal(["unknown"], result.UnresolvedExpressions);
        Assert.True(result.HasUnresolvedExpressions);
    }

    [Fact]
    public void Resolve_VarDotPath_Supported()
    {
        var result = _resolver.Resolve("${var.foo}", new Dictionary<string, string> { ["var.foo"] = "bar" });
        Assert.Equal("bar", result);
    }

    [Fact]
    public void Resolve_NoInterpolation_PassThrough()
    {
        var result = _resolver.ResolveWithReport("plain", new Dictionary<string, string> { ["plain"] = "x" });
        Assert.Equal("plain", result.Value);
        Assert.False(result.HasUnresolvedExpressions);
    }

    [Fact]
    public void Resolve_ComplexExpression_LeftAsIsAndReported()
    {
        var result = _resolver.ResolveWithReport(
            "${var.x ? \"a\" : \"b\"}",
            new Dictionary<string, string> { ["var.x"] = "true" });

        Assert.Equal("${var.x ? \"a\" : \"b\"}", result.Value);
        Assert.Single(result.UnresolvedExpressions);
    }

    [Fact]
    public void Resolve_MixedResolvedAndUnresolved()
    {
        var result = _resolver.ResolveWithReport(
            "${api_name}-${env}-${unknown}",
            new Dictionary<string, string> { ["api_name"] = "bpc", ["env"] = "dev" });

        Assert.Equal("bpc-dev-${unknown}", result.Value);
        Assert.Equal(["unknown"], result.UnresolvedExpressions);
    }

    [Fact]
    public void Resolve_PrefixAndSuffixPreserved()
    {
        var result = _resolver.Resolve(
            "https://${host}/v1/", new Dictionary<string, string> { ["host"] = "api.example.com" });
        Assert.Equal("https://api.example.com/v1/", result);
    }
}
