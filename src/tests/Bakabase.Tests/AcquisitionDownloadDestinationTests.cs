using System.Text.Json;
using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.Modules.Acquisition.Models.Domain;
using Bakabase.Modules.Downloader.Cloud;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Workflow.Components;
using Bakabase.Service.Components.Acquisition.Planning;
using Bakabase.Service.Components.CloudDownloads;
using Bootstrap.Components.Configuration.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Tests;

public sealed partial class AcquisitionRecoveryTests
{
    private sealed class DestinationCapability : ICloudDownloadDestinationService
    {
        public List<int> Applied = [];
        public Dictionary<int, string> Blocked = [];
        public Func<Task>? BeforeApply;
        public Task<CloudDownloadDestinationPreview> PreviewAsync(int runId, CloudDownloadTarget target,
            string semanticOutputRoot, string newRoot, string newDirectory, CancellationToken ct, bool reuseExistingFiles = false, Func<string, Task>? guard = null)
        {
            var id = target.DownloadTaskId!.Value;
            return Task.FromResult(Blocked.TryGetValue(id, out var reason)
                ? new CloudDownloadDestinationPreview(false, reason)
                : new(true, "DOWNLOAD_DESTINATION_READY", "fingerprint-" + id, id, "operation-" + id,
                    Path.Combine(semanticOutputRoot, "old-" + id), newDirectory, newRoot,
                    ReuseEstimate: reuseExistingFiles ? new(2, 100, 1, 70) : null));
        }
        public async Task<CloudDownloadDestinationRevisionReceipt> ApplyAsync(int runId, CloudDownloadTarget target,
            string semanticOutputRoot, string newRoot, string newDirectory, string expectedFingerprint,
            string revisionId, CancellationToken ct, Func<CancellationToken, Task>? validateOwner = null, bool reuseExistingFiles = false, Func<string, Task>? guard = null)
        {
            if (BeforeApply != null) await BeforeApply();
            if (validateOwner != null) await validateOwner(ct);
            var id = target.DownloadTaskId!.Value; Applied.Add(id);
            return new(id, "operation-" + id, new(revisionId, Path.Combine(semanticOutputRoot, "old-" + id),
                newDirectory, newRoot, DateTime.UtcNow, expectedFingerprint));
        }
    }
    private AcquisitionDownloadDestinationService DestinationService(DestinationCapability provider) => new(_db, provider,
        new(), new UserStoragePolicy(false), new(_services.GetRequiredService<IBOptions<AcquisitionOptions>>(), new UserStoragePolicy(false)));
    private static Task AllowDirectory(string _) => Task.CompletedTask;
    private async Task<(int TaskId, int RunId)> SeedDestination(int downloadId, params int[] additionalDownloadIds)
    {
        var (task, run, workset) = await Seed("download.awaitCompletion", WorkflowRunStatus.Waiting, null);
        var original = workset.Targets.Single();
        run.CurrentItemJson = WorkflowItemSnapshot.Capture(workset with
        {
            Targets = [original with {DownloadTaskId = downloadId}, .. additionalDownloadIds.Select(id =>
                original with {Id = $"destination-{id}", DownloadTaskId = id})]
        });
        await _db.SaveChangesAsync(); return (task.Id, run.Id);
    }
    [TestMethod]
    public async Task ReusingExistingFilesRequiresAnExplicitNativePartAndConfirmationOnlyChangesThatPart()
    {
        var (id, _) = await SeedDestination(701, 702);
        var provider = new DestinationCapability();
        var service = DestinationService(provider);
        await Assert.ThrowsExceptionAsync<ArgumentException>(() => service.PreviewAsync(new([id], _root, true), AllowDirectory));
        await Assert.ThrowsExceptionAsync<ArgumentException>(() => service.PreviewAsync(new([id], _root, true, [710]), AllowDirectory));
        await Assert.ThrowsExceptionAsync<ArgumentException>(() => service.PreviewAsync(new([id], _root, true, [701, 702]), AllowDirectory));
        var preview = await service.PreviewAsync(new([id], _root, true, [702]), AllowDirectory);
        var row = preview.Rows.Single();
        Assert.AreEqual("DOWNLOAD_DESTINATION_REUSE_READY", row.ReasonCode);
        Assert.AreEqual(702, row.Downloads.Single().DownloadTaskId);
        Assert.AreEqual(30L, row.Downloads.Single().ReuseEstimate!.RequiredDownloadBytes);
        var result = await service.ApplyAsync(new(preview.PreviewToken, [id]), AllowDirectory);
        Assert.AreEqual(1, result.AppliedDownloadCount);
        CollectionAssert.AreEqual(new[] {702}, provider.Applied);
    }

