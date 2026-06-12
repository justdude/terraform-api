using TerraformApi.Domain.Models.Sync;
using TerraformApi.Domain.Models.Tracking;

namespace TerraformApi.Domain.Interfaces;

/// <summary>
/// Builds an <see cref="OperationExecutionGraph"/> from a completed
/// <see cref="SyncReport"/>. Pure consumer — contains no parsing logic.
/// </summary>
public interface IOperationExecutionGraphBuilder
{
    OperationExecutionGraph BuildFromSyncReport(SyncReport report, string apiGroupName);
}
