using TerraformApi.Domain.Models;

namespace TerraformApi.Domain.Interfaces;

public interface IOpenApiParser
{
    ApimConfiguration Parse(string openApiJson, ConversionSettings settings);
}
