using System.Reflection;
using System.Text.Json;
using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.Abstractions.Exceptions;
using Bakabase.Modules.Bittorrent.Abstractions;
using Bakabase.Modules.Bittorrent.Models;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models.Constants;
using Bakabase.InsideWorld.Business.Components.Downloader.Models.Db;
using Bakabase.InsideWorld.Business.Components.Downloader.Services;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Service.Components.CloudDownloads;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bakabase.Tests;

public sealed partial class CloudWorkflowTests
{
    private sealed class ResumeStoragePolicy : IUserStoragePolicy
    {
        private readonly UserStoragePolicy _storage = new(isContainer: false);
        public string? BlockedPath { get; set; }
        public UserStoragePathException? Failure { get; set; }
        public bool IsRestricted => _storage.IsRestricted;
        public IReadOnlyList<UserStorageRoot> GetRoots(UserStoragePurpose purpose) => _storage.GetRoots(purpose);
        public bool IsPathAllowed(string path, UserStoragePurpose purpose) => path != BlockedPath && _storage.IsPathAllowed(path, purpose);
        public void EnsurePathAllowed(string path, UserStoragePurpose purpose)
        {
            if (path == BlockedPath && Failure != null) throw Failure;
            _storage.EnsurePathAllowed(path, purpose);
        }
        public void EnsureTreeMutationAllowed(string path) => _storage.EnsureTreeMutationAllowed(path);
    }

    [TestMethod]
    public async Task Selected_resume_admits_all_originals_before_waiting_and_keeps_unselected_target()
    {
        var first = Chosen(Source("selected-first", "googleDrive"));
        var second = Chosen(Source("selected-second", "gofile"));
        var untouched = Chosen(Source("unselected", "gofile")) with {Problem = "Unselected failure"};
        var workset = Workset(first.SelectedSource!, second.SelectedSource!, untouched.SelectedSource!);
        first = first with {DownloadTaskId = await Operations.EnqueueAsync(7201, first, workset.OutputDirectory, default)};
        second = second with {DownloadTaskId = await Operations.EnqueueAsync(7201, second, workset.OutputDirectory, default)};
        untouched = untouched with {DownloadTaskId = await Operations.EnqueueAsync(7201, untouched, workset.OutputDirectory, default)};
        var selectedIds = new[] {first.DownloadTaskId!.Value, second.DownloadTaskId!.Value};
        var service = _services.GetRequiredService<DownloadTaskService>();
        await service.Stop();
        await Db.DownloadTasks.Where(t => t.Id == untouched.DownloadTaskId).ExecuteUpdateAsync(update => update
            .SetProperty(t => t.Status, DownloadTaskDbModelStatus.Failed).SetProperty(t => t.Message, untouched.Problem));

        var rows = await Db.DownloadTasks.AsNoTracking().Where(t => selectedIds.Contains(t.Id)).OrderBy(t => t.Id).ToArrayAsync();
        var resultFiles = new List<string>();
        foreach (var row in rows)
        {
            Directory.CreateDirectory(row.DownloadPath);
            var file = Path.Combine(row.DownloadPath, "result.zip");
            await File.WriteAllTextAsync(file, "fixture download " + row.Id);
            resultFiles.Add(file);
        }
        workset = workset with {Targets = [first, second, untouched]};
        var observedWaiting = false;
        var context = new WorkflowExecutionContext
        {
            RunId = 7201, WorkflowDefinitionId = 0, TriggerKind = CloudWorkflow.Trigger, Payload = workset,
            ActivityConfigJson = "{}", Services = _services, Logger = NullLogger.Instance,
            ReportStage = async _ =>
            {
                var downloading = await Db.DownloadTasks.AsNoTracking().Where(t => t.Status == DownloadTaskDbModelStatus.InProgress)
                    .Select(t => t.Id).ToArrayAsync();
                CollectionAssert.AreEquivalent(selectedIds, downloading);
                observedWaiting = true;
                await Db.DownloadTasks.Where(t => selectedIds.Contains(t.Id))
                    .ExecuteUpdateAsync(update => update.SetProperty(t => t.Status, DownloadTaskDbModelStatus.Complete));
                for (var index = 0; index < rows.Length; index++)
                {
                    var row = rows[index];
                    Db.DownloadResults.Add(new DownloadResultDbModel
                    {
                        DownloadTaskId = row.Id, ThirdPartyId = row.ThirdPartyId, SourceKey = row.Key,
                        Name = "Fixture", Kind = DownloadResultKind.LocalFiles, Path = row.DownloadPath,
                        DownloadDirectory = row.DownloadPath, DeduplicationKey = "resume-result-" + row.Id,
                        FilesJson = JsonSerializer.Serialize(new[] {resultFiles[index]})
                    });
                }
                await Db.SaveChangesAsync();
            }
        };
        var signal = JsonSerializer.Serialize(new CloudSelectionSignal
            {ObserveExistingOnly = true, ResumeTargetIds = [first.Id, second.Id]}, JsonSerializerOptions.Web);

        var outcome = await new AwaitCloudDownloadsActivity().ResumeAsync(context, workset, signal, default);

        Assert.IsTrue(observedWaiting);
        Assert.IsNotNull(outcome.Suspension);
        var saved = Item(outcome);
        Assert.AreSame(untouched, saved.Targets[2]);
        Assert.IsNull(saved.Targets[0].Problem);
        Assert.IsNull(saved.Targets[1].Problem);
        CollectionAssert.AreEquivalent(resultFiles, saved.Targets.Take(2).SelectMany(t => t.Files).ToArray());
        var unselected = await Db.DownloadTasks.AsNoTracking().SingleAsync(t => t.Id == untouched.DownloadTaskId);
        Assert.AreEqual(DownloadTaskDbModelStatus.Failed, unselected.Status);
        Assert.AreEqual(untouched.Problem, unselected.Message);
        Assert.AreEqual(3, await Db.CloudDownloadOperations.CountAsync());
        Assert.AreEqual(3, await Db.DownloadTasks.CountAsync());
    }

