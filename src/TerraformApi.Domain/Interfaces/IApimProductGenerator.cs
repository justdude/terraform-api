using TerraformApi.Domain.Models;

namespace TerraformApi.Domain.Interfaces;

/// <summary>
/// Generates a standalone APIM <c>product = [ ... ]</c> Terraform block.
/// Missing settings become placeholder tags; the output starts with a comment
/// explaining every tag used.
/// </summary>
public interface IApimProductGenerator
{
    ProductGenerationResult Generate(ApimProductRequest request);
}
