using TerraformApi.Domain.Models;

namespace TerraformApi.Domain.Interfaces;

public interface ITerraformMerger
{
    string Merge(string existingTerraform, ApimConfiguration newConfiguration);
}