    [TestMethod]
    public async Task Selected_resume_continues_only_the_paused_download_and_preserves_the_failed_sibling()
    {
        var failed = Chosen(Source("old-failed", "googleDrive")) with {Problem = "Existing provider error; do not retry."};
        var paused = Chosen(Source("deployment-paused", "gofile")) with {Problem = "Paused for deployment."};
        var workset = Workset(failed.SelectedSource!, paused.SelectedSource!);
        failed = failed with {DownloadTaskId = await Operations.EnqueueAsync(7201, failed, workset.OutputDirectory, default)};
        paused = paused with {DownloadTaskId = await Operations.EnqueueAsync(7201, paused, workset.OutputDirectory, default)};
        var service = _services.GetRequiredService<DownloadTaskService>();
        await service.Stop(t => t.Id == failed.DownloadTaskId || t.Id == paused.DownloadTaskId);
        await Db.DownloadTasks.Where(t => t.Id == failed.DownloadTaskId).ExecuteUpdateAsync(update => update
            .SetProperty(t => t.Status, DownloadTaskDbModelStatus.Failed).SetProperty(t => t.Message, failed.Problem));
        var pausedRow = await Db.DownloadTasks.AsNoTracking().SingleAsync(t => t.Id == paused.DownloadTaskId);
        Directory.CreateDirectory(pausedRow.DownloadPath);
        var file = Path.Combine(pausedRow.DownloadPath, "retained-result.zip");
        await File.WriteAllTextAsync(file, "fixture download result");
        var sawSelectedStart = false;
        workset = workset with {Targets = [failed, paused]};
        var context = new WorkflowExecutionContext
        {
            RunId = 7201, WorkflowDefinitionId = 0, TriggerKind = CloudWorkflow.Trigger, Payload = workset,
            ActivityConfigJson = "{}", Services = _services, Logger = NullLogger.Instance,
            ReportStage = async _ =>
            {
                Assert.AreEqual(DownloadTaskDbModelStatus.Failed, (await Db.DownloadTasks.AsNoTracking().SingleAsync(t => t.Id == failed.DownloadTaskId)).Status);
                Assert.AreEqual(DownloadTaskDbModelStatus.InProgress, (await Db.DownloadTasks.AsNoTracking().SingleAsync(t => t.Id == paused.DownloadTaskId)).Status);
                sawSelectedStart = true;
                await Db.DownloadTasks.Where(t => t.Id == paused.DownloadTaskId).ExecuteUpdateAsync(update => update
                    .SetProperty(t => t.Status, DownloadTaskDbModelStatus.Complete));
                Db.DownloadResults.Add(new DownloadResultDbModel
                {
                    DownloadTaskId = pausedRow.Id, ThirdPartyId = ThirdPartyId.Gofile, SourceKey = pausedRow.Key,
                    Name = "Fixture", Kind = DownloadResultKind.LocalFiles, Path = pausedRow.DownloadPath,
                    DownloadDirectory = pausedRow.DownloadPath, DeduplicationKey = "selected-resume-result", FilesJson = JsonSerializer.Serialize(new[] {file})
                });
                await Db.SaveChangesAsync();
            }
        };
        var signal = JsonSerializer.Serialize(new CloudSelectionSignal {ObserveExistingOnly = true, ResumeTargetIds = [paused.Id]}, JsonSerializerOptions.Web);
        var outcome = await new AwaitCloudDownloadsActivity().ResumeAsync(context, workset, signal, default);
        Assert.IsTrue(sawSelectedStart);
        Assert.IsNotNull(outcome.Suspension, "The failed sibling must continue to hold the node for review.");
        var saved = Item(outcome);
        Assert.AreSame(failed, saved.Targets[0]);
        Assert.AreEqual(failed.Problem, saved.Targets[0].Problem);
        Assert.IsNull(saved.Targets[1].Problem);
        CollectionAssert.AreEqual(new[] {file}, saved.Targets[1].Files);
        Assert.AreEqual(paused.DownloadTaskId, saved.Targets[1].DownloadTaskId);
        Assert.AreEqual(2, await Db.DownloadTasks.CountAsync());
        Assert.AreEqual(2, await Db.CloudDownloadOperations.CountAsync());
        Assert.AreEqual(0, _baidu.TransferRequests.Count);
        Assert.AreEqual(0, _google.DownloadCalls + _gofile.DownloadCalls);

        // Repeating the selected resume only reuses the completed result and existing identity.
        var replay = await new AwaitCloudDownloadsActivity().ResumeAsync(Context(saved), saved, signal, default);
        Assert.IsNotNull(replay.Suspension);
        Assert.AreSame(failed, Item(replay).Targets[0]);
        Assert.AreEqual(saved.Targets[1].DownloadArtifactId, Item(replay).Targets[1].DownloadArtifactId);
        Assert.AreEqual(DownloadTaskDbModelStatus.Failed, (await Db.DownloadTasks.AsNoTracking().SingleAsync(t => t.Id == failed.DownloadTaskId)).Status);
        Assert.AreEqual(2, await Db.DownloadTasks.CountAsync());
        Assert.AreEqual("fixture download result", await File.ReadAllTextAsync(file));
    }

