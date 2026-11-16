using System.Text.Json;
using Bakabase.InsideWorld.Business.Components.Compression;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Service.Components.FileProcessing;

namespace Bakabase.Tests;

[TestClass]
public sealed class FileProcessingLiteralRenameTests
{
    private string _root = null!;
    private string _input = null!;
    private string _state = null!;
    [TestInitialize] public void Initialize()
    {
        _root = Path.Combine(Path.GetTempPath(), "literal-rename-" + Guid.NewGuid().ToString("N"));
        _input = Path.Combine(_root, "input"); _state = Path.Combine(_root, "state");
        Directory.CreateDirectory(_input);
    }
    [TestCleanup] public void Cleanup() => Directory.Delete(_root, true);

    [TestMethod]
    public async Task AllLiteralOccurrencesChangeOnlyBasenameAndLeaveOriginalsAndUnselectedFilesIntact()
    {
        var source = await Input("[remove]/pack[remove][remove].txt");
        var unaffected = await Input("[remove]/PACK[REMOVE].txt");
        var outside = await Input("outside[remove].txt");
        var plan = Plan("[remove]", "", "[remove]/*");
        var result = await new FileProcessingPlanExecutor(new Archives()).ExecuteAsync(plan, _input,
            [source, unaffected], _state);
        Assert.IsTrue(result.Completed, result.Message);
        CollectionAssert.AreEquivalent(new[] {"[remove]/pack.txt", "[remove]/PACK[REMOVE].txt"},
            result.Files.Select(f => Path.GetRelativePath(result.OutputDirectory!, f).Replace('\\', '/')).ToArray());
        foreach (var original in new[] {source, unaffected, outside}) Assert.AreEqual("original", await File.ReadAllTextAsync(original));
    }

    [DataTestMethod]
    [DataRow("original")]
    [DataRow("distinct")]
    public async Task CollisionWithUnselectedManifestEntryFailsBeforeWritingOutputs(string existingContent)
    {
        var source = await Input("remove-name.txt");
        var existing = await Input("name.txt", existingContent);
        var result = await new FileProcessingPlanExecutor(new Archives())
            .ExecuteAsync(Plan("remove-", "", "remove-name.txt"), _input, [source, existing], _state);

        Assert.IsFalse(result.Completed);
        Assert.AreEqual("ProcessingPathConflict", result.FailureCode);
        Assert.IsNotNull(result.PathConflict);
        Assert.AreEqual("PlannedTargetCollision", result.PathConflict.Kind);
        Assert.AreEqual("rename", result.PathConflict.StepId);
        Assert.AreEqual("name.txt", result.PathConflict.TargetPath);
        CollectionAssert.AreEquivalent(new[] {"remove-name.txt", "name.txt"},
            result.PathConflict.Entries.Select(entry => entry.SourcePath).ToArray());
        Assert.AreEqual("original", await File.ReadAllTextAsync(source));
        Assert.AreEqual(existingContent, await File.ReadAllTextAsync(existing));
        Assert.IsFalse(Directory.Exists(_state));
    }

    [DataTestMethod]
    [DataRow("discard", "discard", "")]
    [DataRow("safe.txt", "safe", "CON")]
    [DataRow("safe.txt", "txt", "")]
    public async Task UnsafeResultNameIsRejectedWithoutChangingTheOriginal(string name, string find, string replacement)
    {
        var source = await Input(name);
        await Assert.ThrowsAsync<InvalidOperationException>(() => new FileProcessingPlanExecutor(new Archives())
            .ExecuteAsync(Plan(find, replacement), _input, [source], _state));
        Assert.AreEqual("original", await File.ReadAllTextAsync(source));
    }

    [TestMethod]
    public async Task PasswordResumeKeepsCompletedLiteralRenameAndOriginalArchive()
    {
        var source = await Input("pack.删除zip");
        var archives = new Archives(); var executor = new FileProcessingPlanExecutor(archives);
        var plan = Plan("删除", "") with {Steps = [Plan("删除", "").Steps[0],
            new() {Id = "open", Op = "extractArchive", Input = "rename"}]};
        var waiting = await executor.ExecuteAsync(plan, _input, [source], _state);
        Assert.IsTrue(waiting.NeedsPassword); Assert.AreEqual(0, archives.Extractions);
        var stateFile = Directory.GetFiles(_state, "state.json", SearchOption.AllDirectories).Single();
        var checkpoint = JsonSerializer.Deserialize<FileProcessingPlanExecutor.State>(await File.ReadAllTextAsync(stateFile),
            new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        var renamed = checkpoint.Outputs["rename"].Single().Path;
        var renamedTime = File.GetLastWriteTimeUtc(renamed);
        var done = await executor.ExecuteAsync(plan, _input, [source], _state, password: "accepted");
        Assert.IsTrue(done.Completed, done.Message); Assert.AreEqual(1, archives.Extractions);
        Assert.AreEqual(renamedTime, File.GetLastWriteTimeUtc(renamed));
        Assert.AreEqual("payload", await File.ReadAllTextAsync(done.Files.Single()));
        Assert.AreEqual("original", await File.ReadAllTextAsync(source));
    }

    [TestMethod]
    public void LiteralIntentIncludesBothTextsAndEffectiveInputsWithoutChangingLegacyOperationShape()
    {
        string Intent(FileProcessingPlan plan) => FileArtifactProcessor.Intent("source", JsonSerializer.Serialize(plan), null, new());
        var plan = Plan("remove", "");
        Assert.AreNotEqual(Intent(plan), Intent(Plan("other", "")));
        Assert.AreNotEqual(Intent(plan), Intent(Plan("remove", "replacement")));
        Assert.AreEqual(Intent(plan), Intent(plan with {Steps = [plan.Steps[0] with {Input = "ignored", Inputs = ["download"]}]}));
        var legacy = new FileProcessingPlan {Requirement = "required", Steps = [new() {Id = "open", Op = "extractArchive"}]};
        Assert.AreEqual(Intent(legacy), Intent(legacy with {Steps = [legacy.Steps[0] with {FindText = "irrelevant", ReplaceText = ""}]}));
    }

    private static FileProcessingPlan Plan(string find, string replacement, string? selector = null) => new()
    {Requirement = "required", Steps = [new() {Id = "rename", Op = "renameLiteral", FindText = find, ReplaceText = replacement, Selector = selector}]};
    private async Task<string> Input(string relative, string content = "original")
    {
        var file = Path.Combine(_input, relative); Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        await File.WriteAllTextAsync(file, content); return file;
    }
    private sealed class Archives : IArchiveExtractionService
    {
        public int Extractions;
        public Task<ArchivePasswordProbe> ProbePasswordAsync(string file, IReadOnlyList<string?> candidates, Action<string>? output, CancellationToken ct) =>
            Task.FromResult(new ArchivePasswordProbe(candidates.Contains("accepted"), "accepted", [], "", ""));
        public async Task<ArchiveExtractionResult> ExtractAsync(ArchiveExtractionRequest request, Action<int>? progress, CancellationToken ct)
        {
            Extractions++; Directory.CreateDirectory(request.Directory);
            await File.WriteAllTextAsync(Path.Combine(request.Directory, "payload.txt"), "payload", ct);
            return new(true, request.Directory, "");
        }
    }
}
