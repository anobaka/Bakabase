using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Services;
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
