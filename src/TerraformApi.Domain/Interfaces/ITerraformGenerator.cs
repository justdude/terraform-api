using TerraformApi.Domain.Models;

namespace TerraformApi.Domain.Interfaces;

public interface ITerraformGenerator
{
    string Generate(ApimConfiguration configuration);
}
