using System.Net;
using System.Text.Json;
using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models.Constants;
using Bakabase.InsideWorld.Business.Components.Downloader.Models.Db;
using Bakabase.Modules.Downloader.Abstractions;
using Bakabase.Modules.Downloader.Cloud;
using Bakabase.Modules.Downloader.Models;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Modules.Workflow.Components;
using Bakabase.Service.Components.Acquisition.Planning;
using Bakabase.Service.Components.CloudDownloads;
using Bakabase.Service.Components.CloudDownloads.WebProviders;
using Bakabase.Service.Components.Workflow;
using Bakabase.Tests.CloudDownloads;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Tests;

public sealed partial class AcquisitionRecoveryTests
{
    [DataTestMethod]
    [DataRow("ready", true, "RECOVERY_ORIGINAL_DOWNLOAD_REVALIDATED")]
    [DataRow("changed-id", false, "RECOVERY_MANUAL_REVIEW")]
    [DataRow("changed-options", false, "RECOVERY_DOWNLOAD_IDENTITY_CHANGED")]
    [DataRow("partial-file", false, "RECOVERY_DOWNLOAD_HAS_OUTPUT")]
    [DataRow("progress", false, "RECOVERY_DOWNLOAD_HAS_OUTPUT")]
    [DataRow("unknown-transfer", false, "RECOVERY_TRANSFER_REVIEW_REQUIRED")]
    [DataRow("access-required", false, "RECOVERY_SOURCE_STILL_BLOCKED")]
    public async Task Sharepoint_historical_network_failure_reaches_full_recovery_preview_without_mutating_original_download(
        string scenario, bool canContinue, string expectedReason)
    {
        // Preserve the shape of historical downloader messages, including private diagnostics.
        // A provider-only test cannot catch a missing code in the shared projection registry.
        const string failure = "PUBLIC_SHARE_NETWORK_ERROR";
        const string message = "An error occurred during downloading files (automatic retries after network errors: 3).\n" +
                               "System.Net.Http.HttpRequestException: PUBLIC_SHARE_NETWORK_ERROR: the network request failed (SecureConnectionError).\n" +
                               "   at SendAsync https://tenant.sharepoint.com/private?token=PRIVATE_ACCESS_TOKEN";
        CollectionAssert.AreEqual(new[] {failure}, WorkflowSourceRunProblems.Extract(message).ToArray());
        Assert.AreEqual(SourceAvailabilityStatuses.TemporarilyUnavailable, ExecutionProblemClassifier.AvailabilityStatus(failure));
        Assert.AreEqual("SOURCE_NETWORK_ERROR", ExecutionProblemClassifier.NormalizedReason(failure));
        CollectionAssert.AreEqual(new[] {"networkIssue"}, ExecutionProblemClassifier.Classify([failure]).ToArray());

        var (task, run, workset) = await Seed("download.awaitCompletion", WorkflowRunStatus.Waiting, error: null);
        var source = new CloudSource {Id = "source", ProviderId = "oneDrive", Kind = "share",
            Url = "https://tenant.sharepoint.com/:f:/g/personal/user/fixture?e=private"};
        var target = workset.Targets.Single() with {Sources = [source], AccountId = null, Problem = message};
        var intent = DownloadArtifactCoordinator.DownloadIntent(target, _root);
        var request = new CloudDownloadRequest(intent, source, null, null, Path.Combine(_root, "cloud-original"));
        var download = new DownloadTaskDbModel {Status = DownloadTaskDbModelStatus.Failed, Message = message,
            ThirdPartyId = CloudSites.Site(source.ProviderId), DownloadPath = request.OutputDirectory,
            Options = JsonSerializer.Serialize(request, JsonSerializerOptions.Web)};
        _db.DownloadTasks.Add(download); await _db.SaveChangesAsync();
        var operation = new CloudOperationDbModel {Id = intent, TargetId = target.Id, WorkflowRunId = run.Id,
            Kind = "download", State = "started", DownloadTaskId = download.Id, RequestJson = download.Options};
        _db.CloudDownloadOperations.Add(operation);
        _db.ArtifactOperationBindings.Add(new() {Id = "original-download", OwnerKind = "workflow", OwnerId = run.Id.ToString(),
            Slot = target.Id + ":download", OperationId = intent});
        workset = workset with {NoRepeatExistingDownloads = true, Targets = [target with {DownloadTaskId = download.Id}]};
        switch (scenario)
        {
            case "changed-id": workset = workset with {Targets = [workset.Targets[0] with {DownloadTaskId = download.Id + 1}]}; break;
            case "changed-options": download.Options = "{}"; break;
            case "partial-file": Directory.CreateDirectory(download.DownloadPath);
                await File.WriteAllTextAsync(Path.Combine(download.DownloadPath, "original.part"), "retained"); break;
            case "progress": download.Progress = 1; break;
            case "unknown-transfer": _db.CloudDownloadOperations.Add(new() {Id = "uncertain-transfer", Kind = "transfer",
                State = "unknown", WorkflowRunId = run.Id, TargetId = target.Id}); break;
        }
        run.CurrentItemJson = WorkflowItemSnapshot.Capture(workset);
        run.WaitReason = "cloud.cloudAttention";
        await _db.SaveChangesAsync();
        var beforeTask = JsonSerializer.Serialize(await _db.AcquisitionTasks.AsNoTracking().SingleAsync());
        var beforeRun = JsonSerializer.Serialize(await _db.WorkflowRuns.AsNoTracking().SingleAsync());
        var beforeDownload = JsonSerializer.Serialize(await _db.DownloadTasks.AsNoTracking().SingleAsync());
        var beforeOperation = JsonSerializer.Serialize(await _db.CloudDownloadOperations.AsNoTracking().SingleAsync(o => o.Id == intent));
        var filesBefore = Directory.EnumerateFileSystemEntries(_root, "*", SearchOption.AllDirectories).Order().ToArray();

        var calls = new List<Uri>();
        using var handler = new PublicShareResolverTests.Reply(http =>
        {
            Assert.AreEqual(HttpMethod.Get, http.Method);
            var uri = http.RequestUri!; calls.Add(uri);
            Assert.AreEqual("tenant.sharepoint.com", uri.IdnHost);
            Assert.IsFalse(uri.AbsolutePath.StartsWith("/download/"), "Preview must not request file payloads.");
            if (scenario == "access-required") return new(HttpStatusCode.Forbidden);
            if (uri.AbsolutePath.StartsWith("/:f:")) return new(HttpStatusCode.OK)
            {
                RequestMessage = new(HttpMethod.Get, "https://tenant.sharepoint.com/personal/user/Documents/Forms/AllItems.aspx?id=%2Fpersonal%2Fuser%2FDocuments%2FShared"),
                Content = new StringContent("<script>const context=" + JsonSerializer.Serialize(new Dictionary<string, string>
                {
                    [".driveUrl"] = "https://tenant.sharepoint.com/personal/user/_api/v2.0/drives/drive",
                    [".driveAccessToken"] = "tempauth=private-metadata-token", ["listUrl"] = "/personal/user/Documents"
                }) + ";</script>")
            };
            if (uri.AbsolutePath.Contains("/root:")) return PublicShareResolverTests.Json(new {id = "root", folder = new {childCount = 1}});
            Assert.IsTrue(uri.AbsolutePath.EndsWith("/items/root/children"));
            return PublicShareResolverTests.Json(new {value = new[] {new Dictionary<string, object>
            {
                ["id"] = "original", ["name"] = "original.bin", ["size"] = 3, ["eTag"] = "version-1", ["file"] = new {},
                ["@content.downloadUrlNoAuth"] = "https://tenant.sharepoint.com/download/original"
            }}});
        });
        var provider = new SharePointCloudProvider(Path.Combine(_root, "app"), new PreviewMustNotDownload(),
            new PublicShareResolverTests.Clients(handler));
        var recovery = new CloudDownloadRecoveryService(_db, [provider], new UserStoragePolicy(false));
        _diagnostics = new(_db, new UserStoragePolicy(false), _services.GetRequiredService<IArtifactOperationStore>(), downloads: recovery);
        var host = new RecordingHost();
        var service = new AcquisitionRecoveryService(_db, _diagnostics, new(), host);
        var preview = await service.PreviewAsync(new([task.Id]));
        var row = preview.Rows.Single();
        Assert.AreEqual(canContinue, row.CanContinue); Assert.AreEqual(expectedReason, row.ReasonCode);
        Assert.AreEqual(canContinue ? 1 : 0, preview.ContinuableCount);
        Assert.AreEqual(scenario == "ready" ? 3 : scenario == "access-required" ? 1 : 0, calls.Count);
        if (canContinue)
        {
            Assert.AreEqual(failure, row.CurrentProblemCode);
            Assert.AreEqual("resume", row.Action); Assert.AreEqual(run.Id, row.WorkflowRunId);
            var diagnosis = await _diagnostics.DiagnoseAsync(task, run, default);
            var signal = JsonSerializer.Deserialize<CloudSelectionSignal>(diagnosis.SignalJson, JsonSerializerOptions.Web)!;
            CollectionAssert.AreEqual(new[] {target.Id}, signal.ResumeTargetIds!);
            Assert.IsTrue(signal.ObserveExistingOnly == true && signal.RevalidateExistingDownloads == true);
        }
        Assert.IsFalse(JsonSerializer.Serialize(preview).Contains("PRIVATE_ACCESS_TOKEN"));
        Assert.AreEqual(0, host.Calls);
        Assert.AreEqual(beforeTask, JsonSerializer.Serialize(await _db.AcquisitionTasks.AsNoTracking().SingleAsync()));
        Assert.AreEqual(beforeRun, JsonSerializer.Serialize(await _db.WorkflowRuns.AsNoTracking().SingleAsync()));
        Assert.AreEqual(beforeDownload, JsonSerializer.Serialize(await _db.DownloadTasks.AsNoTracking().SingleAsync()));
        Assert.AreEqual(beforeOperation, JsonSerializer.Serialize(await _db.CloudDownloadOperations.AsNoTracking().SingleAsync(o => o.Id == intent)));
        CollectionAssert.AreEqual(filesBefore, Directory.EnumerateFileSystemEntries(_root, "*", SearchOption.AllDirectories).Order().ToArray());
        Assert.AreEqual(0, await _db.DownloadResults.CountAsync());
    }

    private sealed class PreviewMustNotDownload : IHttpDownloader
    {
        public Task<string> DownloadAsync(HttpDownloadRequest request, Func<int, string?, Task>? progress, CancellationToken cancellationToken) =>
            throw new AssertFailedException("Recovery preview must not download payloads.");
    }
}
