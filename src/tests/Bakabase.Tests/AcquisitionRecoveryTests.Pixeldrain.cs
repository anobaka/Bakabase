using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.InsideWorld.Business.Components.Downloader.Services;
using Bakabase.Modules.Downloader.Cloud;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Service.Components.Acquisition.Planning;
using Bakabase.Service.Components.CloudDownloads;
using Bakabase.Service.Components.CloudDownloads.WebProviders;
using Bakabase.Service.Components.SourceAvailability.Probes;
using Bakabase.Tests.CloudDownloads;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bakabase.Tests;

public sealed partial class AcquisitionRecoveryTests
{
    // The dependency is already installed in this fixture. Both the real provider's
    // private-journal guard and the actual public HTTP probe are used.
    private sealed class InstalledPixeldrain(PixeldrainCloudProvider provider) : ICloudDownloadProvider, ICloudDownloadRecoveryProvider
    {
        public string ProviderId => provider.ProviderId;
        public bool CanRevalidateDownloadFailure(string code) => provider.CanRevalidateDownloadFailure(code);
        public Task<CloudReadiness> CheckReadyAsync(string? accountId, CancellationToken ct) => Task.FromResult(new CloudReadiness(true));
        public Task<CloudReadiness> CheckLocalRecoveryAsync(CloudDownloadRequest request, CancellationToken ct) =>
            provider.CheckLocalRecoveryAsync(request, ct);
        public Task<CloudReadiness> RevalidateDownloadAsync(CloudDownloadRequest request, CancellationToken ct) =>
            provider.RevalidateDownloadAsync(request, ct);
        public Task<CloudDownloadResult> DownloadAsync(CloudDownloadRequest request, Func<CloudDownloadProgress, Task> progress, CancellationToken ct) =>
            throw new AssertFailedException("Recovery must use the existing native download ID.");
    }

    private static CloudSource PixeldrainList() => new()
    {
        Id = "source", ProviderId = "pixeldrain", MethodId = "pixeldrain.galleryDl",
        Kind = "share", Url = "https://pixeldrain.com/l/fixture"
    };

    private (CloudDownloadRecoveryService Recovery, List<Uri> Reads) PixeldrainRecovery(Func<HttpResponseMessage> metadata)
    {
        var reads = new List<Uri>();
        var handler = new PublicShareResolverTests.Reply(request =>
        {
            Assert.AreEqual(HttpMethod.Get, request.Method);
            Assert.AreEqual("https://pixeldrain.com/api/list/fixture", request.RequestUri!.AbsoluteUri);
            reads.Add(request.RequestUri);
            return metadata();
        });
        var provider = new PixeldrainCloudProvider(null!, null!, new(new PublicShareResolverTests.Clients(handler)), () => _root);
        var storage = new UserStoragePolicy(false);
        var recovery = new CloudDownloadRecoveryService(_db, [new InstalledPixeldrain(provider)], storage);
        _diagnostics = new(_db, storage, _services.GetRequiredService<IArtifactOperationStore>(), downloads: recovery);
        return (recovery, reads);
    }

    private static HttpResponseMessage PixeldrainMetadata(bool available = true) => PublicShareResolverTests.Json(
        new {success = true, id = "fixture", file_count = 2, files = new[]
        {
            new {id = "one", size = 8, can_download = true},
            new {id = "two", size = 12, can_download = available}
        }});

    [TestMethod]
    public async Task Pixeldrain_recovery_starts_original_retargeted_id_and_registers_files_to_original_workflow()
    {
        var (task, run, workset, download, operation, _, _) = await SeedRecoverableProvider(PixeldrainList(), "PIXELDRAIN_NETWORK_ERROR");
        var initial = JsonSerializer.Deserialize<CloudDownloadRequest>(download.Options!, JsonSerializerOptions.Web)!;
        var currentRoot = Directory.CreateDirectory(Path.Combine(_root, "current")).FullName;
        var newPath = Path.Combine(currentRoot, "original-task");
        var revised = initial with {DestinationRevisions = [new(Guid.NewGuid().ToString("N"), initial.OutputDirectory, newPath, currentRoot,
            DateTime.UtcNow, new string('A', 64))]};
        operation.RequestJson = download.Options = JsonSerializer.Serialize(revised, JsonSerializerOptions.Web);
        download.DownloadPath = newPath;
        await _db.SaveChangesAsync();

        var (recovery, reads) = PixeldrainRecovery(() => PixeldrainMetadata());
        var results = _services.GetRequiredService<DownloadResultService>();
        var file = Path.Combine(newPath, "original.bin");
        var starter = new OnlyDownloadStarter(this, run.Id)
        {
            AfterStart = async () =>
            {
                Directory.CreateDirectory(newPath);
                await File.WriteAllTextAsync(file, "original bytes");
                await results.RecordFilesAsync(download.Id, download.ThirdPartyId, "fixture-receipt", "fixture", newPath, [file], null);
            }
        };
        var service = new AcquisitionRecoveryService(_db, _diagnostics, new(), new RecordingHost(), downloadOnly: OnlyDownloads(recovery, starter, new()));
        var preview = await service.PreviewAsync(new([task.Id], "downloads"));
        Assert.AreEqual(1, preview.ContinuableCount, JsonSerializer.Serialize(preview.Rows));
        var result = await service.ApplyAsync(new(preview.PreviewToken, [task.Id]));
        CollectionAssert.AreEqual(new[]{download.Id}, result.Downloads!.Single().StartedDownloadTaskIds.ToArray());
        await service.ApplyAsync(new(preview.PreviewToken, [task.Id]));
        CollectionAssert.AreEqual(new[]{download.Id}, starter.Calls);
        Assert.IsTrue(reads.Count >= 2);
        Assert.IsFalse(Directory.Exists(initial.OutputDirectory));

        var store = _services.GetRequiredService<IArtifactOperationStore>();
        var coordinator = new DownloadArtifactCoordinator(_db, store, new UserStoragePolicy(false), results);
        using var services = new ServiceCollection().AddSingleton(_db).AddSingleton(_services.GetRequiredService<DownloadTaskService>())
            .AddSingleton(coordinator).BuildServiceProvider();
        var context = new WorkflowExecutionContext {RunId = run.Id, WorkflowDefinitionId = 0, TriggerKind = CloudWorkflow.Trigger,
            Payload = workset, Services = services, ActivityConfigJson = "{}", Logger = NullLogger.Instance};
        var signal = new CloudSelectionSignal {ObserveExistingOnly = true, ResumeTargetIds = [workset.Targets.Single().Id]};
        var outcome = await new AwaitCloudDownloadsActivity().ResumeAsync(context, workset, JsonSerializer.Serialize(signal, JsonSerializerOptions.Web), default);
        Assert.IsNull(outcome.Suspension, JsonSerializer.Serialize(outcome.Suspension?.Item));
        var finished = ((DownloadWorkset)outcome.Replacement!).Targets.Single();
        Assert.AreEqual(download.Id, finished.DownloadTaskId);
        Assert.IsFalse(string.IsNullOrWhiteSpace(finished.DownloadArtifactId));
        CollectionAssert.AreEqual(new[]{file}, finished.Files);
        Assert.IsTrue(await _db.ArtifactOperationBindings.AnyAsync(b => b.OwnerId == run.Id.ToString() && b.Slot == finished.Id + ":artifact.download" &&
            b.OperationId == operation.Id));
        Assert.AreEqual(1, await _db.CloudDownloadOperations.CountAsync());
        Assert.AreEqual(1, await _db.DownloadTasks.CountAsync());
    }

