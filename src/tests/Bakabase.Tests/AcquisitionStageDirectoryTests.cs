using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain;
using Bakabase.Modules.Downloader.Cloud;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Service.Components.Acquisition.Planning;
using Bakabase.Service.Components.CloudDownloads;
using Bakabase.Service.Components.FileProcessing;
using System.Text.Json;

namespace Bakabase.Tests;

[TestClass]
public sealed class AcquisitionStageDirectoryTests
{
    private string root = null!;
    [TestInitialize] public void Setup() => root = Path.Combine(Path.GetTempPath(), "task-stage-directories-" + Guid.NewGuid().ToString("N"));
    [TestCleanup] public void Cleanup() { if (Directory.Exists(root)) Directory.Delete(root, true); }

    private sealed class Storage(Action<string>? inspect = null) : IUserStoragePolicy
    {
        public bool IsRestricted => false;
        public IReadOnlyList<UserStorageRoot> GetRoots(UserStoragePurpose purpose = UserStoragePurpose.UserFiles) => [];
        public bool IsPathAllowed(string path, UserStoragePurpose purpose = UserStoragePurpose.UserFiles) => true;
        public void EnsurePathAllowed(string path, UserStoragePurpose purpose = UserStoragePurpose.UserFiles) => inspect?.Invoke(path);
        public void EnsureTreeMutationAllowed(string path) { }
    }
    private AcquisitionPlan Plan() => new()
    {
        Title = "Example", OutputDirectory = root, StoragePolicy = new() {ProcessingWorkspaceDirectory = root},
        Parts = [new() {Id = "main", SelectedSourceId = "source", Processing = new() {Requirement = "notRequired"},
            Sources = [new() {Id = "source", Url = "https://example.test/file.zip", ProviderId = "http"}]}]
    };

    [TestMethod]
    public void NewTasksSeparateStagesAndTasksWithoutCreatingDirectories()
    {
        var resolver = new AcquisitionDeliveryDirectoryResolver(new Storage());
        var plan = Plan();
        var first = AcquisitionStageDirectoryResolver.Allocate(12, plan, resolver);
        var second = AcquisitionStageDirectoryResolver.Allocate(13, plan, resolver);
        var part = first.Parts["main"];
        Assert.AreEqual(Path.Combine(root, "Downloads", "Example [A12]"), part.Download!.Directory);
        Assert.AreEqual(Path.Combine(root, "Processing", "Example [A12]"), part.Processing!.Directory);
        Assert.AreEqual(Path.Combine(root, "Acquired", "Example [A12]"), part.Delivery!.Directory);
        Assert.AreNotEqual(part.Download.Directory, second.Parts["main"].Download!.Directory);
        Assert.AreEqual("exclusive", part.Download.Ownership);
        Assert.AreEqual("12", part.Download.Allocation!.OwnerId);
        Assert.IsFalse(Directory.Exists(root));
        AcquisitionPlanCompiler.Compile(plan with {StageDirectories = first});
    }

    [TestMethod]
    public void MultipartStageRootsCannotOverlapAndCannotImpersonateOtherTasks()
    {
        var plan = Plan();
        plan = plan with {Parts = [plan.Parts[0], plan.Parts[0] with {Id = "extra", SelectedSourceId = "extra-source",
            Sources = [plan.Parts[0].Sources[0] with {Id = "extra-source"}]}]};
        var directories = AcquisitionStageDirectoryResolver.Allocate(12, plan, new(new Storage()));
        Assert.AreNotEqual(directories.Parts["main"].Download!.Directory, directories.Parts["extra"].Download!.Directory);
        StringAssert.Contains(directories.Parts["extra"].Download!.Directory, "[extra]");
        var original = directories.Parts["main"];
        var bad = directories with {Parts = new(directories.Parts) { ["main"] = original with
        {Download = original.Download! with {Allocation = original.Download.Allocation! with {OwnerId = "13"}}} }};
        Assert.ThrowsExactly<ArgumentException>(() => AcquisitionStageDirectoryResolver.Validate(bad, plan));
    }

    [TestMethod]
    public void LegacyProjectionUsesOnlyRecordedRootsAndPreservesExistingExclusiveBindings()
    {
        var plan = Plan();
        var recorded = Path.Combine(root, "old", "nested", "payload");
        var target = new CloudDownloadTarget {Id = "main", DownloadDirectory = recorded};
        var projection = AcquisitionStageDirectoryResolver.FromRecorded(12, plan, new() {Targets = [target]});
        Assert.AreEqual(recorded, projection.Parts["main"].Download!.Directory);
        Assert.AreEqual("borrowed", projection.Parts["main"].Download!.Ownership);
        Assert.IsNull(projection.Parts["main"].Processing);
        Assert.IsNull(projection.Parts["main"].Download.Allocation);
        var allocated = AcquisitionStageDirectoryResolver.Allocate(12, plan, new(new Storage()));
        plan = plan with {StageDirectories = allocated};
        var owned = allocated.Parts["main"].Download!;
        projection = AcquisitionStageDirectoryResolver.FromRecorded(12, plan,
            new() {Targets = [target with {DownloadDirectory = owned.Directory}]});
        Assert.AreEqual(owned, projection.Parts["main"].Download);
        Assert.IsFalse(Directory.Exists(root));
    }

