using TerraformApi.Domain.Models;

namespace TerraformApi.Api.Dtos;

public static class DtoMapper
{
    public static ConversionSettings ToSettings(ConvertRequest request) =>
        new()
        {
            Environment = request.Environment,
            ApiGroupName = request.ApiGroupName,
            StageGroupName = request.StageGroupName,
            ApimName = request.ApimName,
            ApiName = request.ApiName,
            ApiDisplayName = request.ApiDisplayName,
            ApiPathPrefix = request.ApiPathPrefix,
            ApiPathSuffix = request.ApiPathSuffix,
            ApiGatewayHost = request.ApiGatewayHost,
            ApiVersion = request.ApiVersion,
            BackendServicePath = request.BackendServicePath,
            Revision = request.Revision,
            ProductId = request.ProductId,
            FrontendHost = request.FrontendHost,
            CompanyDomain = request.CompanyDomain,
            LocalDevHost = request.LocalDevHost,
            LocalDevPort = request.LocalDevPort,
            SubscriptionRequired = request.SubscriptionRequired,
            IncludeCorsPolicy = request.IncludeCorsPolicy,
            OperationPrefix = request.OperationPrefix,
            AllowedOrigins = request.AllowedOrigins,
            AllowedMethods = request.AllowedMethods,
            GenerateProduct = request.GenerateProduct,
            ProductDisplayName = request.ProductDisplayName,
            ProductDescription = request.ProductDescription,
            ProductSubscriptionRequired = request.ProductSubscriptionRequired,
            ProductApprovalRequired = request.ProductApprovalRequired
        };

    public static ConvertResponse ToResponse(ConversionResult result)
    {
        var response = new ConvertResponse
        {
            Success = result.Success,
            TerraformConfig = result.TerraformConfig,
            Warnings = result.Warnings,
            Errors = result.Errors
        };

        if (result.Configuration != null)
        {
            return response with
            {
                Summary = new ApiSummary
                {
                    ApiName = result.Configuration.Api.Name,
                    DisplayName = result.Configuration.Api.DisplayName,
                    Path = result.Configuration.Api.Path,
                    OperationCount = result.Configuration.ApiOperations.Count,
                    Operations = result.Configuration.ApiOperations.Select(op => new OperationSummary
                    {
                        OperationId = op.OperationId,
                        Method = op.Method,
                        UrlTemplate = op.UrlTemplate
                    }).ToList()
                }
            };
        }

        return response;
    }
}
