using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.InsideWorld.Business;
using Bakabase.InsideWorld.Business.Components;
using Bakabase.InsideWorld.Business.Components.Downloader.Components;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models.Constants;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Components;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.Abstractions.Components.Tasks;
using Bakabase.InsideWorld.Business.Components.Downloader.Services;
using Bakabase.InsideWorld.Business.Components.Downloader.Models.Db;
using Bakabase.Modules.Downloader.Cloud;
using Bakabase.Modules.PostParser.Models.Domain;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Modules.Workflow.Abstractions.Services;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Bakabase.Service.Components.CloudDownloads;
using Bakabase.Service.Components.DownloadIntents;
using Bakabase.TestKit.Utils;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Localization;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Tests;

[TestClass]
public sealed partial class CloudWorkflowTests
{
    private IServiceProvider _services = null!;
    private string _root = null!;
    private readonly FixtureProvider _baidu = new("baidu");
    private readonly FixtureProvider _google = new("googleDrive");
    private readonly FixtureProvider _gofile = new("gofile");
    private BakabaseDbContext Db => _services.GetRequiredService<BakabaseDbContext>();
    private CloudOperationService Operations => _services.GetRequiredService<CloudOperationService>();

    [TestInitialize]
    public async Task Initialize()
    {
        _root = TestServiceBuilder.NewTestDirectory();
        _services = await TestServiceBuilder.BuildServiceProvider(_root, collection =>
        {
            collection.RemoveAll<ICloudDownloadProvider>();
            collection.AddSingleton<ICloudDownloadProvider>(_baidu);
            collection.AddSingleton<ICloudDownloadProvider>(_google);
            collection.AddSingleton<ICloudDownloadProvider>(_gofile);
            collection.AddSingleton<CloudDownloaderHelper>();
            collection.AddScoped<CloudOperationService>();
            collection.AddSingleton<IDownloaderFactory>(sp => new QueueOnlyFactory(sp.GetRequiredService<CloudDownloaderHelper>()));
            collection.AddTransient(sp => new BakabaseLocalizer(sp.GetRequiredService<IStringLocalizer<SharedResource>>()));
            collection.AddSingleton<DownloadQueuePump, NoopQueuePump>();
        });
        Db.Set<WorkflowDefinitionDbModel>().Add(new() {Id = 7201, Name = "Artifact consumer", TriggerKind = CloudWorkflow.Trigger, Enabled = true});
        Db.Set<WorkflowRunDbModel>().Add(new() {Id = 7201, WorkflowDefinitionId = 7201, Status = WorkflowRunStatus.Running, StartedAt = DateTime.UtcNow});
        await Db.SaveChangesAsync();
    }

    [TestCleanup]
    public async Task Cleanup()
    {
        await _services.GetRequiredService<BTaskManager>().DisposeAsync();
        await Db.DisposeAsync();
    }

    private static CloudSource Source(string id, string provider, string? group = null, string kind = "share") => new()
    {
        Id = id, ProviderId = provider, GroupId = group, Kind = kind,
        Url = provider switch
        {
            "baidu" => kind == "remote" ? "/my-files/folder" : "https://pan.baidu.com/s/fixture-" + id,
            "googleDrive" => "https://drive.google.com/drive/folders/fixture-" + id,
            "gofile" => "https://gofile.io/d/fixture-" + id,
            _ => "https://unrecognized.example.invalid/share/" + id
        }
    };
    private DownloadWorkset Workset(params CloudSource[] sources) => CloudWorkflow.FromSources(sources, "Fixture", "fixture", Path.Combine(_root, "downloads"));
    private static CloudDownloadTarget Chosen(CloudSource source, string? account = null) => new()
    {
        Id = "target-" + source.Id, Sources = [source], SelectedSourceId = source.Id, AccountId = account
    };
    private WorkflowExecutionContext Context(object payload, CloudWorkflowConfig? config = null) => new()
    {
        RunId = 7201, WorkflowDefinitionId = 0, TriggerKind = CloudWorkflow.Trigger, Payload = payload,
        ActivityConfigJson = JsonSerializer.Serialize(config ?? new(), JsonSerializerOptions.Web),
        Services = _services, Logger = NullLogger.Instance
    };
    private static DownloadWorkset Item(WorkflowItemOutcome outcome) =>
        (DownloadWorkset)(outcome.Suspension?.Item ?? outcome.Replacement ?? throw new AssertFailedException("Workset was dropped."));

