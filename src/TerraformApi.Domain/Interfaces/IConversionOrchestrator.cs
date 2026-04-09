using TerraformApi.Domain.Models;

namespace TerraformApi.Domain.Interfaces;

public interface IConversionOrchestrator
{
    ConversionResult Convert(string openApiJson, ConversionSettings settings);
    ConversionResult Update(string openApiJson, string existingTerraform, ConversionSettings settings);
}
