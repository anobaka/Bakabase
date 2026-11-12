using System.Text.Json;
using Bakabase.InsideWorld.Business;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.InsideWorld.Business.Components.Compression;
using Bakabase.InsideWorld.Business.Components.Configurations.Models.Domain;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain;
using Bakabase.Modules.Acquisition.Models.Domain;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Service.Components.Acquisition.Planning;
using Bakabase.Service.Components.CloudDownloads;
using Bakabase.Service.Components.FileProcessing;
using Bakabase.TestKit.Utils;
using Bootstrap.Components.Configuration.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Tests;

[TestClass]
[DoNotParallelize] // Process-wide task gates use IDs; isolated database fixtures reuse those IDs.
public sealed partial class AcquisitionStoragePolicyTests
{
    private static string Root => Path.Combine(Path.GetTempPath(), "policy-tests");
    private static Task<IServiceProvider> Services() => TestServiceBuilder.BuildServiceProvider(s => s.AddScoped(_ =>
        new Bakabase.Service.Components.DownloadIntents.DownloadMethodRegistry([], [], null!, null!)));
    private static AcquisitionPlan Plan() => new()
    {
        Title = "Resource", OutputDirectory = Root, Parts = [new() {Id = "main", SelectedSourceId = "source", Sources =
            [new() {Id = "source", Url = "https://example.test/file.zip"}], Processing = new() {Requirement = "notRequired"}}]
    };
    private static Task Guard(string _) => Task.CompletedTask;

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task WorkspaceCleanupRechecksSelectionValuesInsteadOfPreviewSummaryArrayIdentity(bool changeSelection)
    {
        var root = Directory.CreateTempSubdirectory("acquisition-cleanup-selection-");
        try
        {
            var services = await Services(); await using var lifetime = (IAsyncDisposable)services;
            var plans = services.GetRequiredService<AcquisitionPlanService>();
            var db = services.GetRequiredService<BakabaseDbContext>();
            var store = services.GetRequiredService<IArtifactOperationStore>();
            var maintenance = services.GetRequiredService<ArtifactWorkspaceMaintenance>();
            // No extraction is involved; keep this tiny fixture independent of the host's reserve threshold.
            var executor = new FileProcessingPlanExecutor(services.GetRequiredService<IArchiveExtractionService>());
            var input = Path.Combine(root.FullName, "input.bin"); await File.WriteAllTextAsync(input, "original");
            var processed = await executor.ExecuteAsync(new() {Requirement = "notRequired"}, root.FullName,
                [input], Path.Combine(root.FullName, "processing"));
            Assert.IsTrue(processed.Completed, processed.Message);
            var artifact = await store.AdoptAsync(new("cleanup-selection", "cleanup-selection", "files.process", "fixture"),
                new(processed.OutputDirectory!, processed.Files, "processedFiles", true), "fixture");
            var task = await plans.CreatePreparedPlanAsync(Plan() with {OutputDirectory = root.FullName}, []);
            db.WorkflowRuns.Add(new WorkflowRunDbModel {Id = 500, Status = WorkflowRunStatus.Success});
            var row = (await db.AcquisitionTasks.FindAsync(task.Id))!;
            row.WorkflowRunId = 500; row.Status = AcquisitionStatus.Completed;
            await db.SaveChangesAsync(); db.ChangeTracker.Clear();
            Assert.IsTrue(await new ArtifactOperationBindingService(db).BindAsync(
                "workflow", "500", "process:artifact.process", artifact.OperationId));
            var previews = new AcquisitionStoragePolicyPreviewStore();
            var preview = await plans.PreviewWorkspaceCleanupAsync(new([task.Id]), previews, maintenance, Guard, default);
            Assert.AreEqual(1, preview.EligibleCount);
            var intermediate = (await FileProcessingPlanExecutor.GetReclaimableFilesAsync(artifact.RootDirectory)).Single();
            if (changeSelection) await File.WriteAllTextAsync(intermediate, "changed private bytes");
            var result = await plans.ReclaimWorkspacesAsync(new(preview.PreviewId, [task.Id]), previews, maintenance, Guard, default);
            if (changeSelection)
            {
                Assert.AreEqual(0, result.CompletedTaskIds.Count);
                Assert.AreEqual("files_changed", result.Skipped.Single().ReasonCode);
                Assert.IsTrue(File.Exists(intermediate));
            }
            else
            {
                CollectionAssert.AreEqual(new[] {task.Id}, result.CompletedTaskIds.ToArray());
                Assert.AreEqual(1, result.RemovedFiles);
                Assert.IsFalse(File.Exists(intermediate));
            }
            Assert.IsTrue(File.Exists(input));
            Assert.IsTrue(processed.Files.All(File.Exists));
            Assert.IsNull((await db.AcquisitionTasks.AsNoTracking().SingleAsync(t => t.Id == task.Id)).DeletedAt);
        }
        finally {root.Delete(true);}
    }

