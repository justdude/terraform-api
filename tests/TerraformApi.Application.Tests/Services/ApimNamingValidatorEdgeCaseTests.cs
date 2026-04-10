using TerraformApi.Application.Services;
using TerraformApi.Domain.Validation;

namespace TerraformApi.Application.Tests.Services;

/// <summary>
/// Edge-case and boundary tests for the APIM naming validator,
/// covering Microsoft's documented naming constraints.
/// </summary>
public class ApimNamingValidatorEdgeCaseTests
{
    private readonly ApimNamingValidatorService _validator = new();

    // ---- API name boundary lengths ----

    [Fact]
    public void ValidateApiName_ExactlyMinLength_IsValid()
    {
        var result = _validator.ValidateApiName("a");
        Assert.True(result.IsValid);
    }

    [Fact]
    public void ValidateApiName_ExactlyMaxLength_IsValid()
    {
        // 256 chars: starts with 'a', 254 x 'b', ends with 'c'
        var name = "a" + new string('b', 254) + "c";
        Assert.Equal(256, name.Length);
        var result = _validator.ValidateApiName(name);
        Assert.True(result.IsValid);
    }

    [Fact]
    public void ValidateApiName_Null_ReturnsInvalid()
    {
        var result = _validator.ValidateApiName(null!);
        Assert.False(result.IsValid);
    }

    [Fact]
    public void ValidateApiName_WhitespaceOnly_ReturnsInvalid()
    {
        var result = _validator.ValidateApiName("   ");
        Assert.False(result.IsValid);
    }

    [Theory]
    [InlineData("has+plus")]
    [InlineData("has:colon")]
    [InlineData("has@at")]
    public void ValidateApiName_SpecialChars_ReturnsInvalid(string name)
    {
        var result = _validator.ValidateApiName(name);
        Assert.False(result.IsValid);
    }

    // ---- Operation ID boundary lengths ----

    [Fact]
    public void ValidateOperationId_ExactlyMaxLength_IsValid()
    {
        var id = "a" + new string('b', 78) + "c"; // 80 chars
        Assert.Equal(80, id.Length);
        var result = _validator.ValidateOperationId(id);
        Assert.True(result.IsValid);
    }

    [Fact]
    public void ValidateOperationId_Null_ReturnsInvalid()
    {
        var result = _validator.ValidateOperationId(null!);
        Assert.False(result.IsValid);
    }

    [Theory]
    [InlineData("underscores_ok")]
    [InlineData("a_b_c")]
    [InlineData("op-1_v2")]
    public void ValidateOperationId_Underscores_AreValid(string id)
    {
        var result = _validator.ValidateOperationId(id);
        Assert.True(result.IsValid);
    }

    // ---- Display name edge cases ----

    [Fact]
    public void ValidateDisplayName_TooLong_ReturnsInvalid()
    {
        var longName = new string('A', 301);
        var result = _validator.ValidateDisplayName(longName);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("300"));
    }

    [Fact]
    public void ValidateDisplayName_ExactlyMaxLength_IsValid()
    {
        var name = new string('A', 300);
        var result = _validator.ValidateDisplayName(name);
        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData("Name with & ampersand", false)]
    [InlineData("Name with ? question", false)]
    [InlineData("Name with + plus", false)]
    [InlineData("Name-with-hyphens_and (parens)", true)]
    [InlineData("Name / with / slashes", true)]
    public void ValidateDisplayName_SpecialChars_Validated(string name, bool expected)
    {
        var result = _validator.ValidateDisplayName(name);
        Assert.Equal(expected, result.IsValid);
    }

    // ---- API path edge cases ----

    [Fact]
    public void ValidateApiPath_TooLong_ReturnsInvalid()
    {
        var longPath = new string('a', 401);
        var result = _validator.ValidateApiPath(longPath);
        Assert.False(result.IsValid);
    }

    [Fact]
    public void ValidateApiPath_ExactlyMaxLength_IsValid()
    {
        var path = new string('a', 400);
        var result = _validator.ValidateApiPath(path);
        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData("path/with/dots.v1", true)]
    [InlineData("under_score/ok", true)]
    [InlineData("path?query=bad", false)]
    [InlineData("path#fragment", false)]
    public void ValidateApiPath_VariousPatterns(string path, bool expected)
    {
        var result = _validator.ValidateApiPath(path);
        Assert.Equal(expected, result.IsValid);
    }

    // ---- Resource group edge cases ----

    [Fact]
    public void ValidateResourceGroupName_TooLong_ReturnsInvalid()
    {
        var longName = new string('a', 91);
        var result = _validator.ValidateResourceGroupName(longName);
        Assert.False(result.IsValid);
    }

    [Fact]
    public void ValidateResourceGroupName_ExactlyMaxLength_IsValid()
    {
        var name = new string('a', 90);
        var result = _validator.ValidateResourceGroupName(name);
        Assert.True(result.IsValid);
    }

    // ---- Sanitize edge cases ----

    [Fact]
    public void SanitizeApiName_VeryLongInput_TruncatesToMaxLength()
    {
        var input = new string('a', 500);
        var result = _validator.SanitizeApiName(input);
        Assert.True(result.Length <= ApimNamingRules.ApiNameMaxLength);
        Assert.True(_validator.ValidateApiName(result).IsValid);
    }

    [Fact]
    public void SanitizeOperationId_VeryLongInput_TruncatesToMaxLength()
    {
        var input = new string('x', 200);
        var result = _validator.SanitizeOperationId(input);
        Assert.True(result.Length <= ApimNamingRules.OperationIdMaxLength);
        Assert.True(_validator.ValidateOperationId(result).IsValid);
    }

    [Fact]
    public void SanitizeApiPath_VeryLongInput_TruncatesToMaxLength()
    {
        var input = new string('a', 500);
        var result = _validator.SanitizeApiPath(input);
        Assert.True(result.Length <= ApimNamingRules.ApiPathMaxLength);
    }

    [Theory]
    [InlineData("---only-hyphens---", "only-hyphens")]
    [InlineData("!!!nothing-valid!!!", "nothing-valid")]
    [InlineData("CamelCase", "camelcase")]
    public void SanitizeApiName_VariousInputs_ProducesValidOutput(string input, string expected)
    {
        var result = _validator.SanitizeApiName(input);
        Assert.Equal(expected, result);
        Assert.True(_validator.ValidateApiName(result).IsValid);
    }

    [Fact]
    public void SanitizeApiName_AllInvalidChars_ReturnsFallback()
    {
        var result = _validator.SanitizeApiName("!@#$%^&*()");
        Assert.Equal("api", result);
    }

    [Fact]
    public void SanitizeOperationId_AllInvalidChars_ReturnsFallback()
    {
        var result = _validator.SanitizeOperationId("!@#$%^&*()");
        Assert.Equal("operation", result);
    }

    // ---- NamingValidationResult factory tests ----

    [Fact]
    public void NamingValidationResult_Valid_HasNoErrors()
    {
        var result = NamingValidationResult.Valid();
        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void NamingValidationResult_Invalid_ContainsAllErrors()
    {
        var result = NamingValidationResult.Invalid("Error 1", "Error 2", "Error 3");
        Assert.False(result.IsValid);
        Assert.Equal(3, result.Errors.Count);
    }
}