    [TestMethod]
    public async Task Manual_selection_shows_original_errors_and_does_not_restart_completed_or_rebound_tasks()
    {
        var first = Chosen(Source("first", "googleDrive"));
        var second = Chosen(Source("second", "gofile"));
        var completed = Chosen(Source("third", "gofile"));
        var workset = Workset(first.SelectedSource!, second.SelectedSource!, completed.SelectedSource!);
        first = first with {DownloadTaskId = await Operations.EnqueueAsync(7201, first, workset.OutputDirectory, default)};
        second = second with {DownloadTaskId = await Operations.EnqueueAsync(7201, second, workset.OutputDirectory, default)};
        completed = completed with {DownloadTaskId = await Operations.EnqueueAsync(7201, completed, workset.OutputDirectory, default)};
        await Db.DownloadTasks.Where(t => t.Id == first.DownloadTaskId || t.Id == second.DownloadTaskId)
            .ExecuteUpdateAsync(u => u.SetProperty(t => t.Status, DownloadTaskDbModelStatus.Failed)
                .SetProperty(t => t.Message, "CLOUD_DOWNLOAD_CHECKSUM_MISMATCH"));
        await Db.DownloadTasks.Where(t => t.Id == completed.DownloadTaskId)
            .ExecuteUpdateAsync(u => u.SetProperty(t => t.Status, DownloadTaskDbModelStatus.Complete));
        workset = workset with {Targets = [first, second, completed]};
        var reader = new CloudManualDownloadResume(Db);
        var preview = await reader.ReadMultipleAsync(7201, workset, default);
        CollectionAssert.AreEqual(new[] {true, true, false}, preview!.Select(t => t.CanRetry).ToArray());
        Assert.AreEqual($"site:{first.DownloadTaskId}", preview[0].DownloadId);
        Assert.AreEqual("CLOUD_DOWNLOAD_CHECKSUM_MISMATCH", preview[0].Problem);
        Assert.AreEqual("completed", preview[2].Status);
        Assert.IsNull(await reader.ReadMultipleAsync(7201, workset with {Targets = [first]}, default));
        var mixed = await reader.ReadMultipleAsync(7201, workset with {Targets = [first, second with {DownloadTaskId = null}]}, default);
        Assert.AreEqual(first.Id, mixed!.Single().TargetId);
        Assert.IsTrue(mixed.Single().CanRetry);
        Assert.IsFalse((await reader.ReadAsync(7202, first, workset.OutputDirectory, default)).CanRetry);
        Assert.IsFalse((await reader.ReadAsync(7201, first with {AccountId = "different-account"}, workset.OutputDirectory, default)).CanRetry);

        var withoutSelection = await new AwaitCloudDownloadsActivity().ResumeAsync(Context(workset), workset, "{}", default);
        Assert.IsNotNull(withoutSelection.Suspension);
        await Db.DownloadTasks.Where(t => t.Id == first.DownloadTaskId).ExecuteUpdateAsync(u => u.SetProperty(t => t.Options, "{}"));
        var refused = await new AwaitCloudDownloadsActivity().ResumeAsync(Context(workset), workset,
            JsonSerializer.Serialize(new CloudSelectionSignal {ObserveExistingOnly = true, ResumeTargetIds = [first.Id]}, JsonSerializerOptions.Web), default);
        Assert.AreEqual("RECOVERY_DOWNLOAD_IDENTITY_CHANGED", Item(refused).Targets[0].Problem);
        Assert.AreSame(second, Item(refused).Targets[1]);
        Assert.AreSame(completed, Item(refused).Targets[2]);
        Assert.AreEqual(2, await Db.DownloadTasks.CountAsync(t => t.Status == DownloadTaskDbModelStatus.Failed));
        Assert.AreEqual(1, await Db.DownloadTasks.CountAsync(t => t.Status == DownloadTaskDbModelStatus.Complete));
        Assert.AreEqual(3, await Db.CloudDownloadOperations.CountAsync());
    }