    [TestMethod]
    public void ChangedPlannedDeliveryDoesNotKeepAnOldUnexecutedLocation()
    {
        var resolver = new AcquisitionDeliveryDirectoryResolver(new Storage());
        var plan = Plan();
        plan = plan with {StageDirectories = AcquisitionStageDirectoryResolver.Allocate(12, plan, resolver),
            DeliveryLayout = resolver.Resolve(12, plan.Title, root, plan.Parts,
                new() {RootDirectory = Path.Combine(root, "new-delivery")})};
        // A legacy unbound projection follows changed planning; current bindings do not.
        plan = plan with {StageDirectories = plan.StageDirectories! with {Parts = new(plan.StageDirectories.Parts)
            {["main"] = plan.StageDirectories.Parts["main"] with {Delivery = plan.StageDirectories.Parts["main"].Delivery! with {BindingId = null}}}}};
        var projected = AcquisitionStageDirectoryResolver.FromRecorded(12, plan, null);
        Assert.AreEqual(plan.DeliveryLayout.PartDirectories["main"], projected.Parts["main"].Delivery!.Directory);
        Assert.AreEqual("exclusive", projected.Parts["main"].Delivery!.Ownership);
    }

    [TestMethod]
    public void NewBindingsAreStableAuthoritiesOverDeepHistoricalOutputs()
    {
        var plan = Plan();
        var allocated = AcquisitionStageDirectoryResolver.Allocate(12, plan, new(new Storage()));
        Assert.IsTrue(Guid.TryParseExact(allocated.Parts["main"].Processing!.BindingId, "N", out _));
        plan = plan with {StageDirectories = allocated};
        var projected = AcquisitionStageDirectoryResolver.FromRecorded(12, plan, new() {Targets =
            [new() {Id = "main", ProcessedDirectory = Path.Combine(root, ".bakabase-work", "plan", "result")} ]});
        Assert.AreEqual(allocated.Parts["main"].Processing, projected.Parts["main"].Processing);
    }

    [TestMethod]
    public void DownloadIdentityIsPrivateForNewTasksAndUnchangedForLegacyTargets()
    {
        var plan = Plan();
        var one = AcquisitionStageDirectoryResolver.Allocate(12, plan, new(new Storage()));
        var two = AcquisitionStageDirectoryResolver.Allocate(13, plan, new(new Storage()));
        var source = new CloudSource {Id = "one", Url = "https://example.test/file.zip", ProviderId = "http"};
        var legacy = new CloudDownloadTarget {Id = "main", Sources = [source], SelectedSourceId = source.Id};
        var original = DownloadArtifactCoordinator.DownloadIntent(legacy, root);
        Assert.AreEqual(original, DownloadArtifactCoordinator.DownloadIntent(legacy with {Id = "other"}, root));
        Assert.AreNotEqual(DownloadArtifactCoordinator.DownloadIntent(legacy with {DownloadDirectoryScope = one.Parts["main"].Download!.Allocation}, root),
            DownloadArtifactCoordinator.DownloadIntent(legacy with {DownloadDirectoryScope = two.Parts["main"].Download!.Allocation}, root));
    }

