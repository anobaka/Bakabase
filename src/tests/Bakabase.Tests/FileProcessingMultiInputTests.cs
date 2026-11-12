using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Bakabase.InsideWorld.Business.Components.Compression;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Service.Components.FileProcessing;

namespace Bakabase.Tests;

[TestClass]
public sealed class FileProcessingMultiInputTests
{
    private string _root = null!;
    private string _input = null!;
    private string _state = null!;
    private static readonly byte[] RarSignature = [0x52, 0x61, 0x72, 0x21, 0x1a, 0x07, 0x01, 0];
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [TestInitialize]
    public void Initialize()
    {
        _root = Path.Combine(Path.GetTempPath(), "multi-input-plan-" + Guid.NewGuid().ToString("N"));
        _input = Path.Combine(_root, "downloads");
        _state = Path.Combine(_root, "state");
        Directory.CreateDirectory(_input);
    }

    [TestCleanup]
    public void Cleanup() => Directory.Delete(_root, true);

    private async Task<string> Input(string relative)
    {
        var file = Path.Combine(_input, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        await File.WriteAllBytesAsync(file, RarSignature);
        return file;
    }

    private static FileProcessingPlan Plan() => new()
    {
        Requirement = "required", Steps =
        [
            new() {Id = "unwrap", Op = "extractArchive", Selector = "wrapper.rar"},
            new() {Id = "place", Op = "moveFile", Input = "unwrap", TargetDirectory = "bundle"},
            new() {Id = "open", Op = "extractArchive", Inputs = ["download", "place"], Selector = "family.part*.r删除ar"}
        ]
    };

    private async Task<List<string>> Inputs() =>
    [await Input("bundle/wrapper.rar"), await Input("bundle/family.part2.r删除ar"),
        await Input("bundle/family.part3.r删除ar"), await Input("bundle/family.part4.r删除ar")];

    [TestMethod]
    public async Task ExplicitWrapperThenManifestUnionExtractsCompleteFamilyAndPreservesOriginals()
    {
        var files = await Inputs();
        await Input("bundle/family.part5.r删除ar"); // present on disk, deliberately outside the manifest
        var note = Path.Combine(_input, "note.txt");
        await File.WriteAllTextAsync(note, "keep this helper");
        files.Add(note);
        var archives = new Archives();
        var result = await new FileProcessingPlanExecutor(archives).ExecuteAsync(Plan(), _input, files, _state);

        Assert.IsTrue(result.Completed, result.Message);
        Assert.AreEqual(1, archives.WrapperExtractions);
        Assert.AreEqual(1, archives.VolumeExtractions);
        CollectionAssert.AreEquivalent(new[] {"payload.txt", "note.txt"}, result.Files.Select(Path.GetFileName).ToArray());
        Assert.IsTrue(result.ExtractedFiles!.All(f => Path.GetFileName(f) == "payload.txt"));
        Assert.IsFalse(result.Files.Any(FileProcessingFiles.IsVolume));
        foreach (var file in files.Take(4)) CollectionAssert.AreEqual(RarSignature, await File.ReadAllBytesAsync(file));
        Assert.IsFalse(File.Exists(Path.Combine(_input, "bundle/family.part1.r删除ar")));
        Assert.IsTrue(File.Exists(Path.Combine(_input, "bundle/family.part5.r删除ar")));
    }

    [TestMethod]
    public async Task NodeBoundariesAndPasswordResumeRetainCompletedWrapperAndMergedManifest()
    {
        var files = await Inputs();
        var archives = new Archives {NeedsVolumePassword = true};
        var executor = new FileProcessingPlanExecutor(archives);
        var plan = Plan();
        Assert.IsTrue((await executor.ExecuteAsync(plan, _input, files, _state, stopAfterStepId: "unwrap")).Completed);
        Assert.IsTrue((await executor.ExecuteAsync(plan, _input, files, _state, stopAfterStepId: "place")).Completed);
        var waiting = await executor.ExecuteAsync(plan, _input, files, _state, stopAfterStepId: "open");
        Assert.IsFalse(waiting.Completed);
        Assert.IsTrue(waiting.NeedsPassword);
        Assert.AreEqual("open", waiting.StepId);
        Assert.AreEqual(1, archives.WrapperExtractions);
        Assert.AreEqual(0, archives.VolumeExtractions);

        var completed = await new FileProcessingPlanExecutor(archives).ExecuteAsync(plan, _input, files, _state,
            password: "accepted", stopAfterStepId: "open");
        Assert.IsTrue(completed.Completed, completed.Message);
        Assert.AreEqual(1, archives.WrapperExtractions, "A resumed step must reuse the committed wrapper output.");
        Assert.AreEqual(1, archives.VolumeExtractions);
        Assert.AreEqual("payload.txt", Path.GetFileName(completed.Files.Single()));
        Assert.IsTrue((await executor.ExecuteAsync(plan, _input, files, _state)).Completed);
        Assert.AreEqual(1, archives.VolumeExtractions);
    }

    [TestMethod]
    public async Task MissingDeclaredVolumeIsNotFilledFromOriginalDirectory()
    {
        var files = await Inputs();
        files.RemoveAt(2); // part 3 exists beside the originals but is not a declared input
        var archives = new Archives();
        var error = await Assert.ThrowsExceptionAsync<InvalidOperationException>(() =>
            new FileProcessingPlanExecutor(archives).ExecuteAsync(Plan(), _input, files, _state));
        StringAssert.Contains(error.Message, "missing parts");
        Assert.AreEqual(1, archives.WrapperExtractions);
        Assert.AreEqual(0, archives.VolumeExtractions);
    }

    [TestMethod]
    public async Task UnrelatedIncompleteFamilyDoesNotPreventExplicitWrapperStep()
    {
        var files = await Inputs();
        var archives = new Archives();
        var result = await new FileProcessingPlanExecutor(archives).ExecuteAsync(Plan(), _input, files, _state,
            stopAfterStepId: "unwrap");
        Assert.IsTrue(result.Completed, result.Message);
        Assert.AreEqual(1, archives.WrapperExtractions);
        Assert.AreEqual(0, archives.VolumeExtractions);
    }

    [TestMethod]
    public async Task CollidingRelativePathsAcrossDeclaredManifestsAreRejected()
    {
        var file = await Input("wrapper.rar");
        var plan = new FileProcessingPlan {Requirement = "required", Steps =
        [new() {Id = "copy", Op = "moveFile", TargetDirectory = "."},
            new() {Id = "open", Op = "extractArchive", Inputs = ["download", "copy"]}]};
        var archives = new Archives();
        var result = await new FileProcessingPlanExecutor(archives).ExecuteAsync(plan, _input, [file], _state);
        Assert.IsFalse(result.Completed);
        Assert.AreEqual("ProcessingPathConflict", result.FailureCode);
        Assert.AreEqual("open", result.StepId);
        Assert.AreEqual("PlannedTargetCollision", result.PathConflict?.Kind);
        Assert.AreEqual("wrapper.rar", result.PathConflict?.TargetPath);
        Assert.AreEqual(2, result.PathConflict?.Entries.Count);
        Assert.AreEqual(0, archives.WrapperExtractions);
        Assert.AreEqual(0, archives.VolumeExtractions);
        CollectionAssert.AreEqual(RarSignature, await File.ReadAllBytesAsync(file));
    }

    [TestMethod]
    public async Task ResumeRejectsUnexpectedFilesAddedToTheMergedSnapshot()
    {
        var files = await Inputs();
        var archives = new Archives {NeedsVolumePassword = true};
        var executor = new FileProcessingPlanExecutor(archives);
        Assert.IsFalse((await executor.ExecuteAsync(Plan(), _input, files, _state)).Completed);
        var merged = Directory.GetDirectories(_state, "merged-inputs", SearchOption.AllDirectories).Single();
        var stagedFirst = Directory.GetFiles(merged, "family.part1.r删除ar", SearchOption.AllDirectories).Single();
        await File.WriteAllBytesAsync(Path.Combine(Path.GetDirectoryName(stagedFirst)!, "family.part5.r删除ar"), RarSignature);
        var error = await Assert.ThrowsExceptionAsync<InvalidOperationException>(() =>
            executor.ExecuteAsync(Plan(), _input, files, _state, password: "accepted"));
        StringAssert.Contains(error.Message, "declared files");
        Assert.AreEqual(0, archives.VolumeExtractions);
    }

    [TestMethod]
    public async Task MultiInputStagingRejectsSymbolicLinksInAnEarlierOutput()
    {
        var files = await Inputs();
        var archives = new Archives();
        var executor = new FileProcessingPlanExecutor(archives);
        Assert.IsTrue((await executor.ExecuteAsync(Plan(), _input, files, _state, stopAfterStepId: "place")).Completed);
        var placed = Directory.GetFiles(_state, "family.part1.r删除ar", SearchOption.AllDirectories)
            .Single(f => Path.GetDirectoryName(Path.GetDirectoryName(f)!)!.EndsWith("-rename", StringComparison.Ordinal));
        File.Delete(placed);
        File.CreateSymbolicLink(placed, files[0]);
        await Assert.ThrowsExceptionAsync<InvalidOperationException>(() =>
            executor.ExecuteAsync(Plan(), _input, files, _state));
        Assert.AreEqual(0, archives.VolumeExtractions);
    }

    [TestMethod]
    public void InputsRequireNonEmptyUniqueExistingReferencesAndOverrideLegacyInput()
    {
        var plan = Plan();
        FileProcessingPlan Alter(List<string>? inputs) => plan with
        {Steps = [plan.Steps[0], plan.Steps[1], plan.Steps[2] with {Input = "ignored-legacy", Inputs = inputs}]};
        FileProcessingPlanValidator.Validate(Alter(["download", "place"]));
        foreach (var invalid in new List<string>?[] {[], ["place", "place"], ["open"], ["future"], [null!], null})
            Assert.ThrowsException<InvalidOperationException>(() => FileProcessingPlanValidator.Validate(Alter(invalid)));
    }

    [TestMethod]
    public void LegacyPlanSerializationAndIntentStayIdenticalWhileInputsAffectNewIntents()
    {
        var legacyStep = new FileProcessingStep {Id = "open", Op = "extractArchive"};
        Assert.AreEqual("{\"id\":\"open\",\"op\":\"extractArchive\",\"input\":\"download\",\"selector\":null,\"extension\":null,\"password\":null}",
            JsonSerializer.Serialize(legacyStep, Json));
        Assert.IsFalse(Newtonsoft.Json.JsonConvert.SerializeObject(legacyStep).Contains("Inputs"));
        var plan = new FileProcessingPlan {Requirement = "required", Steps = [legacyStep]};
        var config = new FileArtifactProcessingConfig();
        var oldInstructions = new {plan.Requirement, Steps = plan.Steps.Select(s => new
            {s.Id, s.Op, s.Input, s.Selector, s.Extension, s.TargetName, s.TargetDirectory, s.Password})};
        var previous = "files-v2:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
        {
            Version = "file-artifacts-v3", Input = "source", Instructions = oldInstructions, config.ArchivePolicy,
            Recursive = config.Recursive.Enabled, Password = (string?)null, Force = (string?)null
        }, Json)))).ToLowerInvariant();
        string Intent(FileProcessingPlan p) => FileArtifactProcessor.Intent("source", JsonSerializer.Serialize(p, Json), null, config);
        Assert.AreEqual(previous, Intent(plan));
        var multi = Plan();
        Assert.AreNotEqual(Intent(multi), Intent(multi with
            {Steps = [multi.Steps[0], multi.Steps[1], multi.Steps[2] with {Inputs = ["place"]}]}));
        Assert.AreEqual(Intent(multi), Intent(multi with
            {Steps = [multi.Steps[0], multi.Steps[1], multi.Steps[2] with {Input = "irrelevant"}]}));
    }

    private sealed class Archives : IArchiveExtractionService
    {
        public int WrapperExtractions { get; private set; }
        public int VolumeExtractions { get; private set; }
        public bool NeedsVolumePassword { get; init; }
        public Task<ArchivePasswordProbe> ProbePasswordAsync(string entryFile, IReadOnlyList<string?> candidates,
            Action<string>? onOutput, CancellationToken ct)
        {
            var needsPassword = NeedsVolumePassword && Path.GetFileName(entryFile) != "wrapper.rar";
            return Task.FromResult(new ArchivePasswordProbe(!needsPassword || candidates.Contains("accepted"),
                needsPassword ? "accepted" : null, [], "", "Password required."));
        }
        public async Task<ArchiveExtractionResult> ExtractAsync(ArchiveExtractionRequest request,
            Action<int>? onProgress, CancellationToken ct)
        {
            if (Path.GetFileName(request.Files[0]) == "wrapper.rar")
            {
                WrapperExtractions++;
                Assert.AreEqual(1, request.Files.Count, "An explicit wrapper selector excludes later incomplete volumes.");
                await File.WriteAllBytesAsync(Path.Combine(request.Directory, "family.part1.r删除ar"), RarSignature, ct);
            }
            else
            {
                VolumeExtractions++;
                Assert.AreEqual(4, request.Files.Count);
                Assert.AreEqual(1, request.Files.Select(Path.GetDirectoryName).Distinct().Count());
                CollectionAssert.AreEquivalent(Enumerable.Range(1, 4).Select(i => $"family.part{i}.r删除ar").ToArray(),
                    Directory.GetFiles(Path.GetDirectoryName(request.Files[0])!).Select(Path.GetFileName).ToArray());
                await File.WriteAllTextAsync(Path.Combine(request.Directory, "payload.txt"), "final content", ct);
            }
            return new(true, request.Directory, "");
        }
    }
}
