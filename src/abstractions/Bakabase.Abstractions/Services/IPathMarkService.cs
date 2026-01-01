using System.Linq.Expressions;
using Bakabase.Abstractions.Models.Db;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Models.Input;

namespace Bakabase.Abstractions.Services;

public interface IPathMarkService
{
    /// <summary>
    /// Get all path marks
    /// </summary>
    Task<List<PathMark>> GetAll(Expression<Func<PathMarkDbModel, bool>>? filter = null, PathMarkAdditionalItem additionalItems = PathMarkAdditionalItem.None);

    /// <summary>
    /// Get a path mark by ID
    /// </summary>
    Task<PathMark?> Get(int id);

    /// <summary>
    /// Get path marks by root path
    /// </summary>
    Task<List<PathMark>> GetByPath(string path, bool includeDeleted = false);

    /// <summary>
    /// Get all distinct paths that have marks
    /// </summary>
    Task<List<string>> GetAllPaths();

    /// <summary>
    /// Get all pending marks (status = Pending or PendingDelete)
    /// </summary>
    Task<List<PathMark>> GetPendingMarks();

    /// <summary>
    /// Get marks by sync status
    /// </summary>
    Task<List<PathMark>> GetBySyncStatus(PathMarkSyncStatus status);

    /// <summary>
    /// Add a new path mark
    /// </summary>
    Task<PathMark> Add(PathMark mark);

    /// <summary>
    /// Add multiple path marks
    /// </summary>
    Task<List<PathMark>> AddRange(List<PathMark> marks);

    /// <summary>
    /// Update an existing path mark
    /// </summary>
    Task Update(PathMark mark);

    /// <summary>
    /// Soft delete a path mark (mark as PendingDelete)
    /// </summary>
    Task SoftDelete(int id);

    /// <summary>
    /// Soft delete all marks for a path
    /// </summary>
    Task SoftDeleteByPath(string path);

    /// <summary>
    /// Hard delete a path mark (remove from database)
    /// </summary>
    Task HardDelete(int id);

    /// <summary>
    /// Hard delete multiple path marks (batch operation)
    /// </summary>
    Task HardDeleteBatch(IEnumerable<int> ids);

    /// <summary>
    /// Mark a path mark as syncing
    /// </summary>
    Task MarkAsSyncing(int id);

    /// <summary>
    /// Mark a path mark as synced
    /// </summary>
    Task MarkAsSynced(int id);

    /// <summary>
    /// Mark multiple path marks as synced (batch operation)
    /// </summary>
    Task MarkAsSyncedBatch(IEnumerable<int> ids);

    /// <summary>
    /// Mark a path mark as failed
    /// </summary>
    Task MarkAsFailed(int id, string? error);

    /// <summary>
    /// Mark a path mark as pending (for re-sync after expiration)
    /// </summary>
    Task MarkAsPending(int id);

    /// <summary>
    /// Mark multiple path marks as pending (batch operation)
    /// </summary>
    Task MarkAsPendingBatch(IEnumerable<int> ids);

    /// <summary>
    /// Get pending marks count
    /// </summary>
    Task<int> GetPendingCount();

    /// <summary>
    /// Preview matched paths for a path mark (without saving) with detailed information (resource layer, property values)
    /// </summary>
    Task<List<PathMarkPreviewResult>> PreviewMatchedPaths(PathMarkPreviewRequest request);

    /// <summary>
    /// Migrate all marks from old path to new path.
    /// This updates the path of all marks and resets them to pending sync status.
    /// </summary>
    Task MigratePath(string oldPath, string newPath);
}