    [TestMethod]
    [DataRow("part")]
    [DataRow("progress")]
    [DataRow("private-state")]
    [DataRow("foreign-binding")]
    [DataRow("foreign-operation")]
    [DataRow("not-network")]
    [DataRow("password")]
    [DataRow("dangling-link")]
    public async Task Pixeldrain_recovery_protects_partial_or_uncertain_originals_without_probing(string mode)
    {
        var source = PixeldrainList();
        if (mode == "password") source = source with {AccessCode = "code"};
        var (_, run, workset, download, operation, _, _) = await SeedRecoverableProvider(source,
            mode == "not-network" ? "PIXELDRAIN_ACCESS_REQUIRED" : "CLOUD_NETWORK_ERROR");
        switch (mode)
        {
            case "part":
                Directory.CreateDirectory(download.DownloadPath);
                await File.WriteAllTextAsync(Path.Combine(download.DownloadPath, ".original.part"), "partial data"); break;
            case "progress": download.Progress = 2; break;
            case "private-state":
                var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(operation.Id)));
                var journal = Directory.CreateDirectory(Path.Combine(_root, "cloud-downloads", "web", "pixeldrain", key));
                await File.WriteAllTextAsync(Path.Combine(journal.FullName, "manifest.json"), "{}"); break;
            case "foreign-binding": _db.ArtifactOperationBindings.Add(new() {Id = "second-binding", OwnerKind = "workflow",
                OwnerId = (run.Id + 1).ToString(), Slot = "other:download", OperationId = operation.Id}); break;
            case "foreign-operation": operation.TargetId = "another-target"; break;
            case "dangling-link":
                if (OperatingSystem.IsWindows()) Assert.Inconclusive("Creating symbolic links can require elevated permissions on Windows.");
                Directory.CreateSymbolicLink(download.DownloadPath, Path.Combine(_root, "unmounted"));
                break;
        }
        await _db.SaveChangesAsync();
        var (recovery, reads) = PixeldrainRecovery(() => PixeldrainMetadata());
        var check = await recovery.CheckAsync(run.Id, workset.Targets.Single(), _root, true, default);
        Assert.IsFalse(check.CanRetry); Assert.IsFalse(check.Candidate); Assert.AreEqual(0, reads.Count);
        Assert.AreEqual(1, await _db.DownloadTasks.CountAsync());
    }

    [TestMethod]
    public async Task Pixeldrain_recovery_requires_every_selected_file_and_rechecks_at_execution()
    {
        var (task, run, workset, _, _, _, _) = await SeedRecoverableProvider(PixeldrainList(), "PIXELDRAIN_NETWORK_ERROR");
        var available = false;
        var (recovery, reads) = PixeldrainRecovery(() => PixeldrainMetadata(available));
        var blocked = await recovery.CheckAsync(run.Id, workset.Targets.Single(), _root, true, default);
        Assert.IsFalse(blocked.CanRetry);
        Assert.AreEqual("SOURCE_ACCESS_REQUIRED", blocked.FailureCode);
        available = true;
        var host = new RecordingHost {BeforeValidation = () => {available = false; return Task.CompletedTask;}};
        var service = new AcquisitionRecoveryService(_db, _diagnostics, new(), host);
        var preview = await service.PreviewAsync(new([task.Id], "workflow")); Assert.AreEqual(1, preview.ContinuableCount);
        var result = await service.ApplyAsync(new(preview.PreviewToken, [task.Id]));
        Assert.AreEqual(0, host.Calls); Assert.AreEqual("RECOVERY_SOURCE_STILL_BLOCKED", result.Skipped.Single().ReasonCode);
        Assert.IsTrue(reads.Count >= 3); Assert.AreEqual(1, await _db.DownloadTasks.CountAsync());
    }
}