    [TestMethod]
    public async Task Download_center_rejects_legacy_builtin_configuration_before_starting_a_run()
    {
        var definition = new WorkflowDefinitionDbModel
        {
            Name = CloudWorkflow.BuiltinName, TriggerKind = CloudWorkflow.Trigger, Enabled = true, IsBuiltin = true
        };
        Db.Set<WorkflowDefinitionDbModel>().Add(definition);
        await Db.SaveChangesAsync();
        Db.Set<WorkflowActivityDbModel>().AddRange(CloudWorkflow.Nodes.Select((kind, order) => new WorkflowActivityDbModel
        {
            WorkflowDefinitionId = definition.Id, Kind = kind, Order = order,
            ConfigJson = order == 0 ? JsonSerializer.Serialize(new {OutputDirectory = Path.Combine(_root, "unexpected")}) : "{}"
        }));
        await Db.SaveChangesAsync();
        using var boundaryServices = new ServiceCollection().BuildServiceProvider();
        var starter = new DownloadIntentStarter(Db, _services.GetRequiredService<IWorkflowDefinitionService>(),
            new UserStoragePolicy(false), boundaryServices);
        var before = await Db.Set<WorkflowRunDbModel>().CountAsync();
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => starter.StartFilesAsync(
            Workset(Source("center", "googleDrive")), null, null, default, downloadsOnly: true));
        Assert.AreEqual(before, await Db.Set<WorkflowRunDbModel>().CountAsync());
        Assert.AreEqual(0, await Db.DownloadTasks.CountAsync());
    }

    [TestMethod]
    public void Parsed_mixed_sources_preserve_credentials_groups_and_unknown_links()
    {
        var result = CloudWorkflow.Prepare(new PostDownloadInfo
        {
            Title = "Parsed fixture", Resources =
            [
                new() {Link = "https://pan.baidu.com/s/fixture", Code = "abcd", Password = "archive-password", GroupId = "main",
                    Extraction = new() {Requirement = "required", Steps = [new() {Id = "unpack", Op = "extract", Password = "archive-password"}]}},
                new() {Link = "https://drive.google.com/drive/folders/fixture", GroupId = "main"},
                new() {Link = "https://gofile.io/d/fixture", GroupId = "extra"},
                new() {Link = "https://unrecognized.example.invalid/download"}
            ]
        }, "post:123:revision:4");
        Assert.AreEqual(3, result.Targets.Count);
        Assert.AreEqual(4, result.Targets.Sum(t => t.Sources.Count));
        var main = result.Targets.Single(t => t.Sources.Count == 2);
        var baidu = main.Sources.Single(s => s.ProviderId == "baidu");
        Assert.AreEqual("abcd", baidu.AccessCode);
        Assert.AreEqual("archive-password", baidu.ArchivePassword);
        StringAssert.Contains(baidu.ExtractionPlanJson!, "archive-password");
        Assert.IsTrue(result.Targets.SelectMany(t => t.Sources).Any(s => s.ProviderId == "unknown"));
        Assert.IsTrue(result.Targets.All(t => t.SelectedSource == null), "Parsing alone does not choose or download mirrors.");
    }

    [TestMethod]
    public async Task Ungrouped_historical_sources_wait_for_explicit_selection()
    {
        var workset = Workset(Source("a", "baidu"), Source("b", "gofile"));
        var result = await new SelectCloudSourcesActivity().ProcessItemAsync(Context(workset), workset, default);
        Assert.AreEqual("cloud.chooseSources", result.Suspension?.Reason);
        Assert.AreEqual(2, Item(result).Targets.Count);
        Assert.IsTrue(Item(result).Targets.All(t => t.SelectedSource == null && !t.Excluded));
        Assert.AreEqual(0, await Db.CloudDownloadOperations.CountAsync());
    }

    [TestMethod]
    public async Task Configured_selection_chooses_one_mirror_and_keeps_unknown_as_a_target()
    {
        var workset = Workset(Source("a", "baidu", "mirror"), Source("b", "googleDrive", "mirror"), Source("unknown", "unknown"));
        var config = new CloudWorkflowConfig {AllowUngrouped = true, PreferProviders = ["googleDrive", "baidu"]};
        var result = await new SelectCloudSourcesActivity().ProcessItemAsync(Context(workset, config), workset, default);
        Assert.IsNull(result.Suspension);
        var selected = Item(result);
        Assert.AreEqual("b", selected.Targets.Single(t => t.Sources.Count == 2).SelectedSourceId);
        Assert.AreEqual("unknown", selected.Targets.Single(t => t.Sources.Count == 1).SelectedSource?.ProviderId);
        Assert.AreEqual(3, selected.Targets.Sum(t => t.Sources.Count));
    }

    [TestMethod]
    public async Task Selection_rejects_two_mirrors_and_source_ids_outside_the_saved_result()
    {
        var workset = Workset(Source("a", "baidu", "mirror"), Source("b", "googleDrive", "mirror"));
        var activity = new SelectCloudSourcesActivity();
        await Assert.ThrowsExceptionAsync<ArgumentException>(() => activity.ProcessItemAsync(
            Context(workset, new() {SourceIds = ["a", "b"]}), workset, default));
        await Assert.ThrowsExceptionAsync<ArgumentException>(() => activity.ProcessItemAsync(
            Context(workset, new() {SourceIds = ["outside"]}), workset, default));
        Assert.AreEqual(0, await Db.DownloadTasks.CountAsync());
    }

    [TestMethod]
    public async Task Baidu_account_is_assigned_only_to_the_selected_Baidu_source()
    {
        var workset = Workset(Source("a", "baidu", "mirror"), Source("b", "googleDrive", "mirror"), Source("c", "baidu"), Source("d", "gofile"));
        var result = await new SelectCloudSourcesActivity().ProcessItemAsync(Context(workset,
            new() {SourceIds = ["b", "c", "d"], AccountId = "account-a"}), workset, default);
        Assert.IsNull(result.Suspension);
        var item = Item(result);
        Assert.AreEqual("account-a", item.Targets.Single(t => t.SelectedSourceId == "c").AccountId);
        Assert.IsTrue(item.Targets.Where(t => t.SelectedSource?.ProviderId != "baidu").All(t => t.AccountId == null));
    }

    [TestMethod]
    public async Task Unknown_source_waits_with_its_saved_details_instead_of_downloading_HTML()
    {
        var source = Source("unknown", "unknown") with {AccessCode = "saved-code"};
        var workset = Workset(source) with {Targets = [Chosen(source)]};
        var result = await new EnqueueCloudSourcesActivity().ProcessItemAsync(Context(workset), workset, default);
        Assert.AreEqual("cloud.cloudAttention", result.Suspension?.Reason);
        var target = Item(result).Targets.Single();
        Assert.AreEqual(source, target.SelectedSource);
        Assert.IsFalse(string.IsNullOrWhiteSpace(target.Problem));
        Assert.IsNull(target.DownloadTaskId);
        Assert.AreEqual(0, await Db.DownloadTasks.CountAsync());
        Assert.AreEqual(0, _google.DownloadCalls + _gofile.DownloadCalls + _baidu.DownloadCalls);
    }

    [TestMethod]
    public async Task Transfer_node_processes_only_Baidu_shares_and_preserves_other_providers()
    {
        var sources = new[] {Source("baidu", "baidu"), Source("google", "googleDrive"), Source("unknown", "unknown")};
        var workset = Workset(sources) with {Targets = sources.Select(s => Chosen(s, s.ProviderId == "baidu" ? "account-a" : null)).ToList()};
        var result = await new BaiduTransferActivity().ProcessItemAsync(Context(workset), workset, default);
        Assert.IsNull(result.Suspension);
        var item = Item(result);
        Assert.AreEqual(3, item.Targets.Count);
        Assert.AreEqual(1, _baidu.TransferRequests.Count);
        Assert.IsNotNull(item.Targets.Single(t => t.SelectedSource?.ProviderId == "baidu").RemoteEntry);
        Assert.IsTrue(item.Targets.Where(t => t.SelectedSource?.ProviderId != "baidu").All(t => t.RemoteEntry == null && t.AccountId == null));
    }

    [TestMethod]
    public async Task Own_drive_remote_source_passes_transfer_node_without_a_share_transfer()
    {
        var source = Source("own-folder", "baidu", kind: "remote");
        var workset = Workset(source) with {Targets = [Chosen(source, "account-a")]};
        var result = await new BaiduTransferActivity().ProcessItemAsync(Context(workset), workset, default);
        Assert.IsNull(result.Suspension);
        Assert.AreEqual(source, Item(result).Targets.Single().SelectedSource);
        Assert.AreEqual(0, _baidu.TransferRequests.Count);
        Assert.AreEqual(0, _baidu.ReadinessCalls);
        Assert.AreEqual(0, await Db.CloudDownloadOperations.CountAsync());
    }

    [TestMethod]
    public async Task Transfer_receipt_is_reused_after_recreating_the_scope()
    {
        var target = Chosen(Source("replay", "baidu"), "account-a");
        var first = await Operations.TransferAsync(72, target, "account-a", "/Bakabase", default);
        await using var scope = _services.CreateAsyncScope();
        var second = await scope.ServiceProvider.GetRequiredService<CloudOperationService>().TransferAsync(72, target, "account-a", "/Bakabase", default);
        Assert.AreEqual(first, second);
        Assert.AreEqual(1, _baidu.TransferRequests.Count);
        var operation = await Db.CloudDownloadOperations.AsNoTracking().SingleAsync();
        Assert.AreEqual("succeeded", operation.State);
        Assert.AreEqual(first.OperationId, operation.Id);
        Assert.IsNotNull(operation.ReceiptJson);
    }

    [TestMethod]
    public async Task Unknown_remote_outcome_is_reconciled_without_repeating_the_transfer()
    {
        var target = Chosen(Source("uncertain", "baidu"), "account-a");
        _baidu.TransferError = new IOException("Connection closed after remote write.");
        await Assert.ThrowsExceptionAsync<IOException>(() => Operations.TransferAsync(73, target, "account-a", "/Bakabase", default));
        Assert.AreEqual("unknown", (await Db.CloudDownloadOperations.SingleAsync()).State);
        _baidu.TransferError = null;
        await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => Operations.TransferAsync(73, target, "account-a", "/Bakabase", default));
        Assert.AreEqual(1, _baidu.TransferRequests.Count);
        Assert.AreEqual(1, _baidu.Reconciliations);
        _baidu.CanReconcile = true;
        var recovered = await Operations.TransferAsync(73, target, "account-a", "/Bakabase", default);
        Assert.AreEqual(1, _baidu.TransferRequests.Count);
        Assert.AreEqual("succeeded", (await Db.CloudDownloadOperations.SingleAsync()).State);
        Assert.AreEqual(recovered, await Operations.TransferAsync(73, target, "account-a", "/Bakabase", default));
    }

    [TestMethod]
    public async Task Typed_preflight_failure_can_retry_without_becoming_an_unknown_side_effect()
    {
        var target = Chosen(Source("preflight", "baidu"), "account-a");
        _baidu.TransferError = new CloudProviderException("The account must log in.", mayHaveSideEffects: false);
        await Assert.ThrowsExceptionAsync<CloudProviderException>(() => Operations.TransferAsync(74, target, "account-a", "/Bakabase", default));
        Assert.AreEqual("failed", (await Db.CloudDownloadOperations.SingleAsync()).State);
        _baidu.TransferError = null;
        var receipt = await Operations.TransferAsync(74, target, "account-a", "/Bakabase", default);
        Assert.AreEqual("account-a", receipt.Entry.AccountId);
        Assert.AreEqual(2, _baidu.TransferRequests.Count);
        Assert.AreEqual(0, _baidu.Reconciliations);
    }

    [TestMethod]
    public async Task Queue_replay_keeps_one_task_and_each_content_target_has_its_own_directory()
    {
        var output = Path.Combine(_root, "queue-output");
        var first = Chosen(Source("first", "googleDrive"));
        var second = Chosen(Source("second", "gofile"));
        var firstId = await Operations.EnqueueAsync(75, first, output, default);
        var replayId = await Operations.EnqueueAsync(75, first, output, default);
        var secondId = await Operations.EnqueueAsync(75, second, output, default);
        Assert.AreEqual(firstId, replayId);
        Assert.AreNotEqual(firstId, secondId);
        var rows = await Db.DownloadTasks.AsNoTracking().OrderBy(t => t.Id).ToArrayAsync();
        Assert.AreEqual(2, rows.Length);
        Assert.AreNotEqual(rows[0].DownloadPath, rows[1].DownloadPath);
        foreach (var row in rows)
        {
            StringAssert.StartsWith(Path.GetFullPath(row.DownloadPath), Path.GetFullPath(output) + Path.DirectorySeparatorChar);
            Assert.IsFalse(row.AutoRetry);
            var request = JsonSerializer.Deserialize<CloudDownloadRequest>(row.Options!, JsonSerializerOptions.Web)!;
            Assert.AreEqual(row.DownloadPath, request.OutputDirectory);
            Assert.IsNull(request.AccountId);
            Assert.IsTrue(await Db.CloudDownloadOperations.AsNoTracking().AnyAsync(o => o.DownloadTaskId == row.Id));
        }
    }

    [TestMethod]
    public async Task Replaying_an_already_started_operation_does_not_restart_a_manually_stopped_task()
    {
        var target = Chosen(Source("stopped-replay", "googleDrive"));
        var output = Path.Combine(_root, "stopped-output");
        var taskId = await Operations.EnqueueAsync(79, target, output, default);
        await _services.GetRequiredService<DownloadTaskService>().Stop(t => t.Id == taskId);
        Assert.AreEqual("started", (await Db.CloudDownloadOperations.AsNoTracking().SingleAsync()).State);
        Assert.AreEqual(DownloadTaskDbModelStatus.Disabled, (await Db.DownloadTasks.AsNoTracking().SingleAsync()).Status);
        var replayId = await Operations.EnqueueAsync(79, target, output, default);
        Assert.AreEqual(taskId, replayId);
        Assert.AreEqual(DownloadTaskDbModelStatus.Disabled, (await Db.DownloadTasks.AsNoTracking().SingleAsync()).Status);
        Assert.AreEqual(1, await Db.DownloadTasks.CountAsync());
    }

    [DataTestMethod]
    [DataRow("site")]
    [DataRow("http")]
    [DataRow("bt")]
    public async Task Queued_sources_resume_past_resolution_without_reusing_a_stale_problem(string transport)
    {
        Assert.IsNotNull(_services.GetService<DownloadSourceResolver>(), "This regression requires the production resolution stage.");
        var source = transport switch
        {
            "http" => new CloudSource {Id = "queued-http", ProviderId = "http", Kind = "http", Url = "https://example.test/file.zip"},
            "bt" => new CloudSource {Id = "queued-bt", ProviderId = "magnet", Url = "magnet:?xt=urn:btih:0123456789012345678901234567890123456789"},
            _ => Source("queued-site", "googleDrive")
        };
        var workset = Workset(source);
        var target = Chosen(source) with {Problem = "Previous configuration needed attention."};
        if (transport == "site")
            target = target with {DownloadTaskId = await Operations.EnqueueAsync(7201, target, workset.OutputDirectory, default)};
        else
            target = target with {TransportTaskId = transport + ":existing-job"};
        workset = workset with {Targets = [target]};
        // Native jobs pass through the transfer node in the same source workset. Site replay also
        // exercises the real queue binding to ensure the old error does not create a second task.
        CloudActivity activity = transport == "site" ? new EnqueueCloudSourcesActivity() : new BaiduTransferActivity();
        var result = await activity.ResumeAsync(Context(workset), workset, "{}", default);
        Assert.IsNull(result.Suspension);
        var resumed = Item(result).Targets.Single();
        Assert.IsNull(resumed.Problem);
        Assert.AreEqual(target.DownloadTaskId, resumed.DownloadTaskId);
        Assert.AreEqual(target.TransportTaskId, resumed.TransportTaskId);
        Assert.AreEqual(source, resumed.SelectedSource);
        Assert.AreEqual(transport == "site" ? 1 : 0, await Db.DownloadTasks.CountAsync());
        Assert.AreEqual(0, _baidu.TransferRequests.Count);
        Assert.AreEqual(0, _google.DownloadCalls + _gofile.DownloadCalls + _baidu.DownloadCalls);
    }

    [TestMethod]
    public async Task Failed_queue_binding_rolls_back_the_task_before_any_download_can_start()
    {
        // Fail the binding update after the task INSERT has succeeded, inside the same transaction.
        await Db.Database.ExecuteSqlRawAsync("""
            CREATE TRIGGER FailCloudQueueBinding BEFORE UPDATE OF DownloadTaskId ON CloudDownloadOperations
            WHEN NEW.DownloadTaskId IS NOT NULL
            BEGIN SELECT RAISE(ABORT, 'fixture binding failure'); END;
            """);
        await Assert.ThrowsExceptionAsync<DbUpdateException>(() => Operations.EnqueueAsync(76,
            Chosen(Source("rollback", "googleDrive")), Path.Combine(_root, "queue-output"), default));
        Db.ChangeTracker.Clear();
        Assert.AreEqual(0, await Db.DownloadTasks.CountAsync(), "The queue task must roll back with its failed operation binding.");
        var operation = await Db.CloudDownloadOperations.AsNoTracking().SingleAsync();
        Assert.IsNull(operation.DownloadTaskId);
        Assert.AreEqual("planned", operation.State);
        Assert.AreEqual(0, _google.DownloadCalls);
    }

    [TestMethod]
    public async Task Reconciliation_rejects_a_receipt_for_another_account()
    {
        var target = Chosen(Source("wrong-receipt", "baidu"), "account-a");
        _baidu.TransferError = new IOException("Connection closed after write.");
        await Assert.ThrowsExceptionAsync<IOException>(() => Operations.TransferAsync(77, target, "account-a", "/Bakabase", default));
        _baidu.TransferError = null;
        _baidu.CanReconcile = true;
        _baidu.ReceiptOverride = request => new(request.OperationId, new("baidu", "different-account", request.RemoteDirectory));
        await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => Operations.TransferAsync(77, target, "account-a", "/Bakabase", default));
        Assert.AreEqual("unknown", (await Db.CloudDownloadOperations.SingleAsync()).State);
        Assert.AreEqual(1, _baidu.TransferRequests.Count);
    }

    [TestMethod]
    public async Task Completed_side_effect_cannot_be_repeated_by_changing_account_or_directory()
    {
        var target = Chosen(Source("immutable", "baidu"), "account-a");
        await Operations.TransferAsync(78, target, "account-a", "/Bakabase", default);
        await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => Operations.TransferAsync(78, target, "account-b", "/Bakabase", default));
        await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => Operations.TransferAsync(78, target, "account-a", "/AnotherDirectory", default));
        Assert.AreEqual(1, _baidu.TransferRequests.Count);
        Assert.AreEqual(1, await Db.CloudDownloadOperations.CountAsync());
    }

    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Excluding_sources_detaches_shared_tasks_but_stops_exclusive_tasks_and_keeps_unknown_details(bool exclusive)
    {
        var keep = Chosen(Source("keep", "googleDrive"));
        var stop = Chosen(Source("stop", "gofile")) with {AcquisitionNonce = exclusive ? "7201" : null};
        var unknown = Chosen(Source("unknown", "unknown"));
        var output = Path.Combine(_root, "selected-output");
        keep = keep with {DownloadTaskId = await Operations.EnqueueAsync(7201, keep, output, default)};
        stop = stop with {DownloadTaskId = await Operations.EnqueueAsync(7201, stop, output, default)};
        var workset = new DownloadWorkset {OutputDirectory = output, Targets = [keep, stop, unknown]};
        var result = await new EnqueueCloudSourcesActivity().ResumeAsync(Context(workset), workset,
            JsonSerializer.Serialize(new CloudSelectionSignal {SourceIds = [keep.SelectedSourceId!]}, JsonSerializerOptions.Web), default);
        Assert.IsNull(result.Suspension);
        var item = Item(result);
        Assert.AreEqual(3, item.Targets.Count);
        Assert.IsFalse(item.Targets.Single(t => t.Id == keep.Id).Excluded);
        var excludedUnknown = item.Targets.Single(t => t.Id == unknown.Id);
        Assert.IsTrue(excludedUnknown.Excluded);
        Assert.AreEqual(unknown.Sources.Single(), excludedUnknown.Sources.Single());
        Assert.AreEqual(exclusive ? DownloadTaskDbModelStatus.Disabled : DownloadTaskDbModelStatus.InProgress,
            (await Db.DownloadTasks.AsNoTracking().SingleAsync(t => t.Id == stop.DownloadTaskId)).Status);
        Assert.AreEqual(2, await Db.DownloadTasks.CountAsync());
    }

    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Cancellation_preserves_shared_acquisition_and_stops_exclusive_siblings(bool exclusive)
    {
        var first = Chosen(Source("cancel-first", "googleDrive")) with {AcquisitionNonce = exclusive ? "7201" : null};
        var second = Chosen(Source("cancel-second", "gofile")) with {AcquisitionNonce = exclusive ? "7201" : null};
        var output = Path.Combine(_root, "cancel-output");
        first = first with {DownloadTaskId = await Operations.EnqueueAsync(7201, first, output, default)};
        second = second with {DownloadTaskId = await Operations.EnqueueAsync(7201, second, output, default)};
        var workset = new DownloadWorkset {OutputDirectory = output, Targets = [first, second]};
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsExceptionAsync<OperationCanceledException>(() => new AwaitCloudDownloadsActivity()
            .ProcessItemAsync(Context(workset), workset, cancellation.Token));
        Assert.IsTrue((await Db.DownloadTasks.AsNoTracking().ToArrayAsync()).All(t => t.Status ==
            (exclusive ? DownloadTaskDbModelStatus.Disabled : DownloadTaskDbModelStatus.InProgress)));
    }

    [DataTestMethod]
    [DataRow("missing-result")]
    [DataRow("empty")]
    [DataRow("missing-file")]
    [DataRow("outside")]
    [DataRow("invalid-json")]
    [DataRow("valid")]
    public async Task Completed_cloud_task_requires_a_valid_existing_file_manifest(string mode)
    {
        var output = Path.Combine(_root, "completed-output");
        Directory.CreateDirectory(output);
        var validFile = Path.Combine(output, "file.zip");
        File.WriteAllText(validFile, "fixture-content");
        var outside = Path.Combine(_root, "unrelated.zip");
        File.WriteAllText(outside, "not-owned-by-the-task");
        var task = new DownloadTaskDbModel
        {
            Key = "completed-fixture", ThirdPartyId = ThirdPartyId.GoogleDrive, Type = 1,
            DownloadPath = output, Status = DownloadTaskDbModelStatus.Complete
        };
        Db.DownloadTasks.Add(task);
        await Db.SaveChangesAsync();
        if (mode != "missing-result")
        {
            var files = mode switch
            {
                "empty" => Array.Empty<string>(), "missing-file" => [Path.Combine(output, "missing.zip")],
                "outside" => [outside], _ => new[] {validFile}
            };
            Db.DownloadResults.Add(new DownloadResultDbModel
            {
                DownloadTaskId = task.Id, ThirdPartyId = ThirdPartyId.GoogleDrive, SourceKey = task.Key,
                Name = "Fixture", Kind = DownloadResultKind.LocalFiles, Path = output, DownloadDirectory = output,
                DeduplicationKey = "fixture-" + mode,
                FilesJson = mode == "invalid-json" ? "invalid" : JsonSerializer.Serialize(files)
            });
            await Db.SaveChangesAsync();
        }
        var target = Chosen(Source("complete", "googleDrive")) with {DownloadTaskId = task.Id};
        var workset = new DownloadWorkset {OutputDirectory = output, Targets = [target]};
        var result = await new AwaitCloudDownloadsActivity().ProcessItemAsync(Context(workset), workset, default);
        if (mode == "valid")
        {
            Assert.IsNull(result.Suspension);
            CollectionAssert.AreEqual(new[] {validFile}, Item(result).Targets.Single().Files);
        }
        else
        {
            Assert.AreEqual("cloud.cloudAttention", result.Suspension?.Reason);
            Assert.IsFalse(string.IsNullOrWhiteSpace(Item(result).Targets.Single().Problem));
            Assert.AreEqual(0, Item(result).Targets.Single().Files.Count);
        }
        Assert.AreEqual("not-owned-by-the-task", File.ReadAllText(outside));
    }

    [DataTestMethod]
    [DataRow("empty")]
    [DataRow("outside")]
    [DataRow("missing")]
    public async Task Unified_cloud_downloader_rejects_incomplete_or_unowned_provider_results(string mode)
    {
        var output = Path.Combine(_root, "provider-result");
        Directory.CreateDirectory(output);
        var outside = Path.Combine(_root, "unrelated-provider-file.zip");
        File.WriteAllText(outside, "unrelated");
        var paths = mode switch
        {
            "empty" => Array.Empty<string>(), "outside" => [outside],
            _ => new[] {Path.Combine(output, "missing.zip")}
        };
        _google.DownloadResult = new(paths);
        var source = Source("bad-result", "googleDrive");
        var request = new CloudDownloadRequest("fixture-operation", source, null, null, output);
        var task = new DownloadTask
        {
            Id = 700, Key = source.Id, Type = 1, ThirdPartyId = ThirdPartyId.GoogleDrive, DownloadPath = output,
            Options = JsonSerializer.Serialize(request, JsonSerializerOptions.Web)
        };
        using var downloader = new GoogleCloudDownloader(_services);
        var execute = typeof(GoogleCloudDownloader).GetMethod("StartCore", BindingFlags.Instance | BindingFlags.NonPublic)!;
        await Assert.ThrowsExceptionAsync<InvalidDataException>(() => (Task)execute.Invoke(downloader, [task, CancellationToken.None])!);
        Assert.AreEqual(0, await Db.DownloadTaskFiles.CountAsync());
        Assert.AreEqual(0, await Db.DownloadResults.CountAsync());
        Assert.AreEqual("unrelated", File.ReadAllText(outside));
    }

    private sealed class NoopQueuePump(IServiceProvider services, ILogger<DownloadQueuePump> logger)
        : DownloadQueuePump(services, logger)
    {
        protected override Task RunPassAsync() => Task.CompletedTask;
    }

    private sealed class QueueOnlyFactory(CloudDownloaderHelper helper) : IDownloaderFactory
    {
        public List<DownloaderDefinition> GetDefinitions() => [];
        public IDownloaderHelper GetHelper(ThirdPartyId thirdPartyId, int taskType) => helper;
        public IDownloader GetDownloader(ThirdPartyId thirdPartyId, int taskType) => new QueueOnlyDownloader(thirdPartyId);
    }

    // The production task binding/start path runs, while network execution is replaced at the factory boundary.
    private sealed class QueueOnlyDownloader(ThirdPartyId site) : IDownloader
    {
        public ThirdPartyId ThirdPartyId => site;
        public int TaskType => 1;
        public DownloaderStatus Status { get; private set; } = DownloaderStatus.JustCreated;
        public string? Current => null;
        public double? EstimatedRemainingSeconds => null;
        public double? DownloadSpeedBytesPerSecond => null;
        public DownloaderStopBy? StoppedBy { get; set; }
        public string? Message => null;
        public int FailureTimes => 0;
        public string? Checkpoint => null;
        public DateTime LastActivityAt => DateTime.UtcNow;
        public Task<bool> Start(DownloadTask task) { Status = DownloaderStatus.Downloading; return Task.FromResult(true); }
        public async Task Stop(DownloaderStopBy stopBy)
        {
            Status = DownloaderStatus.Stopped;
            StoppedBy = stopBy;
            if (OnStatusChanged != null) await OnStatusChanged();
        }
        public void ResetStatus() => Status = DownloaderStatus.JustCreated;
        public void Dispose() { }
        public event Func<Task>? OnStatusChanged;
        public event Func<string, Task>? OnNameAcquired;
        public event Func<decimal, Task>? OnProgress;
        public event Func<Task>? OnDownloadSpeedChanged;
        public event Func<string, long, Task>? OnFileDownloaded;
        public event Func<Task>? OnCurrentChanged;
        public event Func<string, Task>? OnCheckpointChanged;
    }

    private sealed class FixtureProvider(string id) : ICloudDownloadProvider
    {
        public string ProviderId => id;
        public bool SupportsTransfer => id == "baidu";
        public bool CanCorrectAccessCodeAfterFailure(string code) =>
            id == "baidu" && code is "BAIDU_ACCESS_CODE_REQUIRED" or "BAIDU_SHARE_ACCESS_CODE_REJECTED";
        public List<CloudTransferRequest> TransferRequests { get; } = [];
        public int ReadinessCalls { get; private set; }
        public int DownloadCalls { get; private set; }
        public CloudDownloadResult? DownloadResult { get; set; }
        public Func<Func<CloudDownloadProgress, Task>, Task>? ProgressScript { get; set; }
        public int Reconciliations { get; private set; }
        public Exception? TransferError { get; set; }
        public bool CanReconcile { get; set; }
        public Func<CloudTransferRequest, CloudTransferReceipt>? ReceiptOverride { get; set; }
        public Task<CloudReadiness> CheckReadyAsync(string? accountId, CancellationToken ct)
        { ReadinessCalls++; return Task.FromResult(new CloudReadiness(true)); }
        private CloudTransferReceipt Receipt(CloudTransferRequest request) => ReceiptOverride?.Invoke(request) ?? new(request.OperationId,
            new CloudRemoteEntry(id, request.AccountId, request.RemoteDirectory));
        public Task<CloudTransferReceipt> TransferAsync(CloudTransferRequest request, CancellationToken ct)
        {
            TransferRequests.Add(request);
            return TransferError == null ? Task.FromResult(Receipt(request)) : Task.FromException<CloudTransferReceipt>(TransferError);
        }
        public Task<CloudTransferReceipt?> ReconcileTransferAsync(CloudTransferRequest request, CancellationToken ct)
        { Reconciliations++; return Task.FromResult(CanReconcile ? Receipt(request) : null); }
        public async Task<CloudDownloadResult> DownloadAsync(CloudDownloadRequest request, Func<CloudDownloadProgress, Task> progress, CancellationToken ct)
        {
            DownloadCalls++;
            if (ProgressScript != null) await ProgressScript(progress);
            return DownloadResult != null ? DownloadResult
                : throw new AssertFailedException("The queue fixture must never execute a provider or access a cloud drive.");
        }
    }
}
