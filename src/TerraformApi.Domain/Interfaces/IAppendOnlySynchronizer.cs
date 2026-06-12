using TerraformApi.Domain.Models;
using TerraformApi.Domain.Models.Apim;
using TerraformApi.Domain.Models.Sync;

namespace TerraformApi.Domain.Interfaces;

/// <summary>
/// Append-only synchronization of an existing Terraform document with a new
/// OpenAPI-derived configuration. Never deletes anything: unknown operations
/// are preserved, matched operations are enriched only per policy, and new
/// operations are appended.
/// </summary>
public interface IAppendOnlySynchronizer
{
    SyncResult Synchronize(
        ParsedApimDocument existingParsed,
        ApimConfiguration newConfiguration,
        MergePolicy policy,
        OperationMatchStrategy matchStrategy,
        SyncOptions? options = null);
}
