using Bakabase.Service.Components.Acquisition.Planning;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;

namespace Bakabase.Tests;

[TestClass]
public sealed class AcquisitionDeletionProgressTests
{
    private static AcquisitionTaskDeletePreviewStore.Entry Entry(DateTimeOffset expiry) => new(
        new(Guid.NewGuid().ToString("N"), expiry, 1, 1, 0, [new(1, "fixture", true, null)]),
        new Dictionary<int, string> { [1] = "version" },
        new Dictionary<int, IReadOnlyList<int>> { [1] = [2] });

    [TestMethod]
    public void PollingNeverClaimsOrExtendsAnUnusedConfirmation()
    {
        var store = new AcquisitionTaskDeletePreviewStore();
        var entry = Entry(DateTimeOffset.UtcNow.AddMinutes(1)); store.Add(entry);
        var before = store.ReadProgress(entry.Preview.PreviewId);
        Assert.AreEqual(before, store.ReadProgress(entry.Preview.PreviewId));
        Assert.IsFalse(entry.Claimed);
        var expired = Entry(DateTimeOffset.UtcNow.AddSeconds(-1)); store.Add(expired);
        Assert.ThrowsException<ArgumentException>(() => store.ReadProgress(expired.Preview.PreviewId));
        Assert.ThrowsException<ArgumentException>(() => store.Claim(expired.Preview.PreviewId));
    }

    [TestMethod]
    public void LongRunningCleanupSurvivesPreviewExpiryWithoutGrantingAnotherDelete()
    {
        var store = new AcquisitionTaskDeletePreviewStore();
        var entry = Entry(DateTimeOffset.UtcNow.AddMinutes(-20));
        entry.Claimed = true;
        entry.Progress = entry.Progress with { State = "running", StartedAt = DateTimeOffset.UtcNow.AddMinutes(-20) };
        store.Add(entry); store.Add(Entry(DateTimeOffset.UtcNow.AddMinutes(1)));
        Assert.AreEqual("running", store.ReadProgress(entry.Preview.PreviewId).State);
        Assert.ThrowsException<ArgumentException>(() => store.Claim(entry.Preview.PreviewId));
        entry.Progress = entry.Progress with { State = "completed", Phase = "completed", UpdatedAt = DateTimeOffset.UtcNow };
        Assert.AreEqual("completed", store.ReadProgress(entry.Preview.PreviewId).State);
        entry.Progress = entry.Progress with { UpdatedAt = DateTimeOffset.UtcNow.AddMinutes(-11) };
        Assert.ThrowsException<ArgumentException>(() => store.ReadProgress(entry.Preview.PreviewId));
    }

    [TestMethod]
    public void Confirmation_rejects_unaccepted_skip_or_record_fallback_without_consuming_the_token()
    {
        var store = new AcquisitionTaskDeletePreviewStore();
        var entry = Entry(DateTimeOffset.UtcNow.AddMinutes(1)); store.Add(entry);
        var id = entry.Preview.PreviewId;
        Assert.ThrowsException<ArgumentException>(() =>
            store.Claim(new AcquisitionTaskDeleteInput(id, CleanupFiles: true, SkipFileVerification: true)));
        Assert.ThrowsException<ArgumentException>(() =>
            store.Claim(new AcquisitionTaskDeleteInput(id, RemoveTaskIfCleanupFails: true)));
        Assert.IsFalse(entry.Claimed);
        Assert.AreEqual(entry, store.Claim(id));
    }

    [TestMethod]
    public void Only_receipted_cancellation_fields_may_change_during_partial_file_cleanup()
    {
        var old = new AcquisitionDeletionRun(2, WorkflowRunStatus.Waiting, 1, "files", DateTime.UtcNow, null, null);
        var current = old with { Id = 3, Status = WorkflowRunStatus.Success, CompletedAt = DateTime.UtcNow };
        var before = new AcquisitionDeletionActivity([2, 3], "before", null) { ObservedRuns = [old, current] };
        var closed = old with { Status = WorkflowRunStatus.Cancelled, CompletedAt = DateTime.UtcNow };
        var after = before with { Version = "after", ObservedRuns = [closed, current] };
        Assert.IsTrue(before.MatchesAfterCleanup(after, [2]));
        Assert.IsFalse(before.MatchesAfterCleanup(after, []));
        Assert.IsFalse(before.MatchesAfterCleanup(after with { ObservedRuns = [closed with {CurrentStepIndex = 5}, current] }, [2]));
        Assert.IsFalse(before.MatchesAfterCleanup(after with { ObservedRuns = [closed, current with {PendingSignalJson = "{}"}] }, [2]));
        Assert.IsFalse(before.MatchesAfterCleanup(after with { RunIds = [2, 4] }, [2]));
    }
}
