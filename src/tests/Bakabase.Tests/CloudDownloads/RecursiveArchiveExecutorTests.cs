using System.Text.Json;
using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.InsideWorld.Business.Components.Compression;
using Bakabase.Service.Components.FileProcessing;

namespace Bakabase.Tests.CloudDownloads;

[TestClass]
public sealed class RecursiveArchiveExecutorTests
{
    private string _directory = null!;
    private string _input = null!;
    private string _state = null!;
    private readonly Archives _archives = new();
    private RecursiveArchiveExecutor Executor => new(_archives);
    private static RecursiveArchiveOptions Limits => new() {MinimumFreeBytes = 0};

    [TestInitialize]
    public void Initialize()
    {
        _directory = Path.Combine(Path.GetTempPath(), "recursive-archives-" + Guid.NewGuid().ToString("N"));
        _input = Path.Combine(_directory, "input"); _state = Path.Combine(_directory, "state");
        Directory.CreateDirectory(_input);
    }
    [TestCleanup] public void Cleanup() { if (Directory.Exists(_directory)) Directory.Delete(_directory, true); }
    private async Task<string> Input(string name, string content)
    {
        var path = Path.Combine(_input, name); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, content); return path;
    }

    [TestMethod]
    public async Task Dynamic_nested_archives_inherit_input_retention_and_preserve_frozen_policy_on_resume()
    {
        var archive = await Input("outer.zip", "outer");
        var result = await Executor.ExecuteAsync(_input, [archive], _state, Limits, storagePolicy: new(),
            storageTitle: "Resource", inputPolicies: new Dictionary<string, ArtifactStoragePolicy>
            {{archive, new() {CleanupIntermediates = false}}});
        Assert.IsTrue(result.Completed);
        var state = JsonSerializer.Deserialize<RecursiveArchiveExecutor.State>(await File.ReadAllTextAsync(
            Path.Combine(_state, "recursive-state.json")), new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Assert.AreEqual(2, state.Nodes.Count);
        Assert.IsTrue(state.Nodes.All(n => n.StoragePolicy?.CleanupIntermediates == false));
        Assert.AreEqual(0, (await RecursiveArchiveExecutor.GetReclaimableFilesAsync(_state)).Count);
        Assert.IsTrue((await Executor.ExecuteAsync(_input, [archive], _state, Limits,
            storagePolicy: new() {CleanupIntermediates = true})).Completed);
        Assert.AreEqual(2, _archives.Calls.Count);
        Assert.IsTrue(File.Exists(archive));
        Assert.IsTrue(result.Files.All(File.Exists));
    }

    [TestMethod]
    public async Task Recurses_only_new_exact_outputs_and_leaves_delivery_formats_and_originals()
    {
        var input = await Input("outer.zip", "outer");
        var document = await Input("book.epub", "outer");
        var unrelated = await Input("unselected.zip", "outer");
        var result = await Executor.ExecuteAsync(_input, [input, document], _state, Limits);
        Assert.IsTrue(result.Completed);
        Assert.AreEqual(2, _archives.Calls.Count);
        CollectionAssert.AreEquivalent(new[] {"outer", "inner"}, _archives.Calls.ToArray());
        Assert.AreEqual(2, result.Lineage!.Count);
        Assert.AreEqual("payload", await File.ReadAllTextAsync(result.Files.Single(f => f.EndsWith("file.txt"))));
        CollectionAssert.AreEquivalent(new[] {Path.Combine("outer", "inner", "file.txt"), "book.epub"},
            result.Files.Select(f => Path.GetRelativePath(result.OutputDirectory!, f)).ToArray());
        Assert.IsTrue(result.Files.Any(f => Path.GetFileName(f) == "book.epub"));
        CollectionAssert.AreEqual(new[] {input}, result.SourceArchiveFiles!.ToArray());
        foreach (var original in new[] {input, document, unrelated}) Assert.AreEqual("outer", await File.ReadAllTextAsync(original));
    }

    [TestMethod]
    public async Task Password_resume_preserves_completed_siblings_and_applies_default_at_each_layer()
    {
        var first = await Input("a.zip", "plain"); var locked = await Input("b.zip", "locked");
        var waiting = await Executor.ExecuteAsync(_input, [first, locked], _state, Limits, defaultPassword: "wrong");
        Assert.IsFalse(waiting.Completed); Assert.AreEqual("PasswordUnknown", waiting.WaitReason);
        Assert.AreEqual(1, _archives.Calls.Count);
        await Assert.ThrowsExceptionAsync<ArgumentException>(() => Executor.ExecuteAsync(_input, [first, locked], _state,
            Limits, new("another", "correct")));
        var complete = await Executor.ExecuteAsync(_input, [first, locked], _state, Limits,
            new(waiting.ArchiveId!, "correct"), defaultPassword: "correct");
        Assert.IsTrue(complete.Completed); Assert.AreEqual(2, _archives.Calls.Count);
        var repeated = await Executor.ExecuteAsync(_input, [first, locked], _state, Limits);
        Assert.IsTrue(repeated.Completed); Assert.AreEqual(2, _archives.Calls.Count);
        var nestedRoot = Path.Combine(_directory, "nested-state");
        var outer = await Input("c.zip", "outer-locked");
        Assert.IsTrue((await Executor.ExecuteAsync(_input, [outer], nestedRoot, Limits, defaultPassword: "correct")).Completed);
    }

    [TestMethod]
    public async Task Depth_limit_is_incomplete_and_increased_budget_continues_without_repeating_parent()
    {
        var file = await Input("outer.zip", "outer");
        var waiting = await Executor.ExecuteAsync(_input, [file], _state, Limits with {MaxDepth = 1});
        Assert.IsFalse(waiting.Completed); Assert.AreEqual("DepthLimit", waiting.WaitReason);
        Assert.AreEqual(1, _archives.Calls.Count);
        var done = await Executor.ExecuteAsync(_input, [file], _state, Limits with {MaxDepth = 2});
        Assert.IsTrue(done.Completed); Assert.AreEqual(2, _archives.Calls.Count);
    }

    [TestMethod]
    public async Task Expansion_budget_cancels_only_pending_archive_and_resume_retries_it()
    {
        var first = await Input("a.zip", "plain"); var second = await Input("b.zip", "large");
        var waiting = await Executor.ExecuteAsync(_input, [first, second], _state, Limits with {MaxExpandedBytes = 50});
        Assert.IsFalse(waiting.Completed); Assert.AreEqual("ExpandedBytesLimit", waiting.WaitReason);
        Assert.AreEqual(1, _archives.Calls.Count(c => c == "plain"));
        var done = await Executor.ExecuteAsync(_input, [first, second], _state, Limits with {MaxExpandedBytes = 1000});
        Assert.IsTrue(done.Completed); Assert.AreEqual(1, _archives.Calls.Count(c => c == "plain"));
        Assert.AreEqual(2, _archives.Calls.Count(c => c == "large"));
    }

    [TestMethod]
    public async Task Periodic_monitor_interrupts_extraction_even_without_progress_callbacks()
    {
        var archive = await Input("silent.zip", "silent-large");
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var waiting = await Executor.ExecuteAsync(_input, [archive], _state,
            Limits with {MaxExpandedBytes = 50}, ct: deadline.Token);
        Assert.AreEqual("ExpandedBytesLimit", waiting.WaitReason);
        Assert.IsFalse(waiting.Completed);
        Assert.IsFalse(deadline.IsCancellationRequested, "The internal budget monitor must cancel before the external deadline.");
        Assert.IsTrue(File.Exists(archive));
    }

    [TestMethod]
    public async Task Repeated_ancestor_waits_and_identical_sibling_reuses_verified_outputs()
    {
        var cycle = await Input("cycle.zip", "cycle");
        var wait = await Executor.ExecuteAsync(_input, [cycle], _state, Limits);
        Assert.AreEqual("RepeatedContent", wait.WaitReason); Assert.IsFalse(wait.Completed);
        var first = await Input("a.zip", "plain"); var second = await Input("b.zip", "plain");
        var done = await Executor.ExecuteAsync(_input, [first, second], Path.Combine(_directory, "same-state"), Limits);
        Assert.IsTrue(done.Completed); Assert.AreEqual(1, _archives.Calls.Count(c => c == "plain"));
        Assert.AreEqual(2, done.Files.Count); Assert.AreEqual(2, done.Lineage!.Count);
        CollectionAssert.AreEquivalent(new[] {Path.Combine("a", "file.txt"), Path.Combine("b", "file.txt")},
            done.Files.Select(f => Path.GetRelativePath(done.OutputDirectory!, f)).ToArray());
    }

    [TestMethod]
    public async Task Reused_sibling_outputs_still_consume_bytes_budget_and_resume_without_another_extraction()
    {
        var first = await Input("a.zip", "plain"); var second = await Input("b.zip", "plain");
        var waiting = await Executor.ExecuteAsync(_input, [first, second], _state, Limits with {MaxExpandedBytes = 10});
        Assert.IsFalse(waiting.Completed); Assert.AreEqual("ExpandedBytesLimit", waiting.WaitReason);
        Assert.AreEqual(1, _archives.Calls.Count);
        var done = await Executor.ExecuteAsync(_input, [first, second], _state, Limits with {MaxExpandedBytes = 14});
        Assert.IsTrue(done.Completed); Assert.AreEqual(1, _archives.Calls.Count);
        CollectionAssert.AreEquivalent(new[] {Path.Combine("a", "file.txt"), Path.Combine("b", "file.txt")},
            done.Files.Select(f => Path.GetRelativePath(done.OutputDirectory!, f)).ToArray());
        Assert.AreEqual(14L, done.Files.Sum(f => new FileInfo(f).Length));
    }

    [TestMethod]
    public async Task Missing_nested_volume_waits_preserving_parent_and_never_adopts_an_unselected_sibling()
    {
        var archive = await Input("outer.zip", "missing-volumes");
        var waiting = await Executor.ExecuteAsync(_input, [archive], _state, Limits);
        Assert.IsFalse(waiting.Completed); Assert.AreEqual("ArchiveInputRequired", waiting.WaitReason);
        Assert.IsNotNull(waiting.ArchiveId); Assert.AreEqual(1, waiting.Lineage!.Count);
        Assert.AreEqual(1, _archives.Calls.Count);
        StringAssert.Contains(waiting.Message!, "submit this exact archive group again");
        var selected = waiting.Files.Single(f => f.EndsWith("inner.7z.001", StringComparison.Ordinal));
        var unselected = Path.Combine(Path.GetDirectoryName(selected)!, "inner.7z.002");
        await File.WriteAllTextAsync(unselected, "unselected-part");
        var resumed = await Executor.ExecuteAsync(_input, [archive], _state, Limits);
        Assert.IsFalse(resumed.Completed); Assert.AreEqual("ArchiveInputRequired", resumed.WaitReason);
        Assert.AreEqual(waiting.ArchiveId, resumed.ArchiveId); Assert.AreEqual(1, resumed.Lineage!.Count);
        Assert.AreEqual(1, _archives.Calls.Count, "Completed parent extraction must not run again.");
        Assert.IsFalse(resumed.Files.Contains(unselected));
        Assert.IsFalse(resumed.Lineage[0].OutputFiles.Contains(unselected));
        Assert.AreEqual("unselected-part", await File.ReadAllTextAsync(unselected));
        Assert.AreEqual("missing-volumes", await File.ReadAllTextAsync(archive));
    }

    [TestMethod]
    public async Task Completed_result_hash_is_checked_even_if_originals_have_been_quarantined()
    {
        var file = await Input("a.zip", "plain");
        var done = await Executor.ExecuteAsync(_input, [file], _state, Limits);
        File.Delete(file);
        Assert.IsTrue((await Executor.ExecuteAsync(_input, [file], _state, Limits)).Completed);
        await File.WriteAllTextAsync(done.Files[0], "tampered");
        await Assert.ThrowsExceptionAsync<InvalidDataException>(() => Executor.ExecuteAsync(_input, [file], _state, Limits));
    }

    [TestMethod]
    public async Task Relative_durable_paths_allow_completed_result_after_appdata_moves()
    {
        var file = await Input("a.zip", "plain");
        Assert.IsTrue((await Executor.ExecuteAsync(_input, [file], _state, Limits)).Completed);
        var relocated = _directory + "-moved";
        Directory.Move(_directory, relocated);
        _directory = relocated; _input = Path.Combine(relocated, "input"); _state = Path.Combine(relocated, "state");
        var result = await Executor.ExecuteAsync(_input, [Path.Combine(_input, "a.zip")], _state, Limits);
        Assert.IsTrue(result.Completed); Assert.AreEqual(1, _archives.Calls.Count);
    }

    [TestMethod]
    public async Task Relocated_waiting_nested_archive_reuses_its_existing_inner_checkpoint()
    {
        var input = await Input("outer.zip", "outer-hidden");
        var waiting = await Executor.ExecuteAsync(_input, [input], _state, Limits);
        Assert.AreEqual("PasswordUnknown", waiting.WaitReason);
        var checkpoints = Directory.GetFiles(_state, "state.json", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(_state, path)).ToArray();
        Assert.AreEqual(2, checkpoints.Length);

        var original = _directory;
        var relocated = original + "-moved";
        Directory.Move(original, relocated);
        _directory = relocated; _input = Path.Combine(relocated, "input"); _state = Path.Combine(relocated, "state");
        // The migration updates structured checkpoint paths but retains their directory keys.
        var before = System.Text.Json.JsonSerializer.Serialize(original)[1..^1];
        var after = System.Text.Json.JsonSerializer.Serialize(relocated)[1..^1];
        foreach (var path in Directory.GetFiles(_state, "state.json", SearchOption.AllDirectories))
            await File.WriteAllTextAsync(path, (await File.ReadAllTextAsync(path)).Replace(before, after));
        string Identity(string path) => Path.Combine(original, Path.GetRelativePath(relocated, path));
        var result = await Executor.ExecuteAsync(_input, [Path.Combine(_input, "outer.zip")], _state,
            Limits, new(waiting.ArchiveId!, "correct"), identityPath: Identity);

        Assert.IsTrue(result.Completed);
        CollectionAssert.AreEquivalent(checkpoints, Directory.GetFiles(_state, "state.json", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(_state, path)).ToArray());
        CollectionAssert.AreEqual(new[] {"outer-hidden", "locked"}, _archives.Calls.ToArray());
        Assert.AreEqual("payload", await File.ReadAllTextAsync(result.Files.Single()));
    }

    [TestMethod]
    public async Task KnownExpansionOverRecursiveLimitWaitsBeforeWritingAndAcceptsAnExactLimitOnRetry()
    {
        var archive = await Input("large.zip", "large");
        _archives.DeclaredBytes = 100;
        var guard = new StorageWriteGuard(new NoAdditionalFloor(), _ => ("fixture", 200L << 20), TimeSpan.FromMilliseconds(50));
        var executor = new RecursiveArchiveExecutor(_archives, writeGuard: guard);
        var low = await executor.ExecuteAsync(_input, [archive], _state, Limits with {MaxExpandedBytes = 99});
        Assert.AreEqual("ExpandedBytesLimit", low.WaitReason);
        Assert.AreEqual(0, _archives.Calls.Count);
        var ready = await executor.ExecuteAsync(_input, [archive], _state, Limits with {MaxExpandedBytes = 100});
        Assert.IsTrue(ready.Completed, ready.Message);
        Assert.AreEqual(1, _archives.Calls.Count);
    }

    private sealed class NoAdditionalFloor : IStorageWritePolicyProvider
    {
        public Task<long> GetMinimumFreeBytesAsync(StorageWriteContext? context, CancellationToken ct = default) =>
            Task.FromResult(0L);
    }

    private sealed class Archives : IArchiveExtractionService
    {
        public List<string> Calls { get; } = [];
        public long? DeclaredBytes { get; set; }
        public Task<long?> TryGetExpandedBytesAsync(ArchiveExtractionRequest request, CancellationToken ct) =>
            Task.FromResult(DeclaredBytes);
        public async Task<ArchivePasswordProbe> ProbePasswordAsync(string entry, IReadOnlyList<string?> candidates,
            Action<string>? output, CancellationToken ct)
        {
            var content = await File.ReadAllTextAsync(entry, ct);
            var locked = content.Contains("locked"); var password = candidates.FirstOrDefault(c => c == "correct");
            return new(!locked || password != null, password, [], "", "");
        }
        public async Task<ArchiveExtractionResult> ExtractAsync(ArchiveExtractionRequest request, Action<int>? progress, CancellationToken ct)
        {
            var marker = await File.ReadAllTextAsync(request.Files[0], ct); Calls.Add(marker);
            Directory.CreateDirectory(request.Directory);
            var outputs = marker switch
            {
                "outer" => new Dictionary<string, string> {{"inner.zip", "inner"}},
                "outer-locked" => new() {{"inner.zip", "locked"}},
                "outer-hidden" => new() {{"inner.zip", "locked"}},
                "cycle" => new() {{"cycle.zip", "cycle"}},
                "missing-volumes" => new() {{"inner.7z.001", "part-one"}, {"inner.7z.003", "part-three"}},
                "large" or "silent-large" => new() {{"large.bin", new string('x', 100)}},
                _ => new() {{"file.txt", "payload"}}
            };
            foreach (var (name, value) in outputs) await File.WriteAllTextAsync(Path.Combine(request.Directory, name), value, ct);
            if (marker == "silent-large") await Task.Delay(Timeout.Infinite, ct);
            progress?.Invoke(100); ct.ThrowIfCancellationRequested();
            return new(true, request.Directory, "");
        }
    }
}
