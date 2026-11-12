using System.Text.Json;
using Bakabase.InsideWorld.Business.Components.Compression;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Service.Components.FileProcessing;

namespace Bakabase.Tests.Artifacts;

[TestClass]
public sealed class FileProcessingArchiveGroupRecoveryTests
{
    private string root = null!;
    private string input = null!;
    private string checkpoint = null!;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static FileProcessingPlan Plan() => new()
        {Requirement = "required", Steps = [new() {Id = "extract", Op = "extractArchive"}]};

    [TestInitialize]
    public void Initialize()
    {
        root = Path.Combine(Path.GetTempPath(), "archive-group-recovery-" + Guid.NewGuid().ToString("N"));
        input = Path.Combine(root, "input"); checkpoint = Path.Combine(root, "checkpoint");
        Directory.CreateDirectory(input);
    }

    [TestCleanup] public void Cleanup() { if (Directory.Exists(root)) Directory.Delete(root, true); }

    private async Task<string[]> Inputs(params string[] names)
    {
        foreach (var name in names) await File.WriteAllTextAsync(Path.Combine(input, name), name);
        return names.Select(name => Path.Combine(input, name)).ToArray();
    }

    private async Task<(string Path, FileProcessingPlanExecutor.State State)> State()
    {
        var path = Directory.GetFiles(checkpoint, "state.json", SearchOption.AllDirectories).Single();
        return (path, JsonSerializer.Deserialize<FileProcessingPlanExecutor.State>(await File.ReadAllTextAsync(path), Json)!);
    }

    private static Task Save(string path, FileProcessingPlanExecutor.State state) =>
        File.WriteAllTextAsync(path, JsonSerializer.Serialize(state, Json));

    [TestMethod]
    public async Task New_partial_retry_binds_each_dotted_archive_and_does_not_repeat_completed_extraction()
    {
        var files = await Inputs("foo.v1.zip", "foo.v2.zip"); var archives = new Archives {LockedName = "foo.v2.zip"};
        var executor = new FileProcessingPlanExecutor(archives);
        Assert.IsTrue((await executor.ExecuteAsync(Plan(), input, files, checkpoint)).NeedsPassword);
        var (_, saved) = await State();
        Assert.IsTrue(saved.Operations.Values.All(o => o.ArchiveInputs is {Count: 1}));
        var result = await executor.ExecuteAsync(Plan(), input, files, checkpoint, password: "ok");
        Assert.IsTrue(result.Completed, result.Message);
        CollectionAssert.AreEquivalent(new[] {"foo.v1.zip", "foo.v2.zip"}, archives.Extracted.ToArray());
        Assert.AreEqual(2, result.FileLineage!.Entries.Count);
        Assert.IsTrue(result.FileLineage.Entries.All(e => e.Replaced));
    }

    [TestMethod]
    public async Task Legacy_completed_group_with_exact_transition_proof_reuses_its_output()
    {
        var files = await Inputs("a.zip", "z.zip"); var archives = new Archives();
        var executor = new FileProcessingPlanExecutor(archives);
        Assert.IsTrue((await executor.ExecuteAsync(Plan(), input, files, checkpoint)).NeedsPassword);
        var (path, saved) = await State();
        saved.Operations.Values.Single(o => o.Completed).ArchiveInputs = null;
        await Save(path, saved);
        var result = await executor.ExecuteAsync(Plan(), input, files, checkpoint, password: "ok");
        Assert.IsTrue(result.Completed, result.Message);
        CollectionAssert.AreEquivalent(new[] {"a.zip", "z.zip"}, archives.Extracted.ToArray());
        Assert.IsTrue((await State()).State.Operations.Values.All(o => o.ArchiveInputs is {Count: 1}));
    }

    [TestMethod]
    public async Task Legacy_merged_group_cannot_claim_its_output_for_one_new_independent_archive()
    {
        var files = await Inputs("foo.v1.zip", "foo.v2.zip"); var archives = new Archives {LockedName = "foo.v2.zip"};
        var executor = new FileProcessingPlanExecutor(archives);
        await executor.ExecuteAsync(Plan(), input, files, checkpoint);
        var (path, saved) = await State();
        var completed = saved.Operations.Values.Single(o => o.Completed);
        var firstKey = completed.ArchiveInputs!.Single();
        var pendingKey = saved.Operations[saved.PendingOperation!].ArchiveInputs!.Single();
        completed.ArchiveInputs = null;
        // A pre-fix merged group recorded both input origins against just the first archive's output.
        saved.FileTransitions[pendingKey] = [..saved.FileTransitions[firstKey]];
        saved.Operations.Remove(saved.PendingOperation!);
        saved.PendingOperation = null;
        await Save(path, saved);
        var before = await File.ReadAllBytesAsync(path);
        var result = await executor.ExecuteAsync(Plan(), input, files, checkpoint);
        Assert.AreEqual("ArchiveGroupInputChanged", result.FailureCode);
        Assert.IsFalse(result.Completed);
        Assert.AreEqual(1, archives.Extracted.Count);
        CollectionAssert.AreEqual(before, await File.ReadAllBytesAsync(path));
        Assert.IsTrue(Directory.Exists(completed.OutputDirectory));
        Assert.IsTrue(files.All(File.Exists));
    }

