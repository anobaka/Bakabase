using System.Text.Json;
using Bakabase.Modules.Acquisition.Abstractions.Models.Db;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain.Constants;

namespace Bakabase.Tests;

public sealed partial class AcquisitionAttentionSearchTests
{
    [TestMethod]
    public async Task QueueBadgeCountsCurrentOpenTasksAcrossAllBatchesWithoutHistoryDuplicatesOrDeletedRows()
    {
        void Add(string state, bool deleted = false, AcquisitionStatus status = AcquisitionStatus.Waiting) =>
            _db.AcquisitionTasks.Add(new AcquisitionTaskDbModel {Title = "queue", BuilderStateJson = state,
                Status = status, CreatedAt = DateTime.Now, UpdatedAt = DateTime.Now,
                DeletedAt = deleted ? DateTime.Now : null});
        foreach (var status in new[] {"queued", "starting", "running", "paused", "spaceBlocked"})
            Add(JsonSerializer.Serialize(new {localRecoveryQueue = new {batchId = status, status}}, Json));
        foreach (var status in new[] {"queued", "checking"})
            Add(JsonSerializer.Serialize(new {continuationRequest = new {batchId = status, status}}, Json));
        // A new queue request can be submitted for a completed task; queue entry state owns this count.
        Add("""{"localRecoveryQueue":{"batchId":"both","status":"queued"},"continuationRequest":{"batchId":"both","status":"queued"}}""",
            status: AcquisitionStatus.Completed);
        Add("""{"localRecoveryQueue":{"batchId":"deleted","status":"queued"}}""", deleted: true);
        foreach (var status in new[] {"completed", "attention", "cancelled", "skipped"})
            Add(JsonSerializer.Serialize(new {localRecoveryQueue = new {batchId = status, status},
                continuationRequest = new {batchId = status, status}}, Json));
        Add("""{"localRecoveryQueueHistory":[{"batchId":"old","status":"queued"}],"continuationRequestHistory":[{"batchId":"old","status":"checking"}]}""");
        Add("""{"localRecoveryQueue":{"status":"queued"},"continuationRequest":{"batchId":"","status":"checking"}}""");
        Add("invalid JSON");
        Add("null");
        await _db.SaveChangesAsync();
        var page = await _search.SearchAsync(new(Keyword: "no matches", PageSize: 1));
        Assert.AreEqual(0, page.TotalCount);
        Assert.AreEqual(8, page.QueueTaskCount, "The badge is global and counts each task once.");
    }

    [TestMethod]
    public async Task QueueBadgeDoesNotTruncateToTheMostRecentThirtyBatches()
    {
        for (var i = 0; i < 41; i++)
            _db.AcquisitionTasks.Add(new AcquisitionTaskDbModel {Title = "queue " + i,
                Status = AcquisitionStatus.Waiting, CreatedAt = DateTime.Now, UpdatedAt = DateTime.Now,
                BuilderStateJson = JsonSerializer.Serialize(new {localRecoveryQueue = new {batchId = i.ToString(), status = "paused"}}, Json)});
        await _db.SaveChangesAsync();
        var page = await _search.SearchAsync(new(PageSize: 1));
        Assert.AreEqual(1, page.Items.Count);
        Assert.AreEqual(41, page.QueueTaskCount);
    }
}