    [DataTestMethod]
    [DataRow(true, "LOCAL_STORAGE_UNAVAILABLE")]
    [DataRow(false, "SOURCE_ATTENTION_REQUIRED")]
    public async Task Selected_resume_reports_detached_mounts_and_keeps_other_path_errors(bool unavailable, string code)
    {
        var target = Chosen(Source("guarded-destination", "gofile"));
        var workset = Workset(target.SelectedSource!);
        var id = await Operations.EnqueueAsync(7201, target, workset.OutputDirectory, default);
        target = target with {DownloadTaskId = id};
        workset = workset with {Targets = [target]};
        var service = _services.GetRequiredService<DownloadTaskService>();
        await service.Stop(t => t.Id == id);
        var row = await Db.DownloadTasks.AsNoTracking().SingleAsync(t => t.Id == id);
        var message = unavailable ? "Reconnect the mounted storage location. /fixture/mount" : "Choose a folder inside a mounted storage location.";
        _resumeStorage.BlockedPath = row.DownloadPath;
        _resumeStorage.Failure = new UserStoragePathException(message, mountUnavailable: unavailable);

        var directStart = await service.Start(t => t.Id == id, targeted: true);
        Assert.AreNotEqual(0, directStart.Code);
        Assert.AreEqual(message, directStart.Message);

        var signal = JsonSerializer.Serialize(new CloudSelectionSignal {ObserveExistingOnly = true, ResumeTargetIds = [target.Id]}, JsonSerializerOptions.Web);
        var outcome = await new AwaitCloudDownloadsActivity().ResumeAsync(Context(workset), workset, signal, default);

        Assert.IsNotNull(outcome.Suspension);
        var saved = Item(outcome);
        Assert.AreEqual(unavailable ? code : message, saved.Targets.Single().Problem);
        Assert.AreEqual(code, saved.GetWorkflowOutputMetadata("download.awaitCompletion").Single(b => b.Kind == "warning").Code);
        Assert.AreEqual(DownloadTaskDbModelStatus.Disabled, (await Db.DownloadTasks.AsNoTracking().SingleAsync(t => t.Id == id)).Status);
        Assert.AreEqual(1, await Db.CloudDownloadOperations.CountAsync());
        Assert.AreEqual(0, _gofile.DownloadCalls);
    }

