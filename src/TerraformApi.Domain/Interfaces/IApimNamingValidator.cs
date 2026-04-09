using TerraformApi.Domain.Validation;

namespace TerraformApi.Domain.Interfaces;

public interface IApimNamingValidator
{
    NamingValidationResult ValidateApiName(string name);
    NamingValidationResult ValidateOperationId(string operationId);
    NamingValidationResult ValidateDisplayName(string displayName);
    NamingValidationResult ValidateApiPath(string path);
    NamingValidationResult ValidateResourceGroupName(string name);
    string SanitizeApiName(string input);
    string SanitizeOperationId(string input);
    string SanitizeApiPath(string input);
}
