using Bakabase.Abstractions.Components.Tasks;
using Bakabase.Abstractions.Models.Db;
using Bakabase.Abstractions.Models.View;
using Bootstrap.Models.ResponseModels;

namespace Bakabase.Abstractions.Services;

/// <summary>
/// Physically moves resource files to a destination directory through a background task,
/// keeping a durable per-resource record (<see cref="ResourceMoveRecordDbModel"/>) so
/// interrupted or failed moves stay visible and retryable across restarts.
/// </summary>
public interface IResourceMoveService
{
    /// <summary>
    /// Validate the request, persist one Pending record per top-level resource and enqueue the
    /// batch's executor task. Returns the batch id.
    /// </summary>
    Task<SingletonResponse<string>> CreateBatch(int[] resourceIds, string destDir);

    /// <summary>
    /// Dry-run of a move: effective top-level resources with destination paths, per-resource
    /// conflict flags, and the property/media-library marks expected to cover the destination.
    /// </summary>
    Task<SingletonResponse<ResourceMovePreviewViewModel>> Preview(int[] resourceIds, string destDir);

    /// <summary>Most recent records first.</summary>
    Task<List<ResourceMoveRecordDbModel>> GetRecords(int maxCount = 100);

    /// <summary>
    /// Re-run one Failed / Interrupted / Cancelled record. Idempotent towards a half-done move:
    /// when the source is gone but the destination exists, the physical move is skipped and only
    /// the post-move fixups run.
    /// </summary>
    Task<BaseResponse> Retry(int recordId);

    Task<BaseResponse> DeleteRecord(int recordId);

    /// <summary>Delete all records not currently Pending or Moving.</summary>
    Task<BaseResponse> DeleteInactiveRecords();

    /// <summary>Task body of one batch; runs inside the BTask.</summary>
    Task ExecuteBatch(string batchId, BTaskArgs args);

    /// <summary>
    /// Startup reconciliation: records left Pending or Moving by a dead process become
    /// Interrupted — their executor task no longer exists and a half-done physical move is not
    /// safe to auto-resume. The user retries explicitly.
    /// </summary>
    Task MarkInterruptedOnStartup();
}
