using System.Text.Json;
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
        var signal = JsonSerializer.Serialize(new CloudSelectionSignal {ResumeTargetIds = [paused.Id]}, JsonSerializerOptions.Web);
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
