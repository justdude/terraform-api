using TerraformMerge.Engine;

namespace TerraformMerge.Tests;

public class UrlNormalizerTests
{
    [Theory]
    [InlineData("/users/", "users")]
    [InlineData("users//{id}", "users/{id}")]
    [InlineData("users/:id", "users/{id}")]
    [InlineData("  /a/b/  ", "a/b")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void Normalize_CleansUpSyntax(string? input, string expected)
    {
        Assert.Equal(expected, UrlNormalizer.Normalize(input));
    }

    [Theory]
    [InlineData("users/{id}", "users/{}")]
    [InlineData("users/:userId/orders/{orderId}", "users/{}/orders/{}")]
    [InlineData("orders", "orders")]
    public void Canonical_CollapsesParameters(string input, string expected)
    {
        Assert.Equal(expected, UrlNormalizer.Canonical(input));
    }

    [Fact]
    public void Segments_SplitsOnSlash()
    {
        Assert.Equal(["users", "{id}", "orders"], UrlNormalizer.Segments("/users/{id}/orders/"));
    }
}
