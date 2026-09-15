using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Services;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.InsideWorld.Business.Services;
using Bakabase.Modules.Acquisition.Abstractions.Components;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Acquisition.Components;
using Bakabase.Modules.Acquisition.Components.Steps;
using Bakabase.Modules.Acquisition.Models.Domain;
using Bakabase.Service.Components.Acquisition.Steps;
using Bakabase.TestKit.Utils;
using Bootstrap.Components.Configuration.Abstractions;
using Bootstrap.Components.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bakabase.Tests;

/// <summary>
/// The last three steps: pointing at a folder you already have, filing it under a decent name, and
/// the moment the library learns the resource has files. This is where a recipe stops being about
/// downloads and becomes about the library.
/// </summary>
[TestClass]
public sealed class AcquisitionPlacementTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private IServiceProvider _sp = null!;
    private string _root = null!;
    private string _working = null!;
    private string _library = null!;

    [TestInitialize]
    public async Task Setup()
    {
        _sp = await TestServiceBuilder.BuildServiceProvider();
        _root = Path.Combine(Path.GetTempPath(), $"BakabasePlace_{Guid.NewGuid():N}");
        _working = Path.Combine(_root, "working");
        _library = Path.Combine(_root, "library");
        Directory.CreateDirectory(_working);
        Directory.CreateDirectory(_library);

        var options = _sp.GetRequiredService<IBOptions<AcquisitionOptions>>().Value;

        options.LibraryRootDirectory = _library;
    }

    [TestCleanup]
    public void Cleanup()
    {
        try { Directory.Delete(_root, true); }
        catch { /* best effort */ }
    }

    private AcquisitionStepContext Context(string? configJson = null) => new(
        _sp, NullLogger.Instance, (_, _) => Task.CompletedTask, _working, configJson);

    private static AcquisitionWorkItem Item(string? title = "A Great Work", string workingName = "") => new()
    {
        ResourceId = 1,
        LeadKind = AcquisitionLeadKind.SharedPage,
        LeadValue = "https://example.com/thread/1",
        Title = title,
        WorkingName = workingName,
    };

    private string WithFiles(string directory, params string[] names)
    {
        Directory.CreateDirectory(directory);
        foreach (var n in names)
        {
            var path = Path.Combine(directory, n);

            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, "x");
        }

        return directory;
    }

    // ------- naming -------

    /// <summary>
    /// The template is small on purpose. What matters is that a placeholder resolving to nothing
    /// takes its brackets with it — otherwise every name from a post that omitted the circle ends
    /// with an empty pair of brackets.
    /// </summary>
    [TestMethod]
    public void TheDirectoryNameIsRenderedFromTheTemplate()
    {
        var withCircle = Item() with
        {
            Variables = new Dictionary<string, string> {["Circle"] = "Circle Foo"}
        };

        Assert.AreEqual("A Great Work [Circle Foo]",
            AcquisitionDirectoryNamer.Render("{Title} [{Circle}]", withCircle));

        Assert.AreEqual("A Great Work",
            AcquisitionDirectoryNamer.Render("{Title} [{Circle}]", Item()),
            "the empty brackets go with the value that was not there");

        Assert.AreEqual("A Great Work",
            AcquisitionDirectoryNamer.Render(null, Item()),
            "no template means just the title");
    }

    [TestMethod]
    public void ANameIsMadeSafeAndKeptWithinABudget()
    {
        Assert.AreEqual("A_ Great_ Work_",
            AcquisitionDirectoryNamer.Render("{Title}", Item("A? Great: Work|")),
            "sanitized against the Windows rules wherever this runs");

        var long_ = AcquisitionDirectoryNamer.Render("{Title}", Item(new string('x', 400)));

        Assert.AreEqual(AcquisitionDirectoryNamer.MaxLength, long_.Length);
    }

    [TestMethod]
    public void ANameThatSurvivesNothingStillGetsAFolder()
    {
        // Every filesystem-legal character stripped out of it.
        var name = AcquisitionDirectoryNamer.Render("{Title}", Item("///"));

        Assert.IsFalse(string.IsNullOrWhiteSpace(name));
        Assert.AreEqual(name, Bakabase.Abstractions.Components.FileSystem.FileNameSanitizer.Sanitize(name));
    }

    [DataTestMethod]
    [DataRow("游戏/{LeadKind}/{Title}")]
    [DataRow("游戏\\{LeadKind}\\{Title}")]
    public void TemplateSeparatorsCreateFolders_ButVariableSeparatorsRemainInsideOneName(string template)
    {
        Assert.AreEqual(Path.Combine("游戏", "SharedPage", "A_B_C"),
            AcquisitionDirectoryNamer.Render(template, Item("A/B\\C")));
        Assert.AreEqual(Path.Combine("分类", "A Great Work"),
            AcquisitionDirectoryNamer.Render("分类/{Circle}/{Title}", Item()));
        Assert.AreEqual(Path.Combine("分类", "acquisition-1"),
            AcquisitionDirectoryNamer.Render("分类/{Title}", Item(null)));
    }

    [DataTestMethod]
    [DataRow("../{Title}")]
    [DataRow("分类/../{Title}")]
    [DataRow("分类/./{Title}")]
    [DataRow("分类//{Title}")]
    [DataRow("/{Title}")]
    [DataRow("\\\\server\\share\\{Title}")]
    [DataRow("C:\\{Title}")]
    [DataRow("C:{Title}")]
    public void UnsafeRelativeTemplatesAreRejected(string template)
    {
        Assert.ThrowsException<ArgumentException>(() => AcquisitionDirectoryNamer.Render(template, Item()));
    }

    [TestMethod]
    public void DestinationValidationAcceptsFilesystemRoots_WithoutWritingToThem()
    {
        var root = Path.GetPathRoot(Path.GetFullPath(_library))!;
        var relative = Path.Combine("Games", "A Work");
        Assert.AreEqual(Path.Combine(root, relative),
            AcquisitionDirectoryNamer.ResolveTargetDirectory(root, relative));
        Assert.AreEqual(Path.Combine(_library, relative),
            AcquisitionDirectoryNamer.ResolveTargetDirectory(_library + Path.DirectorySeparatorChar, relative));
        Assert.ThrowsException<ArgumentException>(() =>
            AcquisitionDirectoryNamer.ResolveTargetDirectory(_library, "../outside"));
    }

    // ------- picking a folder -------

    [TestMethod]
    public async Task PickingAFolderSuspends_AndTheAnswerBecomesTheSource()
    {
        var step = new PickLocalDirectoryStep();
        var existing = WithFiles(Path.Combine(_root, "already-here"), "a.txt");

        var outcome = await step.ExecuteAsync(Context(), Item(), CancellationToken.None);
        var suspended = (AcquisitionStepOutcome.Suspend) outcome;

        Assert.AreEqual(AcquisitionWaitReason.PickDirectory, suspended.Reason);

        var resumed = await step.ResumeAsync(Context(), Item(),
            new AcquisitionResumeSignal(AcquisitionWaitReason.PickDirectory,
                JsonSerializer.Serialize(new PickLocalDirectoryStep.DirectorySignal(existing), Json)),
            CancellationToken.None);

        Assert.AreEqual(Path.GetFullPath(existing),
            ((AcquisitionStepOutcome.Continue) resumed).Item.ExtractedDirectory);
    }

    [TestMethod]
    public async Task PickingAFolderThatIsNotThereFails()
    {
        var outcome = await new PickLocalDirectoryStep().ResumeAsync(Context(), Item(),
            new AcquisitionResumeSignal(AcquisitionWaitReason.PickDirectory,
                JsonSerializer.Serialize(new PickLocalDirectoryStep.DirectorySignal("/nowhere/at/all"), Json)),
            CancellationToken.None);

        Assert.IsInstanceOfType<AcquisitionStepOutcome.Fail>(outcome);
    }

    // ------- placing -------

    [TestMethod]
    public async Task PlacingMovesTheFilesUnderTheRenderedName()
    {
        WithFiles(_working, "a.txt", "sub/b.txt");

        var outcome = await new PlaceStep().ExecuteAsync(Context(), Item(), CancellationToken.None);
        var item = ((AcquisitionStepOutcome.Continue) outcome).Item;

        Assert.AreEqual(Path.Combine(_library, "A Great Work"), item.TargetDirectory);
        Assert.IsTrue(File.Exists(Path.Combine(item.TargetDirectory!, "a.txt")));
        Assert.IsTrue(File.Exists(Path.Combine(item.TargetDirectory!, "sub", "b.txt")));
    }

    [TestMethod]
    public async Task PreservedDirectoryTreesCanMoveAcrossLinuxMounts()
    {
        var otherMount = CreateCrossMountTarget();
        try
        {
            var resourceId = await CreateMissingResource();
            WithFiles(_working, "Disc1/Content/a.txt", "Disc2/Content/b.txt", "readme.txt");
            File.WriteAllText(Path.Combine(_working, "Disc1", "Content", "a.txt"), "first payload");
            File.WriteAllText(Path.Combine(_working, "Disc2", "Content", "b.txt"), "second payload");
            Directory.CreateDirectory(Path.Combine(_working, "Disc1", "Empty"));
            var config = JsonSerializer.Serialize(new PlaceStep.Config {LibraryRootDirectory = otherMount}, Json);

            var outcome = await new PlaceStep().ExecuteAsync(Context(config),
                Item() with {ResourceId = resourceId, PreserveDirectoryStructure = true}, CancellationToken.None);

            Assert.IsInstanceOfType<AcquisitionStepOutcome.Continue>(outcome, Describe(outcome));
            var placed = ((AcquisitionStepOutcome.Continue) outcome).Item;
            var target = Path.Combine(otherMount, "A Great Work");
            Assert.AreEqual(target, placed.TargetDirectory);
            Assert.AreEqual("first payload", File.ReadAllText(Path.Combine(target, "Disc1", "Content", "a.txt")));
            Assert.AreEqual("second payload", File.ReadAllText(Path.Combine(target, "Disc2", "Content", "b.txt")));
            Assert.IsTrue(File.Exists(Path.Combine(target, "readme.txt")));
            Assert.IsTrue(Directory.Exists(Path.Combine(target, "Disc1", "Empty")));
            Assert.IsFalse(Directory.EnumerateFileSystemEntries(_working).Any());

            var materialized = await new MaterializeStep().ExecuteAsync(Context(config), placed, CancellationToken.None);
            Assert.IsInstanceOfType<AcquisitionStepOutcome.Continue>(materialized, Describe(materialized));
            Assert.AreEqual(target, (await _sp.GetRequiredService<IResourceService>().Get(resourceId))!.Path);
        }
        finally
        {
            Directory.Delete(otherMount, true);
        }
    }

    [TestMethod]
    public async Task RecursivePlacementMovesDirectoryLinksWithoutMovingTheirTargetFiles()
    {
        if (OperatingSystem.IsWindows()) Assert.Inconclusive("Creating symlinks can require Windows privileges.");
        var linkedDirectory = WithFiles(Path.Combine(_root, "linked-directory"), "keep.txt");
        Directory.CreateSymbolicLink(Path.Combine(_working, "link"), linkedDirectory);
        WithFiles(_working, "regular/a.txt");

        var outcome = await new PlaceStep().ExecuteAsync(Context(),
            Item() with {PreserveDirectoryStructure = true}, CancellationToken.None);

        Assert.IsInstanceOfType<AcquisitionStepOutcome.Continue>(outcome, Describe(outcome));
        var target = ((AcquisitionStepOutcome.Continue) outcome).Item.TargetDirectory!;
        Assert.AreEqual("x", File.ReadAllText(Path.Combine(linkedDirectory, "keep.txt")));
        Assert.AreNotEqual(0, (int) (File.GetAttributes(Path.Combine(target, "link")) & FileAttributes.ReparsePoint));
        Assert.AreEqual("x", File.ReadAllText(Path.Combine(target, "regular", "a.txt")));
    }

    [TestMethod]
    public async Task ADirectoryMoveFailurePreservesUnmovedFilesAndTheExistingTarget()
    {
        WithFiles(_working, "complete/a.txt", "blocked/nested/b.txt");
        var target = Path.Combine(_library, "A Great Work");
        WithFiles(target, "blocked/nested");
        File.WriteAllText(Path.Combine(target, "blocked", "nested"), "existing file");

        var outcome = await new PlaceStep().ExecuteAsync(
            Context(JsonSerializer.Serialize(new PlaceStep.Config {OnConflict = PlacementConflictPolicy.Merge}, Json)),
            Item() with {PreserveDirectoryStructure = true}, CancellationToken.None);

        Assert.IsInstanceOfType<AcquisitionStepOutcome.Fail>(outcome);
        Assert.AreEqual("x", File.ReadAllText(Path.Combine(_working, "blocked", "nested", "b.txt")),
            "a failed subtree must not be deleted from the source");
        Assert.AreEqual("existing file", File.ReadAllText(Path.Combine(target, "blocked", "nested")));
        var original = Path.Combine(_working, "complete", "a.txt");
        var moved = Path.Combine(target, "complete", "a.txt");
        Assert.AreEqual("x", File.ReadAllText(File.Exists(original) ? original : moved),
            "files visited before the failure remain at their source or their completed destination");
    }

    private string CreateCrossMountTarget()
    {
        if (!OperatingSystem.IsLinux() || !Directory.Exists("/dev/shm"))
            Assert.Inconclusive("This regression requires Linux /dev/shm on a different mount from the working folder.");

        var otherMount = Path.Combine("/dev/shm", $"BakabasePlace_{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(otherMount);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Assert.Inconclusive($"The separate mount is not writable: {ex.Message}");
        }

        // Verify the fixture really crosses mounts rather than assuming /dev/shm is separate.
        var probe = Path.Combine(_root, "cross-mount-probe");
        Directory.CreateDirectory(probe);
        try
        {
            Directory.Move(probe, Path.Combine(otherMount, "probe"));
        }
        catch (IOException)
        {
            Directory.Delete(probe);
            return otherMount;
        }

        Directory.Delete(otherMount, true);
        Assert.Inconclusive("Directory.Move succeeded: /dev/shm is not a separate mount in this environment.");
        return otherMount;
    }

    [TestMethod]
    public async Task NestedPlacementMarksOnlyTheResource_AndPreservesTheLegacyRootMarkAndExistingResource()
    {
        var marks = _sp.GetRequiredService<IPathMarkService>();
        var resources = _sp.GetRequiredService<IResourceService>();
        var oldMark = await marks.Add(new Bakabase.Abstractions.Models.Domain.PathMark
        {
            Path = _library,
            Type = PathMarkType.Resource,
            ConfigJson = JsonSerializer.Serialize(new Bakabase.Abstractions.Models.Domain.ResourceMarkConfig
            {
                MatchMode = PathMatchMode.Layer,
                Layer = 1,
                FsTypeFilter = PathFilterFsType.Directory,
            }, Json),
        });
        var oldConfig = oldMark.ConfigJson;
        WithFiles(Path.Combine(_library, "Previous Resource"), "kept.txt");
        var sync = _sp.GetRequiredService<ResourceSyncService>();
        await sync.SyncResources(ResourceSource.PathMark, null, null, new PauseToken(), CancellationToken.None);
        var previous = (await resources.GetAll()).Single();
        var insideId = await CreateMissingResource("Keep this name");
        var insidePath = WithFiles(Path.Combine(_library, "游戏", "Already Have"), "existing.txt");
        await _sp.GetRequiredService<IResourceMaterializationService>().MaterializeAsync(insideId, insidePath);
        var newId = await CreateMissingResource();
        WithFiles(_working, "a.txt", "sub/b.txt");

        var outcome = await new PlaceStep().ExecuteAsync(
            Context("""{"directoryTemplate":"游戏/{LeadKind}/{Title}"}"""),
            Item() with {ResourceId = newId}, CancellationToken.None);
        var placed = ((AcquisitionStepOutcome.Continue) outcome).Item;
        var target = Path.Combine(_library, "游戏", "SharedPage", "A Great Work");
        Assert.AreEqual(target, placed.TargetDirectory);
        Assert.IsTrue(File.Exists(Path.Combine(target, "sub", "b.txt")));
        await new MaterializeStep().ExecuteAsync(Context(), placed, CancellationToken.None);

        // Re-scanning the old broad root must still see the new, already-synced boundary.
        await sync.SyncResources(ResourceSource.PathMark, null, null, new PauseToken(), CancellationToken.None);
        await marks.MarkAsPending(oldMark.Id);
        await sync.SyncResources(ResourceSource.PathMark, null, null, new PauseToken(), CancellationToken.None);
        var all = await resources.GetAll();
        Assert.AreEqual(3, all.Count);
        Assert.IsTrue(all.Any(resource => resource.Id == previous.Id && resource.Path == previous.Path));
        Assert.AreEqual(insidePath.Replace('\\', '/'), (await resources.Get(insideId))!.Path);
        Assert.IsTrue((await _sp.GetRequiredService<IReservedPropertyValueService>()
            .GetAll(value => value.ResourceId == insideId)).Any(value => value.Name == "Keep this name"));
        Assert.AreEqual(target.Replace('\\', '/'), (await resources.Get(newId))!.Path);
        Assert.AreEqual(oldConfig, (await marks.Get(oldMark.Id))!.ConfigJson);
        Assert.IsFalse(all.Any(resource => resource.Path == Path.Combine(_library, "游戏").Replace('\\', '/')));
    }

    [TestMethod]
    public async Task UnsafePlacementDoesNotMoveFilesOrCreateMarks()
    {
        WithFiles(_working, "a.txt");
        var outcome = await new PlaceStep().ExecuteAsync(
            Context("""{"directoryTemplate":"../outside/{Title}"}"""), Item(), CancellationToken.None);

        Assert.IsInstanceOfType<AcquisitionStepOutcome.Fail>(outcome);
        Assert.IsTrue(File.Exists(Path.Combine(_working, "a.txt")));
        Assert.IsFalse(Directory.Exists(Path.Combine(_root, "outside")));
        Assert.AreEqual(0, (await _sp.GetRequiredService<IPathMarkService>().GetAll()).Count);
    }

    [TestMethod]
    public async Task ASymbolicLinkCannotRedirectNestedPlacementOutsideTheLibrary()
    {
        if (OperatingSystem.IsWindows()) Assert.Inconclusive("Creating symlinks can require Windows privileges.");
        var outside = Path.Combine(_root, "outside");
        Directory.CreateDirectory(outside);
        Directory.CreateSymbolicLink(Path.Combine(_library, "linked"), outside);
        WithFiles(_working, "a.txt");

        var outcome = await new PlaceStep().ExecuteAsync(
            Context("""{"directoryTemplate":"linked/{Title}"}"""), Item(), CancellationToken.None);

        Assert.IsInstanceOfType<AcquisitionStepOutcome.Fail>(outcome);
        Assert.IsTrue(File.Exists(Path.Combine(_working, "a.txt")));
        Assert.IsFalse(Directory.EnumerateFileSystemEntries(outside).Any());
    }

    [TestMethod]
    public async Task AnInFlightScanCannotDiscoverANewCategoryUsingItsOldMarkSnapshot()
    {
        var marks = _sp.GetRequiredService<IPathMarkService>();
        await marks.Add(new Bakabase.Abstractions.Models.Domain.PathMark
        {
            Path = _library,
            Type = PathMarkType.Resource,
            ConfigJson = """{"matchMode":1,"layer":1,"fsTypeFilter":2}""",
        });
        WithFiles(_working, "a.txt");
        var snapshotTaken = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowDiscovery = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var sync = _sp.GetRequiredService<ResourceSyncService>();
        var scanning = sync.SyncResources(ResourceSource.PathMark, null, message =>
        {
            if (message?.StartsWith("Discovering filesystem resources", StringComparison.Ordinal) != true)
                return Task.CompletedTask;
            snapshotTaken.TrySetResult();
            return allowDiscovery.Task;
        }, new PauseToken(), CancellationToken.None);
        await snapshotTaken.Task.WaitAsync(TimeSpan.FromSeconds(10));

        var placing = new PlaceStep().ExecuteAsync(
            Context("""{"directoryTemplate":"Games/{Title}"}"""), Item(), CancellationToken.None);
        try
        {
            Assert.IsInstanceOfType<AcquisitionStepOutcome.Continue>(await placing);
            Assert.IsTrue(Directory.Exists(Path.Combine(_library, "Games")));
        }
        finally
        {
            allowDiscovery.TrySetResult();
        }

        await scanning;
        Assert.AreEqual(0, (await _sp.GetRequiredService<IResourceService>().GetAll()).Count,
            "the completed scan must discard the category discovered with its old root mark");
        await marks.MarkAsPending((await marks.GetByPath(_library)).Single().Id);
        await sync.SyncResources(ResourceSource.PathMark, null, null, new PauseToken(), CancellationToken.None);
        var resources = await _sp.GetRequiredService<IResourceService>().GetAll();
        Assert.AreEqual(1, resources.Count);
        Assert.AreEqual(Path.Combine(_library, "Games", "A Great Work").Replace('\\', '/'),
            resources.Single().Path);
    }

    /// <summary>
    /// The item is an ITextWorkpiece so the existing text activities can rewrite the folder name.
    /// Placement must take what they left rather than re-rendering the template over the top.
    /// </summary>
    [TestMethod]
    public async Task ANameAlreadySetByATextActivityWins()
    {
        WithFiles(_working, "a.txt");

        var renamed = (AcquisitionWorkItem) ((ITextWorkpiece) Item()).WithWorkingText("Cleaned Up Name");
        var outcome = await new PlaceStep().ExecuteAsync(Context(), renamed, CancellationToken.None);

        Assert.AreEqual(Path.Combine(_library, "Cleaned Up Name"),
            ((AcquisitionStepOutcome.Continue) outcome).Item.TargetDirectory);
    }

    /// <summary>
    /// An archive holding one folder leaves a folder inside a folder. Filing that would put a shell
    /// named after the download in the library.
    /// </summary>
    [TestMethod]
    public async Task ASingleWrapperFolderIsCollapsed()
    {
        WithFiles(Path.Combine(_working, "Some.Release.2024"), "a.txt", "b.txt");

        var outcome = await new PlaceStep().ExecuteAsync(Context(), Item(), CancellationToken.None);
        var target = ((AcquisitionStepOutcome.Continue) outcome).Item.TargetDirectory!;

        Assert.IsTrue(File.Exists(Path.Combine(target, "a.txt")),
            "the contents are at the top, not under a folder named after the download");
    }

    [TestMethod]
    public async Task AnExistingFolderSuspendsByDefault()
    {
        WithFiles(_working, "a.txt");
        WithFiles(Path.Combine(_library, "A Great Work"), "already-here.txt");

        var outcome = await new PlaceStep().ExecuteAsync(Context(), Item(), CancellationToken.None);
        var suspended = (AcquisitionStepOutcome.Suspend) outcome;

        Assert.AreEqual(AcquisitionWaitReason.TargetExists, suspended.Reason);

        var prompt = JsonSerializer.Deserialize<PlaceStep.Prompt>(suspended.PromptJson!, Json)!;

        Assert.AreEqual(1, prompt.ExistingEntryCount);
        Assert.IsTrue(File.Exists(Path.Combine(_library, "A Great Work", "already-here.txt")),
            "and nothing was touched while it waits");
    }

    [TestMethod]
    public async Task TheThreeConflictAnswersDoWhatTheySay()
    {
        WithFiles(_working, "a.txt");
        WithFiles(Path.Combine(_library, "A Great Work"), "already-here.txt");

        // Rename: beside the existing one.
        var renamed = await new PlaceStep().ResumeAsync(Context(), Item(),
            new AcquisitionResumeSignal(AcquisitionWaitReason.TargetExists,
                JsonSerializer.Serialize(new PlaceStep.ConflictSignal(PlacementConflictPolicy.Rename, null), Json)),
            CancellationToken.None);

        Assert.AreEqual(Path.Combine(_library, "A Great Work (2)"),
            ((AcquisitionStepOutcome.Continue) renamed).Item.TargetDirectory);

        // Merge: into the existing one, keeping what was there.
        WithFiles(_working, "b.txt");
        var merged = await new PlaceStep().ResumeAsync(Context(), Item(),
            new AcquisitionResumeSignal(AcquisitionWaitReason.TargetExists,
                JsonSerializer.Serialize(new PlaceStep.ConflictSignal(PlacementConflictPolicy.Merge, null), Json)),
            CancellationToken.None);

        var mergedTarget = ((AcquisitionStepOutcome.Continue) merged).Item.TargetDirectory!;

        Assert.AreEqual(Path.Combine(_library, "A Great Work"), mergedTarget);
        Assert.IsTrue(File.Exists(Path.Combine(mergedTarget, "already-here.txt")));
        Assert.IsTrue(File.Exists(Path.Combine(mergedTarget, "b.txt")));

        // A different name: the user picked one, and it is used as given.
        WithFiles(_working, "c.txt");
        var renamedTo = await new PlaceStep().ResumeAsync(Context(), Item(),
            new AcquisitionResumeSignal(AcquisitionWaitReason.TargetExists,
                JsonSerializer.Serialize(
                    new PlaceStep.ConflictSignal(PlacementConflictPolicy.Merge, "Somewhere Else"), Json)),
            CancellationToken.None);

        Assert.AreEqual(Path.Combine(_library, "Somewhere Else"),
            ((AcquisitionStepOutcome.Continue) renamedTo).Item.TargetDirectory);
    }

    [TestMethod]
    public async Task PlacingWithNoLibraryFolderSaysSo()
    {
        _sp.GetRequiredService<IBOptions<AcquisitionOptions>>().Value.LibraryRootDirectory = null;
        WithFiles(_working, "a.txt");

        Assert.IsInstanceOfType<AcquisitionStepOutcome.Fail>(
            await new PlaceStep().ExecuteAsync(Context(), Item(), CancellationToken.None));
    }

    // ------- materializing -------

    private async Task<int> CreateMissingResource(string name = "Placeholder name") =>
        (await _sp.GetRequiredService<IPlaceholderResourceService>().CreateByTitle(name)).ResourceId;

    /// <summary>
    /// The whole point: a resource that had no files has them, and the library knows it.
    /// </summary>
    [TestMethod]
    public async Task MaterializingPointsTheResourceAtWhatWasPlaced()
    {
        var resourceId = await CreateMissingResource();
        var target = WithFiles(Path.Combine(_library, "A Great Work"), "a.txt");

        var outcome = await new MaterializeStep().ExecuteAsync(Context(),
            Item() with {ResourceId = resourceId, TargetDirectory = target}, CancellationToken.None);

        Assert.IsInstanceOfType<AcquisitionStepOutcome.Continue>(outcome, Describe(outcome));

        var resource = (await _sp.GetRequiredService<IResourceService>().Get(resourceId))!;

        Assert.IsTrue(resource.HasLocalPath);
        Assert.AreEqual(target, resource.Path);
    }

    [TestMethod]
    public async Task MaterializingWithNothingPlacedFailsRatherThanPointingAtNothing()
    {
        var outcome = await new MaterializeStep().ExecuteAsync(Context(),
            Item() with {ResourceId = await CreateMissingResource()}, CancellationToken.None);

        Assert.IsInstanceOfType<AcquisitionStepOutcome.Fail>(outcome);
    }

    [TestMethod]
    public async Task MaterializingClearsTheWorkingDirectory()
    {
        var resourceId = await CreateMissingResource();
        var target = WithFiles(Path.Combine(_library, "A Great Work"), "a.txt");

        WithFiles(_working, "leftover.tmp");

        await new MaterializeStep().ExecuteAsync(Context(),
            Item() with {ResourceId = resourceId, TargetDirectory = target}, CancellationToken.None);

        Assert.IsFalse(Directory.Exists(_working));
    }

    private static string Describe(AcquisitionStepOutcome outcome) => outcome switch
    {
        AcquisitionStepOutcome.Fail f => $"failed: {f.Message}",
        AcquisitionStepOutcome.Skip s => $"skipped: {s.Why}",
        AcquisitionStepOutcome.Suspend sp => $"suspended: {sp.Reason}",
        _ => outcome.ToString() ?? "",
    };
}