    [TestMethod]
    public async Task Legacy_index_shift_is_rejected_before_any_cached_group_is_consumed()
    {
        var files = await Inputs("a.zip", "b.zip", "z.zip"); var archives = new Archives();
        var executor = new FileProcessingPlanExecutor(archives);
        await executor.ExecuteAsync(Plan(), input, files, checkpoint);
        var (path, saved) = await State();
        var completed = saved.Operations.Where(p => p.Value.Completed).OrderBy(p => p.Key).ToArray();
        foreach (var pair in completed) pair.Value.ArchiveInputs = null;
        (saved.Operations[completed[0].Key], saved.Operations[completed[1].Key]) = (completed[1].Value, completed[0].Value);
        await Save(path, saved);
        var result = await executor.ExecuteAsync(Plan(), input, files, checkpoint, password: "ok");
        Assert.AreEqual("ArchiveGroupInputChanged", result.FailureCode);
        Assert.AreEqual(2, archives.Extracted.Count);
        Assert.IsFalse((await State()).State.Passwords.ContainsValue("ok"));
    }

    [TestMethod]
    public async Task Legacy_pending_group_does_not_borrow_an_index_based_password_without_input_binding()
    {
        var files = await Inputs("z.zip"); var archives = new Archives();
        var executor = new FileProcessingPlanExecutor(archives);
        await executor.ExecuteAsync(Plan(), input, files, checkpoint);
        var (path, saved) = await State();
        saved.Operations.Single().Value.ArchiveInputs = null;
        await Save(path, saved);
        var probeCount = archives.Probes;
        var result = await executor.ExecuteAsync(Plan(), input, files, checkpoint, password: "ok");
        Assert.AreEqual("ArchiveGroupInputChanged", result.FailureCode);
        Assert.AreEqual(probeCount, archives.Probes);
        Assert.AreEqual(0, archives.Extracted.Count);
        Assert.IsFalse((await State()).State.Passwords.ContainsValue("ok"));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Legacy_completed_group_without_full_replacement_evidence_is_not_reused(bool missingManifest)
    {
        var files = await Inputs("a.zip", "z.zip"); var archives = new Archives();
        var executor = new FileProcessingPlanExecutor(archives);
        await executor.ExecuteAsync(Plan(), input, files, checkpoint);
        var (path, saved) = await State();
        var completed = saved.Operations.Values.Single(o => o.Completed);
        completed.ArchiveInputs = null;
        if (missingManifest) completed.OutputManifest = null; else saved.LineageVersion = 0;
        await Save(path, saved);
        var result = await executor.ExecuteAsync(Plan(), input, files, checkpoint, password: "ok");
        Assert.AreEqual("ArchiveGroupInputChanged", result.FailureCode);
        Assert.AreEqual(1, archives.Extracted.Count);
    }

    [TestMethod]
    public async Task New_binding_preserves_entry_order_not_only_member_set()
    {
        var files = await Inputs("a.part1.rar", "a.part2.rar", "z.zip"); var archives = new Archives();
        var executor = new FileProcessingPlanExecutor(archives);
        await executor.ExecuteAsync(Plan(), input, files, checkpoint);
        var (path, saved) = await State();
        saved.Operations.Values.Single(o => o.Completed).ArchiveInputs!.Reverse();
        await Save(path, saved);
        var result = await executor.ExecuteAsync(Plan(), input, files, checkpoint, password: "ok");
        Assert.AreEqual("ArchiveGroupInputChanged", result.FailureCode);
        Assert.AreEqual(1, archives.Extracted.Count);
    }

    [TestMethod]
    public async Task Ambiguous_legacy_output_ownership_cannot_reconstruct_input_membership()
    {
        var files = await Inputs("a.zip", "z.zip"); var archives = new Archives();
        var executor = new FileProcessingPlanExecutor(archives);
        await executor.ExecuteAsync(Plan(), input, files, checkpoint);
        var (path, saved) = await State();
        var completed = saved.Operations.Values.Single(o => o.Completed);
        completed.ArchiveInputs = null;
        saved.Operations["other-step"] = new FileProcessingPlanExecutor.Operation
            {Completed = true, OutputDirectory = completed.OutputDirectory, OutputManifest = completed.OutputManifest};
        await Save(path, saved);
        var result = await executor.ExecuteAsync(Plan(), input, files, checkpoint, password: "ok");
        Assert.AreEqual("ArchiveGroupInputChanged", result.FailureCode);
        Assert.AreEqual(1, archives.Extracted.Count);
    }

    private sealed class Archives : IArchiveExtractionService
    {
        public List<string> Extracted { get; } = [];
        public int Probes { get; private set; }
        public string LockedName { get; init; } = "z.zip";
        public Task<ArchivePasswordProbe> ProbePasswordAsync(string entryFile, IReadOnlyList<string?> candidates,
            Action<string>? onOutput, CancellationToken ct)
        {
            Probes++;
            return Task.FromResult(new ArchivePasswordProbe(Path.GetFileName(entryFile) != LockedName || candidates.Contains("ok"),
                "ok", [], "", ""));
        }
        public async Task<ArchiveExtractionResult> ExtractAsync(ArchiveExtractionRequest request, Action<int>? onProgress, CancellationToken ct)
        {
            var name = Path.GetFileName(request.Files[0]); Extracted.Add(name);
            await File.WriteAllTextAsync(Path.Combine(request.Directory, name + ".txt"), name, ct);
            return new(true, request.Directory, "");
        }
    }
}
