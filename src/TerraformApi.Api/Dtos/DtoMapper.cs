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

    public static EnvironmentTransformSettings ToTransformSettings(TransformRequest request) =>
        new()
        {
            SourceEnvironment = request.SourceEnvironment,
            TargetEnvironment = request.TargetEnvironment,
            TargetStageGroupName = request.TargetStageGroupName,
            TargetApimName = request.TargetApimName,
            TargetApiGatewayHost = request.TargetApiGatewayHost,
            TargetFrontendHost = request.TargetFrontendHost,
            TargetCompanyDomain = request.TargetCompanyDomain,
            TargetLocalDevHost = request.TargetLocalDevHost,
            TargetLocalDevPort = request.TargetLocalDevPort,
            TargetSubscriptionRequired = request.TargetSubscriptionRequired
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

    public static TransformResponse ToTransformResponse(EnvironmentTransformResult result) =>
        new()
        {
            Success = result.Success,
            TransformedTerraform = result.TransformedTerraform,
            DetectedSourceEnvironment = result.DetectedSourceEnvironment,
            Summary = result.Summary != null
                ? new TransformSummaryDto
                {
                    TotalOperations = result.Summary.TotalOperations,
                    SyncedOperations = result.Summary.SyncedOperations,
                    AddedOperations = result.Summary.AddedOperations,
                    PreservedOperations = result.Summary.PreservedOperations
                }
                : null,
            Warnings = result.Warnings,
            Errors = result.Errors
        };
}