    [TestMethod]
    public async Task DestinationBatchFreezesIdsAndNamingAndDoesNotEditPlanHistoryOrStartDownloads()
    {
        var (first, _) = await SeedDestination(701); var (second, _) = await SeedDestination(702);
        var provider = new DestinationCapability(); provider.Blocked[702] = "DOWNLOAD_DESTINATION_HAS_OUTPUT";
        var service = DestinationService(provider);
        var before = JsonSerializer.Serialize(await _db.AcquisitionTasks.AsNoTracking().ToListAsync());
        var runs = JsonSerializer.Serialize(await _db.WorkflowRuns.AsNoTracking().ToListAsync());
        var result = await service.PreviewAsync(new([first, second], _root), AllowDirectory);
        Assert.AreEqual(2, result.TaskCount); Assert.IsTrue(result.Rows[0].CanApply); Assert.IsFalse(result.Rows[1].CanApply);
        Assert.AreEqual(0, provider.Applied.Count);
        await Assert.ThrowsExceptionAsync<ArgumentException>(() => service.ApplyAsync(new(result.PreviewToken, [second]), AllowDirectory));
        // An explicit preview pins the reviewed path; a later default change is not an implicit request.
        _services.GetRequiredService<IBOptions<AcquisitionOptions>>().Value.StoragePolicy = new() {DownloadDirectoryTemplate = "changed/{OperationId}"};
        var applied = await service.ApplyAsync(new(result.PreviewToken, [first]), AllowDirectory);
        CollectionAssert.AreEqual(new[] {first}, applied.AppliedTaskIds.ToArray());
        Assert.AreEqual(1, applied.AppliedDownloadCount);
        Assert.AreEqual(applied, await service.ApplyAsync(new(result.PreviewToken, [first]), AllowDirectory));
        CollectionAssert.AreEqual(new[] {701}, provider.Applied);
        Assert.AreEqual(before, JsonSerializer.Serialize(await _db.AcquisitionTasks.AsNoTracking().ToListAsync()));
        Assert.AreEqual(runs, JsonSerializer.Serialize(await _db.WorkflowRuns.AsNoTracking().ToListAsync()));
    }
    [TestMethod]
    public async Task DestinationBatchChecksTaskAgainInsideDownloaderMutationGate()
    {
        var (id, _) = await SeedDestination(701); var provider = new DestinationCapability(); var service = DestinationService(provider);
        var preview = await service.PreviewAsync(new([id], _root), AllowDirectory);
        provider.BeforeApply = () => _db.AcquisitionTasks.Where(t => t.Id == id).ExecuteUpdateAsync(s => s.SetProperty(t => t.DeletedAt, DateTime.Now));
        var result = await service.ApplyAsync(new(preview.PreviewToken, [id]), AllowDirectory);
        Assert.AreEqual(0, result.AppliedDownloadCount); Assert.AreEqual("ACQUISITION_TASK_NOT_FOUND", result.Skipped.Single().ReasonCode);
        Assert.AreEqual(0, provider.Applied.Count);
    }
    [TestMethod]
    public async Task DestinationBatchRejectsChangedDownloadProofAndUnauthorizedPaths()
    {
        var (id, _) = await SeedDestination(701); var provider = new DestinationCapability(); var service = DestinationService(provider);
        await Assert.ThrowsExceptionAsync<UnauthorizedAccessException>(() => service.PreviewAsync(new([id], _root),
            _ => throw new UnauthorizedAccessException("DOWNLOAD_DESTINATION_PATH_NOT_ALLOWED")));
        var preview = await service.PreviewAsync(new([id], _root), AllowDirectory);
        provider.Blocked[701] = "DOWNLOAD_DESTINATION_HAS_OUTPUT";
        var result = await service.ApplyAsync(new(preview.PreviewToken, [id]), AllowDirectory);
        Assert.AreEqual(0, provider.Applied.Count); Assert.AreEqual("DOWNLOAD_DESTINATION_HAS_OUTPUT", result.Skipped.Single().ReasonCode);
    }
    [TestMethod]
    public async Task DestinationBatchAppliesOnlyTheEligibleDownloadsShownAtPreview()
    {
        var (id, _) = await SeedDestination(701, 702);
        var provider = new DestinationCapability(); var service = DestinationService(provider);
        provider.Blocked[702] = "DOWNLOAD_DESTINATION_HAS_OUTPUT";
        var preview = await service.PreviewAsync(new([id], _root), AllowDirectory);
        var row = preview.Rows.Single();
        Assert.IsTrue(row.CanApply);
        Assert.AreEqual("DOWNLOAD_DESTINATION_ELIGIBLE_PARTS", row.ReasonCode);
        Assert.IsTrue(row.Downloads.Single(d => d.DownloadTaskId == 701).CanApply);
        Assert.IsFalse(row.Downloads.Single(d => d.DownloadTaskId == 702).CanApply);

        provider.Blocked.Clear();
        var result = await service.ApplyAsync(new(preview.PreviewToken, [id]), AllowDirectory);
        CollectionAssert.AreEqual(new[] {701}, provider.Applied);
        CollectionAssert.AreEqual(new[] {id}, result.AppliedTaskIds.ToArray());
        Assert.AreEqual(1, result.AppliedDownloadCount);
        Assert.AreEqual(0, result.Skipped.Count);
    }