    [TestMethod]
    public async Task AllocationPreservesCurrentContentsButRejectsForeignOwnersAndMissingStorage()
    {
        Directory.CreateDirectory(root);
        var resolver = new FileProcessingWorkspaceResolver(Path.Combine(root, "app"), new Storage());
        var scope = new ArtifactDirectoryScope("acquisition", "12", "processing-main", root, Path.Combine(root, "task"));
        var directory = await resolver.EnsureScopedDirectoryAsync(scope, default);
        await File.WriteAllTextAsync(Path.Combine(directory, "user-added.txt"), "manual current contents");
        Assert.AreEqual(directory, await resolver.EnsureScopedDirectoryAsync(scope, default));
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => resolver.EnsureScopedDirectoryAsync(scope with {OwnerId = "13"}, default));
        Directory.Delete(directory, true);
        await Assert.ThrowsExactlyAsync<DirectoryNotFoundException>(() => resolver.EnsureScopedDirectoryAsync(scope, default));
        Assert.IsFalse(Directory.Exists(directory));
        var unavailable = scope with {StorageRoot = Path.Combine(root, "offline"), Directory = Path.Combine(root, "offline", "task")};
        await Assert.ThrowsExactlyAsync<DirectoryNotFoundException>(() => resolver.EnsureScopedDirectoryAsync(unavailable, default));
    }

    [TestMethod]
    public async Task AllocationCannotTakeOverAnUnregisteredDirectoryContainingFiles()
    {
        Directory.CreateDirectory(root);
        var directory = Path.Combine(root, "occupied"); Directory.CreateDirectory(directory);
        var file = Path.Combine(directory, "keep.txt"); await File.WriteAllTextAsync(file, "keep");
        var resolver = new FileProcessingWorkspaceResolver(Path.Combine(root, "app"), new Storage());
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => resolver.EnsureScopedDirectoryAsync(
            new("acquisition", "12", "download-main", root, directory), default));
        Assert.AreEqual("keep", await File.ReadAllTextAsync(file));
    }

    [TestMethod]
    public async Task AllocationAcceptsTrailingSeparatorsAndFilesystemRootWithoutChangingIdentity()
    {
        Directory.CreateDirectory(root);
        var scope = new ArtifactDirectoryScope("acquisition", "12", "processing-main", root + Path.DirectorySeparatorChar,
            Path.Combine(root, "task") + Path.DirectorySeparatorChar);
        FileProcessingWorkspaceResolver.ValidateDirectoryScope(scope);
        FileProcessingWorkspaceResolver.ValidateDirectoryScope(scope with {StorageRoot = Path.GetPathRoot(root)!});
        var resolver = new FileProcessingWorkspaceResolver(Path.Combine(root, "app"), new Storage());
        var allocated = await resolver.EnsureScopedDirectoryAsync(scope, default);
        Assert.AreEqual(allocated, await resolver.EnsureScopedDirectoryAsync(scope with
            {StorageRoot = root, Directory = Path.Combine(root, "task")}, default));
    }

    [TestMethod]
    public void LocalRevisionDoesNotProbeHistoricalDownloadStorageWhenAllocatingProcessing()
    {
        var historical = Path.Combine(root, "offline-nas");
        var current = Path.Combine(root, "local-ssd");
        var checkedPaths = new List<string>();
        var resolver = new AcquisitionDeliveryDirectoryResolver(new Storage(path =>
        {
            checkedPaths.Add(path);
            if (path.StartsWith(historical, StringComparison.Ordinal)) throw new InvalidOperationException("Historical storage is unavailable.");
        }));
        var plan = Plan() with {OutputDirectory = historical, StoragePolicy = new() {ProcessingWorkspaceDirectory = current}};
        var processing = AcquisitionStageDirectoryResolver.AllocateProcessingOnly(12, plan, resolver);
        Assert.AreEqual(Path.Combine(current, "Example [A12]"), processing["main"].Directory);
        Assert.IsTrue(checkedPaths.Count > 0);
        Assert.IsFalse(checkedPaths.Any(path => path.StartsWith(historical, StringComparison.Ordinal)));
        Assert.IsFalse(Directory.Exists(root));
    }

    [TestMethod]
    public void AllocationRootsDoNotClaimOtherTasksButConcreteDirectoriesRemainReferences()
    {
        var scope = new ArtifactDirectoryScope("acquisition", "12", "processing-main", root, Path.Combine(root, "task-12"));
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(new {processingDirectoryScope = scope}, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        var value = json.RootElement.GetProperty("processingDirectoryScope");
        var allocation = ArtifactDirectoryScopeReferences.AllocationRoot("processingDirectoryScope", value, json.RootElement);
        Assert.IsNotNull(allocation);
        Assert.AreEqual(root, allocation.Value.GetString());
        Assert.AreNotEqual(value.GetProperty("directory"), allocation.Value);
        Assert.IsNull(ArtifactDirectoryScopeReferences.AllocationRoot("source", value, json.RootElement));
        var unknown = value.GetRawText().TrimEnd('}') + ",\"input\":\"retained\"}";
        using var other = JsonDocument.Parse(unknown);
        Assert.IsNull(ArtifactDirectoryScopeReferences.AllocationRoot("processingDirectoryScope", other.RootElement, json.RootElement));
        Assert.ThrowsExactly<ArgumentException>(() => FileProcessingWorkspaceResolver.ValidateDirectoryScope(scope with {StorageRoot = null!}));
        Assert.ThrowsExactly<ArgumentException>(() => FileProcessingWorkspaceResolver.ValidateDirectoryScope(scope with
            {Directory = Path.Combine(root, "task", "..", "..", "outside")}));
    }
}
