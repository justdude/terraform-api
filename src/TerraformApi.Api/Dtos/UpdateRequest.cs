using System.ComponentModel.DataAnnotations;

namespace TerraformApi.Api.Dtos;

public class UpdateRequest : ConvertRequest
{
    [Required]
    public required string ExistingTerraform { get; init; }
}