    [TestMethod]
    public async Task DestinationBatchReportsPartialWritesWhenAReviewedDownloadChangesDuringApply()
    {
        var (id, _) = await SeedDestination(701, 702);
        var provider = new DestinationCapability(); var service = DestinationService(provider);
        var preview = await service.PreviewAsync(new([id], _root), AllowDirectory);
        provider.BeforeApply = () => provider.Applied.Count > 0
            ? throw new InvalidOperationException("DOWNLOAD_DESTINATION_PREVIEW_CHANGED") : Task.CompletedTask;
        var result = await service.ApplyAsync(new(preview.PreviewToken, [id]), AllowDirectory);
        Assert.AreEqual(1, result.AppliedDownloadCount); Assert.AreEqual(0, result.AppliedTaskIds.Count);
        Assert.AreEqual("DOWNLOAD_DESTINATION_PARTIALLY_APPLIED", result.Skipped.Single().ReasonCode);
    }
    [TestMethod]
    public async Task DestinationBatchProtectsActiveAndPreviouslyCompletedWorkflows()
    {
        var (id, runId) = await SeedDestination(701); var provider = new DestinationCapability(); var service = DestinationService(provider);
        await _db.WorkflowRuns.Where(r => r.Id == runId).ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, WorkflowRunStatus.Running));
        Assert.AreEqual("ACQUISITION_TASK_ACTIVE", (await service.PreviewAsync(new([id], _root), AllowDirectory)).Rows.Single().ReasonCode);
        await _db.WorkflowRuns.Where(r => r.Id == runId).ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, WorkflowRunStatus.Success));
        Assert.IsFalse((await service.PreviewAsync(new([id], _root), AllowDirectory)).Rows.Single().CanApply);
        Assert.AreEqual(0, provider.Applied.Count);
    }
}