    [DataTestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task Awaiting_completed_download_pauses_only_for_a_detached_mount(bool mountUnavailable)
    {
        var target = Chosen(Source("completed-on-mount", "gofile"));
        var workset = Workset(target.SelectedSource!);
        var id = await Operations.EnqueueAsync(7201, target, workset.OutputDirectory, default);
        target = target with {DownloadTaskId = id};
        workset = workset with {Targets = [target]};
        await _services.GetRequiredService<DownloadTaskService>().Stop(t => t.Id == id);
        await Db.DownloadTasks.Where(t => t.Id == id).ExecuteUpdateAsync(u =>
            u.SetProperty(t => t.Status, DownloadTaskDbModelStatus.Complete));
        var row = await Db.DownloadTasks.AsNoTracking().SingleAsync(t => t.Id == id);
        Directory.CreateDirectory(row.DownloadPath);
        var file = Path.Combine(row.DownloadPath, "retained.zip");
        await File.WriteAllTextAsync(file, "saved content");
        Db.DownloadResults.Add(new DownloadResultDbModel
        {
            DownloadTaskId = id, ThirdPartyId = ThirdPartyId.Gofile, SourceKey = row.Key,
            Name = "Fixture", Kind = DownloadResultKind.LocalFiles, Path = row.DownloadPath,
            DownloadDirectory = row.DownloadPath, DeduplicationKey = "completed-on-mount-result",
            FilesJson = JsonSerializer.Serialize(new[] {file})
        });
        await Db.SaveChangesAsync();

        _resumeStorage.BlockedPath = workset.OutputDirectory;
        _resumeStorage.Failure = new UserStoragePathException(mountUnavailable
            ? "Reconnect the mounted storage location. /fixture/mount"
            : "Choose a folder inside a mounted storage location.", mountUnavailable);
        const string observe = "{\"observeExistingOnly\":true}";
        var activity = new AwaitCloudDownloadsActivity();
        if (mountUnavailable)
        {
            var waiting = await activity.ResumeAsync(Context(workset), workset, observe, default);
            Assert.AreEqual("cloud.cloudAttention", waiting.Suspension?.Reason);
            var saved = Item(waiting);
            Assert.AreEqual(workset.OutputDirectory, saved.OutputDirectory);
            Assert.IsTrue(saved.NoRepeatExistingDownloads == true);
            Assert.AreEqual(JsonSerializer.Serialize(target with {Problem = "LOCAL_STORAGE_UNAVAILABLE"}, JsonSerializerOptions.Web),
                JsonSerializer.Serialize(saved.Targets.Single(), JsonSerializerOptions.Web));

            _resumeStorage.BlockedPath = null;
            _resumeStorage.Failure = null;
            var resumed = await activity.ResumeAsync(Context(saved), saved, observe, default);
            Assert.IsNull(resumed.Suspension);
            Assert.AreEqual(id, Item(resumed).Targets.Single().DownloadTaskId);
            CollectionAssert.AreEqual(new[] {file}, Item(resumed).Targets.Single().Files);
        }
        else
        {
            var denied = await Assert.ThrowsExactlyAsync<UserStoragePathException>(() =>
                activity.ResumeAsync(Context(workset), workset, observe, default));
            Assert.IsFalse(denied.MountUnavailable);
        }

        Assert.AreEqual(DownloadTaskDbModelStatus.Complete, (await Db.DownloadTasks.AsNoTracking().SingleAsync()).Status);
        Assert.AreEqual(1, await Db.DownloadResults.CountAsync());
        Assert.AreEqual(1, await Db.CloudDownloadOperations.CountAsync());
        Assert.AreEqual(0, _gofile.DownloadCalls);
    }

    [TestMethod]
    public async Task Manual_transport_selection_requires_the_original_consumer_without_starting_the_manager()
    {
        var target = Chosen(Source("split", "magnet")) with {TransportTaskId = "bt:original"};
        var workset = Workset(target.SelectedSource!);
        var intent = DownloadArtifactCoordinator.DownloadIntent(target, workset.OutputDirectory);
        var bt = DispatchProxy.Create<IBtDownloadManager, AcquisitionTaskDeletionTests.SnapshotProxy>();
        var snapshot = (AcquisitionTaskDeletionTests.SnapshotProxy)(object)bt;
        snapshot.Bt = [new BtDownloadJob {Id = "original", Name = "existing", OutputDirectory = _root,
            Status = BtDownloadStatus.Stopped, SourceKind = "workflow", SourceId = intent}];
        var reader = new CloudManualDownloadResume(Db, bt);
        Assert.IsFalse((await reader.ReadAsync(7201, target, workset.OutputDirectory, default)).CanRetry);
        Db.Set<ArtifactConsumerDbModel>().Add(new() {Id = "original", ConsumerKey = $"workflow:7201:{target.Id}", OperationId = intent});
        await Db.SaveChangesAsync();
        Assert.IsTrue((await reader.ReadAsync(7201, target, workset.OutputDirectory, default)).CanRetry);
        Assert.IsFalse((await reader.ReadAsync(7202, target, workset.OutputDirectory, default)).CanRetry);
        Db.Set<ArtifactConsumerDbModel>().Add(new() {Id = "also-used", ConsumerKey = "workflow:7202:another-target", OperationId = intent});
        await Db.SaveChangesAsync();
        Assert.IsFalse((await reader.ReadAsync(7201, target, workset.OutputDirectory, default)).CanRetry);
        Assert.AreEqual(4, snapshot.Reads);
    }

    [DataTestMethod]
    [DataRow("empty")]
    [DataRow("duplicate")]
    [DataRow("unknown")]
    [DataRow("excluded")]
    [DataRow("unstarted")]
    [DataRow("mixed-source-selection")]
    [DataRow("mixed-output-directory")]
    public async Task Selected_resume_rejects_invalid_scopes_before_touching_downloads(string mode)
    {
        var selected = Chosen(Source("scope", "baidu")) with {DownloadTaskId = mode == "unstarted" ? null : 42, Excluded = mode == "excluded"};
        var item = Workset(selected.SelectedSource!) with {Targets = [selected]};
        var signal = new CloudSelectionSignal
        {
            ResumeTargetIds = mode switch {"empty" => [], "duplicate" => [selected.Id, selected.Id], "unknown" => ["other"], _ => [selected.Id]},
            SourceIds = mode == "mixed-source-selection" ? [selected.SelectedSourceId!] : [],
            OutputDirectory = mode == "mixed-output-directory" ? _root : null
        };
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => new AwaitCloudDownloadsActivity().ResumeAsync(Context(item), item,
            JsonSerializer.Serialize(signal, JsonSerializerOptions.Web), default));
        Assert.AreEqual(0, await Db.DownloadTasks.CountAsync());
        Assert.AreEqual(0, await Db.CloudDownloadOperations.CountAsync());
        Assert.AreEqual(0, _baidu.TransferRequests.Count);
    }

