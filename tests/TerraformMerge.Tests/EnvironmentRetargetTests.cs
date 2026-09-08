using TerraformApi.Domain.Models.Hcl;
using TerraformMerge.Engine;

namespace TerraformMerge.Tests;

/// <summary>
/// Moving an operation between environments: which values the destination
/// environment supplies, which are rewritten from the operation's own value,
/// and which are never touched.
/// </summary>
public class EnvironmentRetargetTests
{
    private static OperationNode Op(
        string environment,
        string id = "list-orders",
        OperationSource source = OperationSource.TargetTerraform) => new()
    {
        Source = source,
        OperationId = $"{id}-{environment}",
        Method = "GET",
        UrlTemplate = "orders",
        DisplayName = "List orders",
        Description = "Returns all orders",
        StatusCode = 200,
        ApiName = $"orders-api-{environment}",
        ApimResourceGroupName = $"rg-apim-{environment}",
        ApimName = $"apim-company-{environment}"
    };

    [Fact]
    public void Detect_ReadsTheEnvironmentOfAnOperation()
    {
        Assert.Equal("dev", EnvironmentCatalog.Detect(Op("dev")));
        Assert.Equal("qa", EnvironmentCatalog.Detect(Op("qa")));
    }

    [Fact]
    public void Detect_FallsBackToTheOperationIdWhenTheApimFieldsAreEmpty()
    {
        var node = new OperationNode
        {
            Source = OperationSource.TargetOpenApi,
            OperationId = "list-orders-qa",
            Method = "GET",
            UrlTemplate = "orders"
        };

        Assert.Equal("qa", EnvironmentCatalog.Detect(node));
    }

    [Fact]
    public void Dominant_IsTheEnvironmentMostOperationsAreIn()
    {
        Assert.Equal("qa", EnvironmentCatalog.Dominant([Op("qa"), Op("qa", "get-order"), Op("dev")]));
        Assert.Null(EnvironmentCatalog.Dominant([]));
    }

    [Fact]
    public void Choices_ListsLoadedEnvironmentsFirstThenTheRest()
    {
        var choices = EnvironmentCatalog.Build([Op("qa")], [Op("dev")]).Choices();

        Assert.Equal(["dev", "qa"], choices.Take(2));
        Assert.Contains("staging", choices);          // offered even though no file uses it
        Assert.Equal(choices.Count, choices.Distinct().Count());
    }

    [Fact]
    public void Retarget_TakesTheDestinationEnvironmentsOwnValues()
    {
        var qa = new OperationNode
        {
            Source = OperationSource.OriginalTerraform,
            OperationId = "list-orders-qa",
            Method = "GET",
            UrlTemplate = "orders",
            ApiName = "orders-qa-api",              // qa names its api differently
            ApimResourceGroupName = "rg-apim-qa",
            ApimName = "apim-company-qa"
        };
        var catalog = EnvironmentCatalog.Build([qa], []);

        var moved = Op("dev", "get-order");
        Assert.True(EnvironmentRetargeter.Retarget(moved, "qa", catalog));

        Assert.Equal("orders-qa-api", moved.ApiName);          // from the qa file, not a rename
        Assert.Equal("rg-apim-qa", moved.ApimResourceGroupName);
        Assert.Equal("apim-company-qa", moved.ApimName);
        Assert.Equal("get-order-qa", moved.OperationId);       // its own id, re-stamped
    }

    [Fact]
    public void Retarget_RewritesTheEnvironmentTokenWhenTheDestinationIsUnknown()
    {
        var moved = Op("dev");

        // No catalog: nothing to copy from, so every value is rewritten from the
        // operation's own — this is the "qa config is still empty" case.
        Assert.True(EnvironmentRetargeter.Retarget(moved, "qa"));

        Assert.Equal("rg-apim-qa", moved.ApimResourceGroupName);
        Assert.Equal("apim-company-qa", moved.ApimName);
        Assert.Equal("orders-api-qa", moved.ApiName);
        Assert.Equal("list-orders-qa", moved.OperationId);
    }

    [Fact]
    public void Retarget_DisagreeingEnvironmentValuesFallBackToARewrite()
    {
        // Two qa api groups naming their apis differently: there is no single
        // right api_name, so the moved operation keeps its own, re-stamped.
        var groupA = Op("qa");
        var groupB = Op("qa", "list-stock");
        groupB.ApiName = "stock-api-qa";
        var catalog = EnvironmentCatalog.Build([groupA, groupB], []);

        var moved = Op("dev", "get-order");
        moved.ApiName = "payments-api-dev";
        Assert.True(EnvironmentRetargeter.Retarget(moved, "qa", catalog));

        Assert.Equal("payments-api-qa", moved.ApiName);
        Assert.Equal("rg-apim-qa", moved.ApimResourceGroupName); // both groups agree on this one
    }

