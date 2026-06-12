using Microsoft.AspNetCore.Mvc;
using TerraformApi.Domain.Interfaces;
using TerraformApi.Domain.Models;

namespace TerraformApi.Api.Controllers;

/// <summary>
/// Standalone APIM product Terraform generation.
/// </summary>
[ApiController]
[Route("api")]
[Produces("application/json")]
public sealed class ProductsController : ControllerBase
{
    private readonly IApimProductGenerator _productGenerator;

    public ProductsController(IApimProductGenerator productGenerator) =>
        _productGenerator = productGenerator;

    /// <summary>
    /// Generates a standalone APIM <c>product = [ ... ]</c> Terraform block.
    /// </summary>
    /// <remarks>
    /// All settings are optional: anything not provided is generated with a
    /// placeholder tag (e.g. <c>{product-id}</c>, <c>{apim-name}</c>) that you
    /// replace later — the output starts with a comment explaining each tag.
    /// </remarks>
    [HttpPost("generate-product")]
    [ProducesResponseType(typeof(ProductGenerationResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProductGenerationResult), StatusCodes.Status400BadRequest)]
    public IActionResult GenerateProduct([FromBody] ApimProductRequest request)
    {
        var result = _productGenerator.Generate(request);
        return result.Success ? Ok(result) : BadRequest(result);
    }
}
