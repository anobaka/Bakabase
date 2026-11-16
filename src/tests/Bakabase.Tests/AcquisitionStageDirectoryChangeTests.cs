using System.Text.Json;
using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.InsideWorld.Business;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.Modules.Acquisition.Abstractions.Models.Db;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Acquisition.Abstractions.Services;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Workflow.Abstractions.Services;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Service.Components.Acquisition.Planning;
using Bakabase.Service.Components.DownloadIntents;
using Bakabase.Service.Components.CloudDownloads;
using Bakabase.Service.Components.FileProcessing;
using Bakabase.TestKit.Utils;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Tests;

[TestClass, DoNotParallelize]
public sealed class AcquisitionStageDirectoryChangeTests
{
    private string root = null!;
    private IServiceProvider services = null!;
    private BakabaseDbContext db = null!;
    private AcquisitionPlanService plans = null!;
    private IArtifactOperationStore artifacts = null!;
    [TestInitialize]
    public async Task Setup()
    {
        root = Path.Combine(Path.GetTempPath(), "stage-directory-edit-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        services = await TestServiceBuilder.BuildServiceProvider(s => s.AddScoped(_ => new DownloadMethodRegistry([], [], null!, null!)));
        db = services.GetRequiredService<BakabaseDbContext>();
        artifacts = services.GetRequiredService<IArtifactOperationStore>();
        plans = new(db, [], services.GetRequiredService<IAcquisitionService>(), services.GetRequiredService<IWorkflowDefinitionService>(),
            services.GetRequiredService<DownloadIntentService>(), new Storage(), artifacts: artifacts);
    }
    [TestCleanup]
    public void Cleanup()
    {
        (services as IDisposable)?.Dispose(); if (Directory.Exists(root)) Directory.Delete(root, true);
    }
    private async Task<AcquisitionTaskDbModel> TaskAsync(string name = "One")
    {
        var baseDirectory = Path.Combine(root, name); Directory.CreateDirectory(baseDirectory);
        var plan = new AcquisitionPlan {Title = name, OutputDirectory = root,
            Parts = [new() {Id = "part", Title = name, SelectedSourceId = "source",
                Sources = [new() {Id = "source", Url = "https://example.test/a.zip", ProviderId = "http"}],
                Processing = new() {Requirement = "notRequired"}}]};
        var row = new AcquisitionTaskDbModel {Title = name, BuilderId = "prepared", PlanRevision = 1, Status = AcquisitionStatus.Waiting,
            Phase = "attention", CreatedAt = DateTime.Now, UpdatedAt = DateTime.Now};
        db.AcquisitionTasks.Add(row); await db.SaveChangesAsync();
        plan = plan with {StageDirectories = new() {TaskId = row.Id, Parts = new() { ["part"] = new()
        {
            Download = new(Path.Combine(baseDirectory, "Download"), "borrowed", "downloadRecord"),
            Processing = new(Path.Combine(baseDirectory, "Processing"), "borrowed", "processingRecord"),
            Delivery = new(Path.Combine(baseDirectory, "Delivery"), "borrowed", "deliveryRecord")
        }}}};
        foreach (var namePart in new[] {"Download", "Processing", "Delivery"}) Directory.CreateDirectory(Path.Combine(baseDirectory, namePart));
        row.PlanJson = JsonSerializer.Serialize(plan, JsonSerializerOptions.Web); await db.SaveChangesAsync(); return row;
    }
    private AcquisitionStageDirectoryChangeInput Input(AcquisitionTaskDbModel task, string target, bool move = false) =>
        new(task.PlanRevision, [new("part", Processing: new(target, move))]);

    [TestMethod]
    public async Task NeverStartedTaskCanAdoptNonemptyDirectoryWithoutMovingOldFilesOrGrantingOwnership()
    {
        var task = await TaskAsync(); var old = Path.Combine(root, "One", "Processing"); await File.WriteAllTextAsync(Path.Combine(old, "old.txt"), "keep");
        var target = Path.Combine(root, "selected"); Directory.CreateDirectory(target); await File.WriteAllTextAsync(Path.Combine(target, "manual.txt"), "current");
        var before = task.PlanJson;
        var applied = await plans.ChangeStageDirectoriesAsync(task.Id, Input(task, target), null, default);
        var binding = applied.Directories.Parts["part"].Processing!;
        Assert.AreEqual(target, binding.Directory); Assert.AreEqual("borrowed", binding.Ownership); Assert.IsNull(binding.Allocation);
        Assert.IsTrue(Guid.TryParseExact(binding.BindingId, "N", out _)); Assert.AreEqual(2, applied.Revision);
        Assert.IsTrue(File.Exists(Path.Combine(old, "old.txt"))); Assert.IsTrue(File.Exists(Path.Combine(target, "manual.txt")));
        Assert.AreEqual(target, (await plans.GetStageDirectoriesAsync(task.Id, default)).Parts.Single().ProcessingDirectory);
        Assert.AreEqual(0, await db.WorkflowRuns.CountAsync()); Assert.AreEqual(0, await db.Artifacts.CountAsync());
        Assert.IsNotNull(before); Assert.IsNull(JsonSerializer.Deserialize<AcquisitionPlan>(before, JsonSerializerOptions.Web)!.StageDirectories!.Parts["part"].Processing!.BindingId);
    }

    [TestMethod]
    public async Task MoveUsesCurrentFilesAndLeavesRunHistoryUntouched()
    {
        var task = await TaskAsync(); var old = Path.Combine(root, "One", "Processing"); await File.WriteAllTextAsync(Path.Combine(old, "manual.txt"), "current");
        var target = Path.Combine(root, "selected"); var previousHistory = task.PlanHistoryJson;
        var result = await plans.ChangeStageDirectoriesAsync(task.Id, Input(task, target, true), null, default);
        Assert.AreEqual("current", await File.ReadAllTextAsync(Path.Combine(target, "manual.txt")));
        Assert.IsFalse(File.Exists(Path.Combine(old, "manual.txt"))); Assert.IsTrue(Directory.Exists(old));
        Assert.AreEqual(1L, result.Changes.Single().FileCount); Assert.AreEqual(0, await db.WorkflowNodeExecutions.CountAsync());
        Assert.IsNull(previousHistory); StringAssert.Contains(task.PlanHistoryJson!, "directories.changed");
    }

    [DataTestMethod, DataRow(true), DataRow(false)]
    public async Task ReturningToSavedAllocationPreservesOwnershipOnlyWithExistingReceipt(bool hasReceipt)
    {
        var task = await TaskAsync();
        var canonical = Path.Combine(root, "One", "Processing");
        var locations = new FileProcessingWorkspaceResolver(Path.Combine(root, "metadata"), new Storage());
        var scope = new ArtifactDirectoryScope("acquisition", task.Id.ToString(), "processing-part", root, canonical);
        if (hasReceipt) await locations.EnsureScopedDirectoryAsync(scope, default);
        var deep = Path.Combine(canonical, "old-result"); Directory.CreateDirectory(deep);
        await File.WriteAllTextAsync(Path.Combine(deep, "manual.txt"), "current");
        var plan = JsonSerializer.Deserialize<AcquisitionPlan>(task.PlanJson!, JsonSerializerOptions.Web)!;
        plan.StageDirectories!.Parts["part"] = plan.StageDirectories.Parts["part"] with
        {Processing = new(canonical, "exclusive", "taskAllocation") {Allocation = scope}};
        task.PlanJson = JsonSerializer.Serialize(plan, JsonSerializerOptions.Web);
        var run = new WorkflowRunDbModel {Status = WorkflowRunStatus.Success, StartedAt = DateTime.Now,
            CurrentItemJson = JsonSerializer.Serialize(new {type = typeof(DownloadWorkset).FullName + ", " + typeof(DownloadWorkset).Assembly.GetName().Name,
                item = new DownloadWorkset {Targets = [new() {Id = "part", ProcessedDirectory = deep}]}}, JsonSerializerOptions.Web)};
        db.WorkflowRuns.Add(run); await db.SaveChangesAsync();
        db.Set<WorkflowRunSourceDbModel>().Add(new() {WorkflowRunId = run.Id, Module = "acquisition", EntityKind = "task", EntityId = task.Id.ToString(), Revision = "1"});
        task.WorkflowRunId = run.Id; await db.SaveChangesAsync();
        var service = new AcquisitionPlanService(db, [], services.GetRequiredService<IAcquisitionService>(), services.GetRequiredService<IWorkflowDefinitionService>(),
            services.GetRequiredService<DownloadIntentService>(), new Storage(), artifacts: artifacts, workspaces: locations);
        Assert.AreEqual(deep, (await service.GetStageDirectoriesAsync(task.Id, default)).Parts.Single().ProcessingDirectory);
        var result = await service.ChangeStageDirectoriesAsync(task.Id, Input(task, canonical, true), null, default);
        var current = result.Directories.Parts["part"].Processing!;
        Assert.AreEqual(hasReceipt ? "exclusive" : "borrowed", current.Ownership);
        Assert.AreEqual(hasReceipt ? scope : null, current.Allocation);
        Assert.IsNotNull(current.BindingId);
        Assert.AreEqual("current", await File.ReadAllTextAsync(Path.Combine(canonical, "manual.txt")));
        Assert.IsFalse(File.Exists(Path.Combine(deep, "manual.txt")));
        Assert.AreEqual(canonical, (await service.GetStageDirectoriesAsync(task.Id, default)).Parts.Single().ProcessingDirectory);
        StringAssert.Contains(run.CurrentItemJson, "old-result");
    }

    [TestMethod]
    public async Task AllConflictsCheckedBeforeAnyFileMove()
    {
        var task = await TaskAsync(); var old = Path.Combine(root, "One", "Processing");
        await File.WriteAllTextAsync(Path.Combine(old, "a.txt"), "a"); await File.WriteAllTextAsync(Path.Combine(old, "b.txt"), "b");
        var target = Path.Combine(root, "selected"); Directory.CreateDirectory(target); await File.WriteAllTextAsync(Path.Combine(target, "b.txt"), "keep");
        await Assert.ThrowsExactlyAsync<IOException>(() => plans.ChangeStageDirectoriesAsync(task.Id, Input(task, target, true), null, default));
        Assert.IsTrue(File.Exists(Path.Combine(old, "a.txt"))); Assert.IsTrue(File.Exists(Path.Combine(old, "b.txt")));
        Assert.AreEqual("keep", await File.ReadAllTextAsync(Path.Combine(target, "b.txt"))); Assert.AreEqual(1, task.PlanRevision);
    }

    [TestMethod]
    public async Task AnotherTaskPlanStillProtectsTarget()
    {
        var task = await TaskAsync(); var other = await TaskAsync("Other");
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => plans.ChangeStageDirectoriesAsync(task.Id,
            Input(task, Path.Combine(root, "Other", "Processing")), null, default));
        Assert.AreEqual(1, task.PlanRevision); Assert.AreEqual(1, other.PlanRevision);
    }

