using TerraformMerge.Engine;

namespace TerraformMerge.Tests;

/// <summary>
/// The segment rule: an environment name counts only as a whole segment of a
/// value, so identifiers that merely contain the letters are left alone. Every
/// case here is one the retarget would otherwise corrupt.
/// </summary>
public class EnvironmentTokenTests
{
    [Theory]
    [InlineData("rg-apim-dev", "dev")]
    [InlineData("apim-company-qa", "qa")]
    [InlineData("orders.staging/v1/api", "staging")]
    [InlineData("Orders API - dev", "dev")]
    [InlineData("dev-list-orders", "dev")]
    [InlineData("orders_uat_api", "uat")]
    public void Find_ReadsTheEnvironmentSegment(string value, string expected) =>
        Assert.Equal(expected, EnvironmentToken.Find(value));

    [Theory]
    [InlineData("api-devices")]          // "dev" is a prefix of a longer segment
    [InlineData("latest-orders")]        // "test" sits inside "latest"
    [InlineData("orders-api")]
    [InlineData("")]
    [InlineData(null)]
    public void Find_IgnoresLettersInsideAnotherWord(string? value) =>
        Assert.Null(EnvironmentToken.Find(value));

    [Fact]
    public void Find_PrefersTheLongerEnvironmentName()
    {
        Assert.Equal("development", EnvironmentToken.Find("rg-apim-development"));
        Assert.Equal("pre-prod", EnvironmentToken.Find("rg-apim-pre-prod"));
    }

    [Theory]
    [InlineData("rg-apim-dev", "rg-apim-qa")]
    [InlineData("list-orders-dev", "list-orders-qa")]
    [InlineData("orders.dev/v1/api", "orders.qa/v1/api")]
    [InlineData("dev-orders-dev", "qa-orders-qa")]   // every segment, not just the first
    [InlineData("api-devices", "api-devices")]       // untouched
    public void Replace_RewritesOnlyWholeSegments(string value, string expected) =>
        Assert.Equal(expected, EnvironmentToken.Replace(value, "dev", "qa"));

    [Theory]
    [InlineData("rg-apim-DEV", "rg-apim-QA")]
    [InlineData("Orders API - Dev", "Orders API - Qa")]
    public void Replace_KeepsTheCasingTheValueUsed(string value, string expected) =>
        Assert.Equal(expected, EnvironmentToken.Replace(value, "dev", "qa"));

    [Fact]
    public void Contains_AsksTheSameSegmentQuestion()
    {
        Assert.True(EnvironmentToken.Contains("rg-apim-dev", "dev"));
        Assert.False(EnvironmentToken.Contains("api-devices", "dev"));
    }
}