    [TestMethod]
    public async Task NewTaskFreezesDefaultsAndChangingRetentionNeverChangesDownloadIdentity()
    {
        var services = await Services(); await using var lifetime = (IAsyncDisposable)services;
        var plans = services.GetRequiredService<AcquisitionPlanService>();
        var options = services.GetRequiredService<IBOptions<AcquisitionOptions>>().Value;
        options.StoragePolicy = new() {DownloadDirectoryTemplate = "{Provider}/{Title} [D{OperationId}]", SourceCleanup = "quarantine"};
        var created = await plans.CreatePreparedPlanAsync(Plan(), []);
        Assert.AreEqual("quarantine", created.StoragePolicy!.SourceCleanup);
        var before = AcquisitionPlanCompiler.Workset(created.Id, 1, created.Plan!, []);
        options.StoragePolicy = new() {SourceCleanup = "delete"};
        Assert.AreEqual("quarantine", (await plans.GetAsync(created.Id, default))!.StoragePolicy!.SourceCleanup);
        var changed = await plans.UpdateAsync(created.Id, new(created.Task.PlanRevision, StoragePolicy: new() {CleanupIntermediates = false}), default);
        var after = AcquisitionPlanCompiler.Workset(created.Id, 2, changed.Plan!, []);
        Assert.AreEqual(DownloadArtifactCoordinator.DownloadIntent(before.Targets[0], before.OutputDirectory),
            DownloadArtifactCoordinator.DownloadIntent(after.Targets[0], after.OutputDirectory));
        Assert.AreEqual(2, changed.PlanHistory.Count);
        var nodes = AcquisitionPlanCompiler.Compile(created.Plan!).Nodes;
        Assert.AreEqual("download.cleanupArtifacts", nodes.Last().Kind);
        Assert.AreEqual("fs.publishArtifacts", nodes[^2].Kind);
    }

    [TestMethod]
    public async Task BatchPinsDefaultsVersionsAndPreservesTaskStateAndNodeOverrides()
    {
        var services = await Services(); await using var lifetime = (IAsyncDisposable)services;
        var plans = services.GetRequiredService<AcquisitionPlanService>(); var db = services.GetRequiredService<BakabaseDbContext>();
        var previews = new AcquisitionStoragePolicyPreviewStore(); var first = await plans.CreatePreparedPlanAsync(Plan(), []);
        first = await plans.UpdateAsync(first.Id, new(first.Task.PlanRevision, StoragePolicyOverrides:
            [new("main", new() {CleanupIntermediates = false})]), default);
        var second = await plans.CreateDraftAsync(new("draft", OutputDirectory: Root), default);
        var preview = await plans.PreviewStoragePolicyAsync(new([first.Id, second.Id]), previews, Guard, default);
        Assert.AreEqual(2, preview.EligibleCount);
        await plans.UpdateAsync(second.Id, new(second.Task.PlanRevision, StoragePolicy: new() {SourceCleanup = "quarantine"}), default);
        var before = await db.AcquisitionTasks.AsNoTracking().SingleAsync(t => t.Id == first.Id);
        var result = await plans.ApplyStoragePolicyAsync(new(preview.PreviewId, [first.Id, second.Id]), previews, Guard, default);
        CollectionAssert.AreEqual(new[] {first.Id}, result.AppliedTaskIds.ToArray());
        Assert.AreEqual("revision_changed", result.Skipped.Single().ReasonCode);
        var after = await db.AcquisitionTasks.AsNoTracking().SingleAsync(t => t.Id == first.Id);
        Assert.AreEqual(before.Phase, after.Phase); Assert.AreEqual(before.Status, after.Status);
        Assert.AreEqual(before.WorkflowRunId, after.WorkflowRunId); Assert.AreEqual(before.TargetDirectory, after.TargetDirectory);
        Assert.AreEqual(false, (await plans.GetAsync(first.Id, default))!.Plan!.Parts.Single().StoragePolicy!.CleanupIntermediates);
        Assert.AreEqual(result, await plans.ApplyStoragePolicyAsync(new(preview.PreviewId, [first.Id, second.Id]), previews, Guard, default));
        await Assert.ThrowsExceptionAsync<ArgumentException>(() => plans.ApplyStoragePolicyAsync(new(preview.PreviewId, [first.Id]), previews, Guard, default));
    }

