using TerraformApi.Application.Services;
using TerraformApi.Domain.Validation;

namespace TerraformApi.Application.Tests.Services;

public class ApimNamingValidatorServiceTests
{
    private readonly ApimNamingValidatorService _validator = new();

    [Theory]
    [InlineData("my-api", true)]
    [InlineData("api1", true)]
    [InlineData("a", true)]
    [InlineData("MyApi-v2", true)]
    [InlineData("api-name-with-hyphens", true)]
    [InlineData("", false)]
    [InlineData("-starts-with-hyphen", false)]
    [InlineData("ends-with-hyphen-", false)]
    [InlineData("has spaces", false)]
    [InlineData("has_underscore", false)]
    [InlineData("has.dot", false)]
    public void ValidateApiName_ReturnsExpectedResult(string name, bool expectedValid)
    {
        var result = _validator.ValidateApiName(name);
        Assert.Equal(expectedValid, result.IsValid);
    }

    [Fact]
    public void ValidateApiName_TooLong_ReturnsInvalid()
    {
        var longName = "a" + new string('b', 255); // 256 chars, valid pattern
        var result = _validator.ValidateApiName(longName);
        Assert.True(result.IsValid);

        var tooLong = "a" + new string('b', 256); // 257 chars
        result = _validator.ValidateApiName(tooLong);
        Assert.False(result.IsValid);
    }

    [Theory]
    [InlineData("get-users-dev", true)]
    [InlineData("post_create_user", true)]
    [InlineData("op1", true)]
    [InlineData("a", true)]
    [InlineData("operation-with-hyphens_and_underscores", true)]
    [InlineData("", false)]
    [InlineData("-starts-bad", false)]
    [InlineData("has spaces", false)]
    [InlineData("has.dots", false)]
    public void ValidateOperationId_ReturnsExpectedResult(string operationId, bool expectedValid)
    {
        var result = _validator.ValidateOperationId(operationId);
        Assert.Equal(expectedValid, result.IsValid);
    }

    [Fact]
    public void ValidateOperationId_TooLong_ReturnsInvalid()
    {
        var tooLong = "a" + new string('b', 80); // 81 chars
        var result = _validator.ValidateOperationId(tooLong);
        Assert.False(result.IsValid);
    }

    [Theory]
    [InlineData("My API Display Name", true)]
    [InlineData("API v2.0 (Production)", true)]
    [InlineData("Simple", true)]
    [InlineData("", false)]
    [InlineData("Has * asterisk", false)]
    [InlineData("Has # hash", false)]
    [InlineData("Has < angle >", false)]
    public void ValidateDisplayName_ReturnsExpectedResult(string displayName, bool expectedValid)
    {
        var result = _validator.ValidateDisplayName(displayName);
        Assert.Equal(expectedValid, result.IsValid);
    }

    [Theory]
    [InlineData("myapp.dev/v1/api", true)]
    [InlineData("simple-path", true)]
    [InlineData("v1/users", true)]
    [InlineData("", true)]
    [InlineData("path with spaces", false)]
    public void ValidateApiPath_ReturnsExpectedResult(string path, bool expectedValid)
    {
        var result = _validator.ValidateApiPath(path);
        Assert.Equal(expectedValid, result.IsValid);
    }

    [Theory]
    [InlineData("rg-apim-dev", true)]
    [InlineData("my.resource-group_1", true)]
    [InlineData("rg(prod)", true)]
    [InlineData("", false)]
    [InlineData("has spaces", false)]
    public void ValidateResourceGroupName_ReturnsExpectedResult(string name, bool expectedValid)
    {
        var result = _validator.ValidateResourceGroupName(name);
        Assert.Equal(expectedValid, result.IsValid);
    }

    [Theory]
    [InlineData("My API Service", "my-api-service")]
    [InlineData("api/v2", "api-v2")]
    [InlineData("  spaces  ", "spaces")]
    [InlineData("UPPERCASE", "uppercase")]
    [InlineData("special!@#chars", "special-chars")]
    [InlineData("multiple---hyphens", "multiple-hyphens")]
    [InlineData("", "api")]
    public void SanitizeApiName_ReturnsValidName(string input, string expected)
    {
        var result = _validator.SanitizeApiName(input);
        Assert.Equal(expected, result);
        Assert.True(_validator.ValidateApiName(result).IsValid);
    }

    [Theory]
    [InlineData("GET /users/{id}", "get-users-id")]
    [InlineData("post.create.user", "post-create-user")]
    [InlineData("", "operation")]
    public void SanitizeOperationId_ReturnsValidId(string input, string expected)
    {
        var result = _validator.SanitizeOperationId(input);
        Assert.Equal(expected, result);
        Assert.True(_validator.ValidateOperationId(result).IsValid);
    }

    [Theory]
    [InlineData("/v1/users", "v1/users")]
    [InlineData("already/clean", "already/clean")]
    [InlineData("UPPER/Case", "upper/case")]
    [InlineData("", "")]
    public void SanitizeApiPath_ReturnsValidPath(string input, string expected)
    {
        var result = _validator.SanitizeApiPath(input);
        Assert.Equal(expected, result);
    }
}
