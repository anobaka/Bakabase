using System.Linq.Expressions;
using Bakabase.Abstractions.Models.Db;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;

namespace Bakabase.Abstractions.Services;

public interface IPathRuleQueueService
{
    /// <summary>
    /// Get all queue items
    /// </summary>
    Task<List<PathRuleQueueItem>> GetAll(Expression<Func<PathRuleQueueItemDbModel, bool>>? filter = null);

    /// <summary>
    /// Get pending items for processing
    /// </summary>
    Task<List<PathRuleQueueItem>> GetPendingItems(int batchSize = 100);

    /// <summary>
    /// Enqueue a new item
    /// </summary>
    Task<PathRuleQueueItem> Enqueue(PathRuleQueueItem item);

    /// <summary>
    /// Enqueue multiple items
    /// </summary>
    Task EnqueueRange(IEnumerable<PathRuleQueueItem> items);

    /// <summary>
    /// Update item status
    /// </summary>
    Task UpdateStatus(int id, RuleQueueStatus status, string? error = null);

    /// <summary>
    /// Mark item as processing
    /// </summary>
    Task MarkAsProcessing(int id);

    /// <summary>
    /// Mark item as completed
    /// </summary>
    Task MarkAsCompleted(int id);

    /// <summary>
    /// Mark item as failed
    /// </summary>
    Task MarkAsFailed(int id, string error);

    /// <summary>
    /// Delete completed items
    /// </summary>
    Task DeleteCompletedItems();

    /// <summary>
    /// Delete all items
    /// </summary>
    Task DeleteAll();

    /// <summary>
    /// Get queue statistics
    /// </summary>
    Task<QueueStatistics> GetStatistics();
}

public class QueueStatistics
{
    public int PendingCount { get; set; }
    public int ProcessingCount { get; set; }
    public int CompletedCount { get; set; }
    public int FailedCount { get; set; }
    public int TotalCount => PendingCount + ProcessingCount + CompletedCount + FailedCount;
}