    [Fact]
    public void Retarget_NeverTouchesTheRoute()
    {
        var moved = Op("dev");
        moved.UrlTemplate = "orders/dev/{orderId}";

        EnvironmentRetargeter.Retarget(moved, "qa");

        Assert.Equal("GET", moved.Method);
        Assert.Equal("orders/dev/{orderId}", moved.UrlTemplate); // the route is not an environment
        Assert.Equal(200, moved.StatusCode);
    }

    [Fact]
    public void Retarget_LeavesInterpolatedValuesVariable()
    {
        var moved = Op("dev");
        moved.ApimResourceGroupName = "${var.resource_group}";
        moved.OperationId = "${var.env}-list-orders";

        Assert.True(EnvironmentRetargeter.Retarget(moved, "qa"));

        Assert.Equal("${var.resource_group}", moved.ApimResourceGroupName);
        Assert.Equal("${var.env}-list-orders", moved.OperationId);
        Assert.Equal("apim-company-qa", moved.ApimName); // the literal fields still move
    }

    [Fact]
    public void Retarget_IsANoOpForAnOperationAlreadyInThatEnvironment()
    {
        var node = Op("qa");

        Assert.False(EnvironmentRetargeter.Retarget(node, "qa"));
        Assert.False(EnvironmentRetargeter.Retarget(node, "QA"));
        Assert.False(EnvironmentRetargeter.Retarget(node, "  "));
        Assert.Equal("list-orders-qa", node.OperationId);
        Assert.Empty(node.EditedFields);
    }

    [Fact]
    public void Retarget_StampsAnOperationThatCarriesNoEnvironmentAtAll()
    {
        var openApi = new OperationNode
        {
            Source = OperationSource.TargetOpenApi,
            OperationId = "listOrders",
            Method = "GET",
            UrlTemplate = "orders"
        };
        var catalog = EnvironmentCatalog.Build([Op("qa", source: OperationSource.OriginalTerraform)], []);

        Assert.True(EnvironmentRetargeter.Retarget(openApi, "qa", catalog));

        Assert.Equal("rg-apim-qa", openApi.ApimResourceGroupName);
        Assert.Equal("apim-company-qa", openApi.ApimName);
        Assert.Equal("orders-api-qa", openApi.ApiName);
        Assert.Equal("listOrders", openApi.OperationId); // no environment in it to rewrite
    }

    [Fact]
    public void Plan_DescribesTheMoveWithoutMakingIt()
    {
        var node = Op("dev");
        var plan = EnvironmentRetargeter.Plan(node, "qa");

        Assert.True(plan.HasChanges);
        Assert.Equal("dev", plan.FromEnvironment);
        Assert.Equal("qa", plan.ToEnvironment);
        Assert.Equal("list-orders-qa", plan.OperationId);
        Assert.True(plan.RenamesOperationId);
        Assert.Equal("rg-apim-qa", plan.Fields[OperationField.ApimResourceGroupName]);

        // The operation itself is untouched until the plan is applied.
        Assert.Equal("rg-apim-dev", node.ApimResourceGroupName);
        Assert.Equal("list-orders-dev", node.OperationId);
    }

    [Fact]
    public void Retarget_MarksTheMovedFieldsEditedSoAGeneratedBlockUsesThem()
    {
        var moved = Op("dev");
        EnvironmentRetargeter.Retarget(moved, "qa");

        var built = OperationHclBuilder.Build(moved, OperationTemplateContext.Placeholders);

        // The placeholder context would win for any field the move did not mark
        // edited, so these values prove the move reaches the generated block.
        Assert.Equal("rg-apim-qa", Literal(built, "apim_resource_group_name"));
        Assert.Equal("apim-company-qa", Literal(built, "apim_name"));
        Assert.Equal("orders-api-qa", Literal(built, "api_name"));
        Assert.Equal("list-orders-qa", Literal(built, "operation_id"));
    }

    private static string? Literal(HclObject obj, string key) =>
        obj.Get(key) is HclLiteral literal ? literal.RawValue : null;

    [Fact]
    public void Copy_IsIndependentOfTheOperationItCameFrom()
    {
        var source = Op("dev");
        var copy = source.Copy();

        EnvironmentRetargeter.Retarget(copy, "qa");

        Assert.Equal("rg-apim-qa", copy.ApimResourceGroupName);
        Assert.Equal("rg-apim-dev", source.ApimResourceGroupName);
        Assert.Equal("list-orders-dev", source.OperationId);
        Assert.Empty(source.EditedFields);
    }
}