    [DataTestMethod]
    [DataRow("transfer")]
    [DataRow("enqueue")]
    [DataRow("select")]
    public async Task Selected_download_resume_cannot_be_used_at_other_nodes(string phase)
    {
        var selected = Chosen(Source("wrong-phase", "baidu")) with {DownloadTaskId = 42};
        var item = Workset(selected.SelectedSource!) with {Targets = [selected]};
        CloudActivity activity = phase switch {"transfer" => new BaiduTransferActivity(), "enqueue" => new EnqueueCloudSourcesActivity(), _ => new SelectCloudSourcesActivity()};
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => activity.ResumeAsync(Context(item), item,
            JsonSerializer.Serialize(new CloudSelectionSignal {ResumeTargetIds = [selected.Id]}, JsonSerializerOptions.Web), default));
        Assert.AreEqual(0, await Db.DownloadTasks.CountAsync());
        Assert.AreEqual(0, _baidu.TransferRequests.Count);
    }

    [TestMethod]
    public async Task Selected_completed_artifact_does_not_certify_an_unselected_unfinished_target()
    {
        var pending = Chosen(Source("pending", "gofile")) with {DownloadTaskId = 42};
        var complete = Chosen(Source("complete", "googleDrive")) with {PublishedArtifactId = "retained-artifact", PublishedFiles = ["retained-file"]};
        var item = Workset(pending.SelectedSource!, complete.SelectedSource!) with {Targets = [pending, complete]};
        var outcome = await new AwaitCloudDownloadsActivity().ResumeAsync(Context(item), item,
            JsonSerializer.Serialize(new CloudSelectionSignal {ResumeTargetIds = [complete.Id]}, JsonSerializerOptions.Web), default);
        Assert.IsNotNull(outcome.Suspension);
        Assert.AreSame(pending, Item(outcome).Targets[0]);
        Assert.IsNull(Item(outcome).Targets[0].Problem);
        Assert.AreEqual(complete.PublishedArtifactId, Item(outcome).Targets[1].PublishedArtifactId);
        CollectionAssert.AreEqual(complete.PublishedFiles, Item(outcome).Targets[1].PublishedFiles);
        Assert.AreEqual(0, await Db.DownloadTasks.CountAsync());
    }
}