    [TestMethod]
    public async Task BatchRejectsChangedDefaultsAndAnyRecordedHistoricalRun()
    {
        var services = await Services(); await using var lifetime = (IAsyncDisposable)services;
        var plans = services.GetRequiredService<AcquisitionPlanService>(); var db = services.GetRequiredService<BakabaseDbContext>();
        var options = services.GetRequiredService<IBOptions<AcquisitionOptions>>().Value; var previews = new AcquisitionStoragePolicyPreviewStore();
        var task = await plans.CreatePreparedPlanAsync(Plan(), []);
        var preview = await plans.PreviewStoragePolicyAsync(new([task.Id]), previews, Guard, default);
        options.StoragePolicy = new() {CleanupIntermediates = false};
        Assert.AreEqual("policy_changed", (await plans.ApplyStoragePolicyAsync(new(preview.PreviewId, [task.Id]), previews, Guard, default)).Skipped.Single().ReasonCode);
        var row = (await db.AcquisitionTasks.FindAsync(task.Id))!;
        row.BuilderStateJson = "{}"; row.PlanJson = JsonSerializer.Serialize(Plan(), WorkflowJson.Options);
        row.PlanHistoryJson = JsonSerializer.Serialize(new[] {new AcquisitionPlanRevision(1, Plan(), [], DateTime.UtcNow, PreviousRunId: 616)}, WorkflowJson.Options);
        await db.SaveChangesAsync();
        Assert.IsNull((await plans.GetAsync(task.Id, default))!.StoragePolicy);
        Assert.IsFalse((await plans.GetAsync(task.Id, default))!.CanEditStoragePolicy);
        preview = await plans.PreviewStoragePolicyAsync(new([task.Id]), previews, Guard, default);
        Assert.AreEqual("already_started", preview.Rows.Single().ReasonCode);
        await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => plans.UpdateAsync(task.Id, new(task.Task.PlanRevision, StoragePolicy: new()), default));
    }

    [TestMethod]
    public async Task AcquisitionDownloadDefaultIsIndependentAndFrozenAtCreation()
    {
        var root = Directory.CreateTempSubdirectory("acquisition-download-");
        try
        {
            var services = await Services(); await using var lifetime = (IAsyncDisposable)services;
            var options = services.GetRequiredService<IBOptions<AcquisitionOptions>>().Value;
            var downloads = services.GetRequiredService<IBOptions<DownloaderGlobalOptions>>().Value;
            var resolver = services.GetRequiredService<AcquisitionOutputDirectoryResolver>();
            var plans = services.GetRequiredService<AcquisitionPlanService>();
            downloads.DefaultDownloadPath = Root;
            Assert.AreEqual(Root, resolver.ResolveNew(null));
            options.DownloadRootDirectory = root.FullName;
            Assert.AreEqual(root.FullName, resolver.ResolveNew(null));
            Assert.AreEqual("", resolver.ResolveNew(""), "an explicit unconfigured task remains unconfigured");
            Assert.AreEqual(Root, resolver.ResolveNew(Root), "an explicit caller root wins");
            var created = await plans.CreatePreparedPlanAsync(Plan() with {OutputDirectory = null!}, []);
            options.DownloadRootDirectory = null;
            Assert.AreEqual(Root, resolver.ResolveNew(null));
            Assert.AreEqual(root.FullName, (await plans.GetAsync(created.Id, default))!.Plan!.OutputDirectory);
            Assert.AreEqual(Root, downloads.DefaultDownloadPath, "the acquisition default never changes the download center");
            options.DownloadRootDirectory = Path.Combine(root.FullName, "disconnected");
            Assert.AreEqual(options.DownloadRootDirectory, resolver.ResolveNew(null), "producer completion can freeze an unavailable root without falling back");
            Assert.ThrowsException<DirectoryNotFoundException>(() => AcquisitionOutputDirectoryResolver.ValidateConfiguredRoot(options.DownloadRootDirectory));
            Assert.IsFalse(Directory.Exists(options.DownloadRootDirectory), "missing configured storage cannot fall back or be recreated");
        }
        finally {root.Delete(true);}
    }

    [TestMethod]
    public async Task BatchRootOnlyPreservesOverridesAndRecomputesInheritedDelivery()
    {
        var root = Directory.CreateTempSubdirectory("acquisition-root-batch-");
        try
        {
            var services = await Services(); await using var lifetime = (IAsyncDisposable)services;
            var plans = services.GetRequiredService<AcquisitionPlanService>();
            var options = services.GetRequiredService<IBOptions<AcquisitionOptions>>().Value;
            var previews = new AcquisitionStoragePolicyPreviewStore();
            var inherited = await plans.CreatePreparedPlanAsync(Plan(), []);
            var separate = await plans.CreatePreparedPlanAsync(Plan(), []);
            separate = await plans.UpdateAsync(separate.Id, new(separate.Task.PlanRevision,
                DeliverySettings: new() {RootDirectory = Path.Combine(Root, "final")},
                StoragePolicy: new() {CleanupIntermediates = false, SourceCleanup = "quarantine"},
                StoragePolicyOverrides: [new("main", new() {CleanupIntermediates = false})]), default);
            var before = separate.Plan!;
            // Root-only changes must not pick up unrelated changed defaults.
            options.StoragePolicy = new() {SourceCleanup = "delete"};
            options.DeliveryRootDirectory = Path.Combine(Root, "different-final-default");
            var preview = await plans.PreviewStoragePolicyAsync(new([inherited.Id, separate.Id], root.FullName, false), previews, Guard, default);
            Assert.IsNull(preview.Policy);
            StringAssert.StartsWith(preview.Rows.Single(r => r.TaskId == inherited.Id).DeliveryPreview!, Path.Combine(root.FullName, "Acquired"));
            Assert.AreEqual(before.DeliveryLayout!.TaskDirectory, preview.Rows.Single(r => r.TaskId == separate.Id).DeliveryPreview);
            var result = await plans.ApplyStoragePolicyAsync(new(preview.PreviewId, [inherited.Id, separate.Id]), previews, Guard, default);
            Assert.AreEqual(2, result.AppliedTaskIds.Count);
            var after = (await plans.GetAsync(separate.Id, default))!;
            Assert.AreEqual(root.FullName, after.Task.TargetDirectory);
            Assert.AreEqual(root.FullName, after.Plan!.OutputDirectory);
            Assert.AreEqual(before.DeliveryLayout.TaskDirectory, after.Plan.DeliveryLayout!.TaskDirectory);
            Assert.AreEqual(before.StoragePolicy, after.Plan.StoragePolicy);
            Assert.AreEqual(before.Parts[0].StoragePolicy, after.Plan.Parts[0].StoragePolicy);
            CollectionAssert.AreEqual(before.Parts[0].Sources.ToArray(), after.Plan.Parts[0].Sources.ToArray());
            Assert.AreEqual(Root, after.PlanHistory[0].Plan.OutputDirectory, "historical paths are retained");
            StringAssert.StartsWith((await plans.GetAsync(inherited.Id, default))!.Plan!.DeliveryLayout!.RootDirectory, root.FullName);
        }
        finally {root.Delete(true);}
    }

    [TestMethod]
    public async Task BatchDefaultRootIsPreviewBoundAndExplicitRootIgnoresUnrelatedDefaultChanges()
    {
        var root = Directory.CreateTempSubdirectory("acquisition-root-default-");
        try
        {
            var services = await Services(); await using var lifetime = (IAsyncDisposable)services;
            var plans = services.GetRequiredService<AcquisitionPlanService>();
            var options = services.GetRequiredService<IBOptions<AcquisitionOptions>>().Value;
            var downloads = services.GetRequiredService<IBOptions<DownloaderGlobalOptions>>().Value;
            var previews = new AcquisitionStoragePolicyPreviewStore();
            var task = await plans.CreateDraftAsync(new("draft", OutputDirectory: Root), default);
            options.DownloadRootDirectory = root.FullName;
            var preview = await plans.PreviewStoragePolicyAsync(new([task.Id], "", false), previews, Guard, default);
            options.DownloadRootDirectory = null;
            downloads.DefaultDownloadPath = Root;
            Assert.AreEqual("policy_changed", (await plans.ApplyStoragePolicyAsync(new(preview.PreviewId, [task.Id]), previews, Guard, default)).Skipped.Single().ReasonCode);
            Assert.AreEqual(Root, (await plans.GetAsync(task.Id, default))!.Task.TargetDirectory);
            downloads.DefaultDownloadPath = root.FullName;
            preview = await plans.PreviewStoragePolicyAsync(new([task.Id], "", false), previews, Guard, default);
            Assert.AreEqual(1, (await plans.ApplyStoragePolicyAsync(new(preview.PreviewId, [task.Id]), previews, Guard, default)).AppliedTaskIds.Count);
            Assert.AreEqual(root.FullName, (await plans.GetAsync(task.Id, default))!.Task.TargetDirectory);
            preview = await plans.PreviewStoragePolicyAsync(new([task.Id], root.FullName, false), previews, Guard, default);
            options.DownloadRootDirectory = Path.Combine(root.FullName, "unavailable-new-default");
            options.StoragePolicy = new() {CleanupIntermediates = false};
            Assert.AreEqual(1, (await plans.ApplyStoragePolicyAsync(new(preview.PreviewId, [task.Id]), previews, Guard, default)).AppliedTaskIds.Count);
        }
        finally {root.Delete(true);}
    }

    [TestMethod]
    public async Task BatchPreviewVersionDescribesCapturedValuesWhenDefaultsChangeDuringAuthorization()
    {
        var root = Directory.CreateTempSubdirectory("acquisition-root-version-");
        try
        {
            var services = await Services(); await using var lifetime = (IAsyncDisposable)services;
            var plans = services.GetRequiredService<AcquisitionPlanService>();
            var options = services.GetRequiredService<IBOptions<AcquisitionOptions>>().Value;
            options.DownloadRootDirectory = root.FullName;
            var task = await plans.CreatePreparedPlanAsync(Plan(), []);
            var previews = new AcquisitionStoragePolicyPreviewStore();
            Task Authorize(string path)
            {
                options.StoragePolicy = options.StoragePolicy with {SourceCleanup = "delete"};
                return Task.CompletedTask;
            }
            var preview = await plans.PreviewStoragePolicyAsync(new([task.Id], ""), previews, Authorize, default);
            Assert.AreEqual("keep", preview.Policy!.SourceCleanup);
            var applied = await plans.ApplyStoragePolicyAsync(new(preview.PreviewId, [task.Id]), previews, Guard, default);
            Assert.AreEqual("policy_changed", applied.Skipped.Single().ReasonCode);
            Assert.AreEqual(Root, (await plans.GetAsync(task.Id, default))!.Task.TargetDirectory);
        }
        finally {root.Delete(true);}
    }

    [DataTestMethod]
    [DataRow("omitted", true)]
    [DataRow("null", true)]
    [DataRow("false", false)]
    [DataRow("true", true)]
    public async Task HttpRecordBindingAppliesCurrentSettingsUnlessExplicitlyOptedOut(string flag, bool applyDefaults)
    {
        var root = Directory.CreateTempSubdirectory("acquisition-http-defaults-");
        try
        {
            var services = await Services(); await using var lifetime = (IAsyncDisposable)services;
            var plans = services.GetRequiredService<AcquisitionPlanService>();
            var options = services.GetRequiredService<IBOptions<AcquisitionOptions>>().Value;
            var task = await plans.CreatePreparedPlanAsync(Plan(), []);
            var processing = Directory.CreateDirectory(Path.Combine(root.FullName, "processing")).FullName;
            var delivery = Path.Combine(root.FullName, "final");
            options.DownloadRootDirectory = root.FullName;
            options.ProcessingWorkspaceDirectory = processing;
            options.StoragePolicy = new() {ProcessingWorkspaceDirectory = processing, SourceCleanup = "delete"};
            options.DeliveryRootDirectory = delivery;
            // MVC uses Newtonsoft for this positional record. Constructing new(...) in tests
            // does not exercise its handling of omitted optional constructor parameters.
            var json = "{\"taskIds\":[" + task.Id + "],\"outputDirectory\":\"\"" +
                (flag == "omitted" ? "" : ",\"applyCurrentSettings\":" + flag) + "}";
            var input = Newtonsoft.Json.JsonConvert.DeserializeObject<AcquisitionStoragePolicyPreviewInput>(json)!;
            var previews = new AcquisitionStoragePolicyPreviewStore();
            var preview = await plans.PreviewStoragePolicyAsync(input, previews, Guard, default);
            Assert.AreEqual(applyDefaults, preview.Policy != null);
            Assert.AreEqual(1, preview.EligibleCount);
            var expectedProcessing = applyDefaults ? processing : root.FullName;
            StringAssert.StartsWith(preview.Rows.Single().ProcessingPreview!, expectedProcessing);
            StringAssert.StartsWith(preview.Rows.Single().DeliveryPreview!, applyDefaults ? delivery : Path.Combine(root.FullName, "Acquired"));
            var applied = await plans.ApplyStoragePolicyAsync(new(preview.PreviewId, [task.Id]), previews, Guard, default);
            Assert.AreEqual(1, applied.AppliedTaskIds.Count);
            var updated = (await plans.GetAsync(task.Id, default))!;
            Assert.AreEqual(root.FullName, updated.Plan!.OutputDirectory);
            Assert.AreEqual(applyDefaults ? processing : "", updated.StoragePolicy!.ProcessingWorkspaceDirectory);
            Assert.AreEqual(applyDefaults ? "delete" : "keep", updated.StoragePolicy.SourceCleanup);
            Assert.AreEqual(applyDefaults ? delivery : "", updated.DeliverySettings!.RootDirectory);
        }
        finally {root.Delete(true);}
    }

    [TestMethod]
    public async Task BatchRootGuardsDisappearancesAndKeepsLegacyNullPolicies()
    {
        var root = Directory.CreateTempSubdirectory("acquisition-root-guard-");
        try
        {
            var services = await Services(); await using var lifetime = (IAsyncDisposable)services;
            var plans = services.GetRequiredService<AcquisitionPlanService>();
            var db = services.GetRequiredService<BakabaseDbContext>();
            var previews = new AcquisitionStoragePolicyPreviewStore();
            var task = await plans.CreatePreparedPlanAsync(Plan(), []);
            var row = (await db.AcquisitionTasks.FindAsync(task.Id))!;
            row.BuilderStateJson = "{}";
            row.PlanJson = JsonSerializer.Serialize(Plan(), WorkflowJson.Options);
            await db.SaveChangesAsync();
            var original = row.PlanJson;
            await Assert.ThrowsExceptionAsync<UnauthorizedAccessException>(() => plans.PreviewStoragePolicyAsync(
                new([task.Id], root.FullName, false), previews, path => path == root.FullName
                    ? throw new UnauthorizedAccessException("Not shared") : Task.CompletedTask, default));
            var preview = await plans.PreviewStoragePolicyAsync(new([task.Id], root.FullName, false), previews, Guard, default);
            root.Delete();
            Assert.AreEqual("policy_invalid", (await plans.ApplyStoragePolicyAsync(new(preview.PreviewId, [task.Id]), previews, Guard, default)).Skipped.Single().ReasonCode);
            Assert.AreEqual(original, (await db.AcquisitionTasks.AsNoTracking().SingleAsync(t => t.Id == task.Id)).PlanJson);
            Directory.CreateDirectory(root.FullName);
            preview = await plans.PreviewStoragePolicyAsync(new([task.Id], root.FullName, false), previews, Guard, default);
            Assert.AreEqual(1, (await plans.ApplyStoragePolicyAsync(new(preview.PreviewId, [task.Id]), previews, Guard, default)).AppliedTaskIds.Count);
            var changed = (await plans.GetAsync(task.Id, default))!;
            Assert.IsNull(changed.StoragePolicy);
            Assert.IsNull(changed.DeliverySettings);
            Assert.IsNull(changed.Plan!.DeliveryLayout);
            Assert.AreEqual(root.FullName, changed.Plan.OutputDirectory);
        }
        finally {if (Directory.Exists(root.FullName)) Directory.Delete(root.FullName, true);}
    }

    [TestMethod]
    public async Task BatchRootCannotChangeTaskThatStartsAfterPreview()
    {
        var root = Directory.CreateTempSubdirectory("acquisition-root-started-");
        try
        {
            var services = await Services(); await using var lifetime = (IAsyncDisposable)services;
            var plans = services.GetRequiredService<AcquisitionPlanService>();
            var db = services.GetRequiredService<BakabaseDbContext>();
            var previews = new AcquisitionStoragePolicyPreviewStore();
            var task = await plans.CreatePreparedPlanAsync(Plan(), []);
            var preview = await plans.PreviewStoragePolicyAsync(new([task.Id], root.FullName, false), previews, Guard, default);
            var row = (await db.AcquisitionTasks.FindAsync(task.Id))!;
            row.Status = Bakabase.Modules.Acquisition.Abstractions.Models.Domain.Constants.AcquisitionStatus.Running;
            await db.SaveChangesAsync();
            var result = await plans.ApplyStoragePolicyAsync(new(preview.PreviewId, [task.Id]), previews, Guard, default);
            Assert.AreEqual("already_started", result.Skipped.Single().ReasonCode);
            Assert.AreEqual(Root, (await plans.GetAsync(task.Id, default))!.Task.TargetDirectory);
            Assert.AreEqual(1, (await plans.GetAsync(task.Id, default))!.Task.PlanRevision);
        }
        finally {root.Delete(true);}
    }

    [TestMethod]
    public async Task UnavailableDownloadRootWaitsBeforeCreatingWorkflowOrDownload()
    {
        var root = Directory.CreateTempSubdirectory("acquisition-root-preflight-");
        try
        {
            var services = await Services(); await using var lifetime = (IAsyncDisposable)services;
            var plans = services.GetRequiredService<AcquisitionPlanService>();
            var db = services.GetRequiredService<BakabaseDbContext>();
            var options = services.GetRequiredService<IBOptions<AcquisitionOptions>>().Value;
            options.DownloadRootDirectory = root.FullName;
            var task = await plans.CreatePreparedPlanAsync(Plan() with {OutputDirectory = null!}, []);
            root.Delete();
            var result = await plans.RunAsync(task.Id, task.Task.PlanRevision, default);
            Assert.AreEqual("attention", result.Task.Phase);
            Assert.IsTrue(result.Issues.Any(i => i.Code == "OUTPUT_DIRECTORY_REQUIRED"));
            Assert.IsNull(result.Task.WorkflowRunId);
            Assert.AreEqual(0, await db.WorkflowRuns.CountAsync());
            Assert.AreEqual(0, await db.DownloadTasks.CountAsync());
            Assert.IsFalse(Directory.Exists(root.FullName), "an unavailable selected volume is never recreated");
        }
        finally {if (Directory.Exists(root.FullName)) Directory.Delete(root.FullName, true);}
    }

    [TestMethod]
    public async Task SelectedSourceStepsCanBeOverriddenButCannotMoveSharedWorkspace()
    {
        var services = await Services(); await using var lifetime = (IAsyncDisposable)services;
        var plans = services.GetRequiredService<AcquisitionPlanService>();
        var sourcePlan = new FileProcessingPlan {Requirement = "required", Steps = [new() {Id = "extract", Op = "extractArchive", Input = "download"}]};
        var original = Plan(); original = original with {Parts = [original.Parts[0] with {Processing = null,
            Sources = [original.Parts[0].Sources[0] with {Processing = sourcePlan}]}]};
        var task = await plans.CreatePreparedPlanAsync(original, []);
        var updated = await plans.UpdateAsync(task.Id, new(task.Task.PlanRevision, StoragePolicyOverrides:
            [new("main", new() {CleanupIntermediates = false}, "extract")]), default);
        Assert.AreEqual(false, updated.Plan!.Parts[0].Sources[0].Processing!.Steps[0].StoragePolicy!.CleanupIntermediates);
        await Assert.ThrowsExceptionAsync<ArgumentException>(() => plans.UpdateAsync(task.Id, new(updated.Task.PlanRevision,
            StoragePolicyOverrides: [new("main", new() {ProcessingWorkspaceDirectory = Root}, "extract")]), default));
    }

    [TestMethod]
    public void UnsafeTemplatesAndOversizeRenderedSegmentsAreRejectedBeforeRunning()
    {
        foreach (var template in new[] {"../{OperationId}", "/{OperationId}", "{Title}", "{Title}{Title}{Title}{OperationId}", "{Unknown}/{OperationId}", "CON/{OperationId}"})
            Assert.ThrowsException<ArgumentException>(() => ArtifactStoragePolicies.Validate(new() {DownloadDirectoryTemplate = template}));
        var rendered = ArtifactStoragePolicies.RenderDownload(new(), new string('游', 512), new string('a', 64), "baidu");
        Assert.IsTrue(System.Text.Encoding.UTF8.GetByteCount(rendered) <= 240);
        StringAssert.Contains(rendered, new string('a', 64));
    }
}
