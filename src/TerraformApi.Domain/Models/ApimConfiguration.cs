namespace TerraformApi.Domain.Models;

public sealed record ApimConfiguration
{
    public required string ApiGroupName { get; init; }
    public List<string> Products { get; init; } = [];
    public required ApimApi Api { get; init; }
    public List<ApimApiOperation> ApiOperations { get; init; } = [];
}

public sealed record ApimApi
{
    public required string ApimResourceGroupName { get; init; }
    public required string ApimName { get; init; }
    public required string Name { get; init; }
    public required string DisplayName { get; init; }
    public required string Path { get; init; }
    public required string ServiceUrl { get; init; }
    public List<string> Protocols { get; init; } = ["https"];
    public string Revision { get; init; } = "1";
    public bool SoapPassThrough { get; init; }
    public bool SubscriptionRequired { get; init; }
    public string? ProductId { get; init; }
    public string? SubscriptionKeyParameterNames { get; init; }
    public string? Policy { get; init; }
}

public sealed record ApimApiOperation
{
    public required string OperationId { get; init; }
    public required string ApimResourceGroupName { get; init; }
    public required string ApimName { get; init; }
    public required string ApiName { get; init; }
    public required string DisplayName { get; init; }
    public required string Method { get; init; }
    public required string UrlTemplate { get; init; }
    public int StatusCode { get; init; } = 200;
    public string Description { get; init; } = "";
    public List<ApimOperationRequest> Requests { get; init; } = [];
    public List<ApimOperationResponse> Responses { get; init; } = [];
}

public sealed record ApimOperationRequest
{
    public List<ApimParameter> Headers { get; init; } = [];
    public List<ApimParameter> QueryParameters { get; init; } = [];
    public List<ApimRepresentation> Representations { get; init; } = [];
}

public sealed record ApimOperationResponse
{
    public int StatusCode { get; init; }
    public string Description { get; init; } = "";
    public List<ApimRepresentation> Representations { get; init; } = [];
}

public sealed record ApimParameter
{
    public required string Name { get; init; }
    public bool Required { get; init; }
    public string Type { get; init; } = "string";
    public string Description { get; init; } = "";
}

public sealed record ApimRepresentation
{
    public required string ContentType { get; init; }
    public string? SchemaId { get; init; }
    public string? TypeName { get; init; }
}
