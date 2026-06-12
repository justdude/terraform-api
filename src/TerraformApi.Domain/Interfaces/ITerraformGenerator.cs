using TerraformApi.Domain.Models;

namespace TerraformApi.Domain.Interfaces;

public interface ITerraformGenerator
{
    string Generate(ApimConfiguration configuration);

    /// <summary>Generates a standalone <c>product = [ ... ]</c> block for one product.</summary>
    string GenerateProductBlock(ApimProduct product);
}
