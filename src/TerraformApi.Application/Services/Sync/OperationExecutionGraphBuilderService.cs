using TerraformApi.Domain.Interfaces;
using TerraformApi.Domain.Models.Sync;
using TerraformApi.Domain.Models.Tracking;

namespace TerraformApi.Application.Services.Sync;

/// <summary>
/// Maps a <see cref="SyncReport"/> onto the execution graph:
/// AddedFromOpenApi → New, Changed → Modified, Identical/Preserved → Included,
/// diffs that applied nothing due to policy → Skipped when not in the config.
/// </summary>
public sealed class OperationExecutionGraphBuilderService : IOperationExecutionGraphBuilder
{
    /// <inheritdoc />
    public OperationExecutionGraph BuildFromSyncReport(SyncReport report, string apiGroupName)
    {
        var nodes = new List<OperationNode>();

        foreach (var diff in report.Diffs)
        {
            var fingerprint = diff.TerraformFingerprint ?? diff.OpenApiFingerprint;
            var operationId = fingerprint?.OperationId
                ?? $"{fingerprint?.Method} {fingerprint?.UrlTemplate}".Trim();

            var (status, origin) = diff.Kind switch
            {
                OperationDiffKind.AddedFromOpenApi when diff.AppliedChanges.Count > 0 =>
                    (OperationNodeStatus.New, OperationNodeOrigin.OpenApi),
                OperationDiffKind.AddedFromOpenApi =>
                    (OperationNodeStatus.Skipped, OperationNodeOrigin.OpenApi),
                OperationDiffKind.Changed =>
                    (OperationNodeStatus.Modified, OperationNodeOrigin.Both),
                OperationDiffKind.Identical =>
                    (OperationNodeStatus.Included, OperationNodeOrigin.Both),
                OperationDiffKind.PreservedFromTerraform =>
                    (OperationNodeStatus.Included, OperationNodeOrigin.ExistingTerraform),
                _ =>
                    (OperationNodeStatus.Skipped, OperationNodeOrigin.ExistingTerraform)
            };

            nodes.Add(new OperationNode
            {
                OperationId = string.IsNullOrEmpty(operationId) ? "(unknown)" : operationId,
                Method = fingerprint?.Method,
                UrlTemplate = fingerprint?.UrlTemplate,
                Status = status,
                Origin = origin,
                AppliedChanges = diff.AppliedChanges
            });
        }

        var newCount = nodes.Count(n => n.Status == OperationNodeStatus.New);
        var modifiedCount = nodes.Count(n => n.Status == OperationNodeStatus.Modified);
        var skippedCount = nodes.Count(n => n.Status == OperationNodeStatus.Skipped);
        var includedCount = nodes.Count - skippedCount;

        return new OperationExecutionGraph
        {
            ApiGroupName = apiGroupName,
            GeneratedAt = report.GeneratedAt,
            Nodes = nodes,
            Statistics = new GraphStatistics
            {
                TotalOperations = includedCount,
                NewOperations = newCount,
                IncludedOperations = includedCount,
                ModifiedOperations = modifiedCount,
                PreservedOperations = report.OperationsPreserved,
                SkippedOperations = skippedCount
            }
        };
    }
}