    [TestMethod]
    public async Task StaleRevisionDoesNotCreateDirectory()
    {
        var task = await TaskAsync(); var target = Path.Combine(root, "selected");
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => plans.ChangeStageDirectoriesAsync(task.Id,
            Input(task, target) with {ExpectedRevision = 0}, null, default));
        Assert.IsFalse(Directory.Exists(target));
    }

    [TestMethod]
    public async Task StageOverlapCannotBeSaved()
    {
        var task = await TaskAsync();
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => plans.ChangeStageDirectoriesAsync(task.Id,
            Input(task, Path.Combine(root, "One", "Download")), null, default));
        Assert.AreEqual(1, task.PlanRevision);
    }

    [DataTestMethod, DataRow(WorkflowRunStatus.Running), DataRow(WorkflowRunStatus.Pending)]
    public async Task ActiveRunCannotMoveFiles(WorkflowRunStatus status)
    {
        var task = await TaskAsync(); var run = new WorkflowRunDbModel {Status = status, StartedAt = DateTime.Now};
        db.WorkflowRuns.Add(run); await db.SaveChangesAsync();
        db.Set<WorkflowRunSourceDbModel>().Add(new() {WorkflowRunId = run.Id, Module = "acquisition", EntityKind = "task", EntityId = task.Id.ToString(), Revision = "1"});
        task.WorkflowRunId = run.Id; await db.SaveChangesAsync();
        var target = Path.Combine(root, "selected");
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => plans.ChangeStageDirectoriesAsync(task.Id, Input(task, target), null, default));
        Assert.IsFalse(Directory.Exists(target));
    }

    [TestMethod]
    public async Task DirectoryReferenceExceptionCannotImpersonateAnotherTask()
    {
        var task = await TaskAsync(); var other = await TaskAsync("Other");
        var run = new WorkflowRunDbModel {Status = WorkflowRunStatus.Success, StartedAt = DateTime.Now}; db.WorkflowRuns.Add(run); await db.SaveChangesAsync();
        db.Set<WorkflowRunSourceDbModel>().Add(new() {WorkflowRunId = run.Id, Module = "acquisition", EntityKind = "task", EntityId = other.Id.ToString(), Revision = "1"});
        await db.SaveChangesAsync();
        var directory = Path.Combine(root, "One", "Processing");
        var candidate = new ArtifactRecord("candidate", "", "files", directory, [], new([], ""), "", ArtifactLocationState.Available, false, [], null);
        Assert.IsTrue(await new ArtifactCleanupReferenceGuard(db).HasReferencesAsync(candidate, [run.Id], default,
            directoryOwnerTaskId: task.Id));
    }

    [DataTestMethod, DataRow("legacy", true), DataRow("null", true), DataRow("valid", true),
     DataRow("invalid", false), DataRow("unknown", false), DataRow("duplicate", false)]
    public void AllocationStorageRootAcceptsOnlyItsOptionalValidExplicitBinding(string variant, bool allowed)
    {
        var scope = new ArtifactDirectoryScope("acquisition", "12", "processing-part", root, Path.Combine(root, "owned"));
        var value = JsonSerializer.Serialize(new AcquisitionStageDirectory(scope.Directory, "exclusive", "taskAllocation")
            {Allocation = scope, BindingId = variant == "valid" ? Guid.NewGuid().ToString("N") : null}, JsonSerializerOptions.Web);
        if (variant == "legacy") value = value.Replace("\"bindingId\":null,", "");
        if (variant == "invalid") value = value.Replace("\"bindingId\":null", "\"bindingId\":\"not-an-identity\"");
        if (variant == "unknown") value = value.Insert(1, "\"unknown\":true,");
        if (variant == "duplicate") value = value.Insert(1, "\"bindingId\":null,");
        using var document = JsonDocument.Parse(value);
        var found = ArtifactDirectoryScopeReferences.AllocationRoot("allocation", document.RootElement.GetProperty("allocation"), document.RootElement);
        Assert.AreEqual(allowed, found.HasValue);
        if (found.HasValue) Assert.AreEqual(scope.StorageRoot, found.Value.GetString());
    }

    private sealed class Storage : IUserStoragePolicy
    {
        public bool IsRestricted => false;
        public IReadOnlyList<UserStorageRoot> GetRoots(UserStoragePurpose purpose = UserStoragePurpose.UserFiles) => [];
        public bool IsPathAllowed(string path, UserStoragePurpose purpose = UserStoragePurpose.UserFiles) => true;
        public void EnsurePathAllowed(string path, UserStoragePurpose purpose = UserStoragePurpose.UserFiles) { }
        public void EnsureTreeMutationAllowed(string path) { }
    }
}
