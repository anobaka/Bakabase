using System.Text.Json;
using Bakabase.Service.Components.FileProcessing;

namespace Bakabase.Tests.Artifacts;

[TestClass]
public sealed class FileProcessingWorkspaceTests
{
    private string _root = null!;
    private string _app = null!;
    private string _downloads = null!;
    private const string Key = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string AnotherKey = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    [TestInitialize]
    public void Setup()
    {
        _root = Path.Combine(Path.GetTempPath(), "processing-placement-" + Guid.NewGuid().ToString("N"));
        _app = Path.Combine(_root, "app"); _downloads = Path.Combine(_root, "downloads");
        Directory.CreateDirectory(_app); Directory.CreateDirectory(_downloads);
    }
    [TestCleanup]
    public void Cleanup() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }

    [TestMethod]
    public async Task Default_payload_is_beside_downloads_and_configuration_changes_do_not_relocate_it()
    {
        var resolver = new FileProcessingWorkspaceResolver(_app);
        var first = await resolver.ResolveAsync("operations", Key, 1, _downloads, null, default);
        StringAssert.StartsWith(first, Path.Combine(_downloads, ".bakabase-work"));
        Assert.IsFalse(Directory.Exists(Path.Combine(_app, "artifact-operations")));
        var changed = Path.Combine(_root, "custom"); Directory.CreateDirectory(changed);
        var resumed = await new FileProcessingWorkspaceResolver(_app).ResolveAsync("operations", Key, 1, _downloads, changed, default);
        Assert.AreEqual(first, resumed);
        var next = await resolver.ResolveAsync("operations", AnotherKey, 1, _downloads, changed, default);
        StringAssert.StartsWith(next, Path.Combine(changed, ".bakabase-work"));
    }

    [TestMethod]
    public async Task Missing_selected_or_pinned_storage_never_recreates_a_mount_directory()
    {
        var resolver = new FileProcessingWorkspaceResolver(_app);
        var unavailable = Path.Combine(_root, "absent-share", "downloads");
        await Assert.ThrowsExactlyAsync<DirectoryNotFoundException>(() => resolver.ResolveAsync("operations", Key, 1, _downloads, unavailable, default));
        Assert.IsFalse(Directory.Exists(Path.Combine(_root, "absent-share")));
        var first = await resolver.ResolveAsync("operations", Key, 1, _downloads, null, default);
        Directory.Move(_downloads, _downloads + ".detached");
        await Assert.ThrowsExactlyAsync<DirectoryNotFoundException>(() => resolver.ResolveAsync("operations", Key, 1, _downloads, _root, default));
        Assert.IsFalse(Directory.Exists(_downloads));
        Assert.IsFalse(Directory.Exists(first));
    }

    [TestMethod]
    public async Task Existing_legacy_checkpoint_is_pinned_without_copying_or_reprocessing()
    {
        var legacy = Path.Combine(_app, "artifact-plan-checkpoints", Key, "1"); Directory.CreateDirectory(legacy);
        await File.WriteAllTextAsync(Path.Combine(legacy, "receipt"), "existing");
        var actual = await new FileProcessingWorkspaceResolver(_app).ResolveAsync("checkpoints", Key, 1, _downloads, null, default);
        Assert.AreEqual(legacy, actual);
        Assert.AreEqual("existing", await File.ReadAllTextAsync(Path.Combine(actual, "receipt")));
    }

    [TestMethod]
    public async Task Relocated_checkpoints_reuse_their_original_semantic_paths_and_new_work_still_runs()
    {
        var source = Path.Combine(_app, "artifact-plan-checkpoints");
        var destination = Path.Combine(_downloads, "migrated-checkpoints");
        Directory.CreateDirectory(Path.Combine(destination, Key, "1"));
        Directory.CreateDirectory(Path.Combine(_app, "artifact-workspaces"));
        await File.WriteAllTextAsync(Path.Combine(_app, "artifact-workspaces", "relocations.json"),
            JsonSerializer.Serialize(new FileProcessingWorkspaceResolver.Relocations(1,
                [new(source, destination)]), Json));
        var resolver = new FileProcessingWorkspaceResolver(_app);
        var migrated = await resolver.ResolveAsync("checkpoints", Key, 1, _downloads, null, default);
        Assert.AreEqual(Path.Combine(destination, Key, "1"), migrated);
        Assert.AreEqual(Path.Combine(source, Key, "1", "x.zip"), resolver.IdentityPath(Path.Combine(migrated, "x.zip")));
        Assert.AreEqual(destination + "-unrelated/x", resolver.IdentityPath(destination + "-unrelated/x"));
        var next = await resolver.ResolveAsync("checkpoints", AnotherKey, 1, _downloads, null, default);
        StringAssert.StartsWith(next, Path.Combine(_downloads, ".bakabase-work"));
    }

    [TestMethod]
    public async Task Using_an_existing_workspace_artifact_does_not_nest_managed_roots()
    {
        var resolver = new FileProcessingWorkspaceResolver(_app);
        var first = await resolver.ResolveAsync("operations", Key, 1, _downloads, null, default);
        var result = Path.Combine(first, "plan", "result"); Directory.CreateDirectory(result);
        var next = await resolver.ResolveAsync("operations", AnotherKey, 1, result, null, default);
        Assert.AreEqual(1, next.Split(".bakabase-work").Length - 1);
        StringAssert.StartsWith(next, Path.Combine(_downloads, ".bakabase-work"));
    }

    [TestMethod]
    public async Task Rclone_copy_payloads_use_the_same_pinned_user_storage_without_adopting_plan_checkpoints()
    {
        var unrelated = Path.Combine(_app, "artifact-plan-checkpoints", Key, "1"); Directory.CreateDirectory(unrelated);
        var actual = await new FileProcessingWorkspaceResolver(_app).ResolveAsync("rclone-copies", Key, 1, _downloads, null, default);
        StringAssert.StartsWith(actual, Path.Combine(_downloads, ".bakabase-work"));
        StringAssert.Contains(actual, "rclone-copies");
        Assert.AreNotEqual(unrelated, actual);
    }

    [TestMethod]
    public async Task Overlapping_relocation_roots_are_rejected_instead_of_guessing()
    {
        Directory.CreateDirectory(Path.Combine(_app, "artifact-workspaces"));
        await File.WriteAllTextAsync(Path.Combine(_app, "artifact-workspaces", "relocations.json"),
            JsonSerializer.Serialize(new FileProcessingWorkspaceResolver.Relocations(1,
                [new(Path.Combine(_app, "old"), Path.Combine(_downloads, "new")),
                 new(Path.Combine(_app, "old", "nested"), Path.Combine(_downloads, "another"))]), Json));
        Assert.ThrowsExactly<InvalidDataException>(() => new FileProcessingWorkspaceResolver(_app).IdentityPath(_downloads));
    }

    private async Task WriteRelocations(IReadOnlyList<FileProcessingWorkspaceResolver.Relocation> mappings)
    {
        Directory.CreateDirectory(Path.Combine(_app, "artifact-workspaces"));
        await File.WriteAllTextAsync(Path.Combine(_app, "artifact-workspaces", "relocations.json"),
            JsonSerializer.Serialize(new FileProcessingWorkspaceResolver.Relocations(1, mappings), Json));
    }

    [DataTestMethod]
    [DataRow(101)]
    [DataRow(1024)]
    public async Task Larger_disjoint_maps_preserve_forward_reverse_and_checkpoint_identity(int count)
    {
        var mappings = Enumerable.Range(0, count).Select(i => new FileProcessingWorkspaceResolver.Relocation(
            Path.Combine(_root, "source-" + i.ToString("D4")), Path.Combine(_root, "destination-" + i.ToString("D4")))).ToArray();
        await WriteRelocations(mappings);
        var resolver = new FileProcessingWorkspaceResolver(_app);
        foreach (var index in new[] {0, count / 2, count - 1})
        {
            var source = Path.Combine(mappings[index].SourceRoot, "folder", "archive.zip");
            var target = Path.Combine(mappings[index].DestinationRoot, "folder", "archive.zip");
            Assert.AreEqual(target, resolver.Relocate(source));
            Assert.AreEqual(source, resolver.IdentityPath(target));
            Assert.AreEqual(FileProcessingPlanExecutor.CheckpointIdentity(new(), [source]),
                FileProcessingPlanExecutor.CheckpointIdentity(new(), [target], resolver.IdentityPath));
            Assert.AreEqual(mappings[index].SourceRoot + "-unrelated", resolver.Relocate(mappings[index].SourceRoot + "-unrelated"));
        }
    }

    [DataTestMethod]
    [DataRow("duplicate-source")]
    [DataRow("duplicate-destination")]
    [DataRow("nested-source")]
    [DataRow("nested-destination")]
    [DataRow("own-overlap")]
    [DataRow("chain")]
    [DataRow("cycle")]
    [DataRow("destination-ancestor")]
    [DataRow("source-volume-root")]
    [DataRow("destination-volume-root")]
    public async Task Enlarged_maps_still_reject_ambiguous_or_chained_roots(string fault)
    {
        var a = Path.Combine(_root, "a"); var b = Path.Combine(_root, "b");
        var x = Path.Combine(_root, "x"); var y = Path.Combine(_root, "y");
        FileProcessingWorkspaceResolver.Relocation[] mappings = fault switch
        {
            "duplicate-source" => [new(a, x), new(a, y)],
            "duplicate-destination" => [new(a, x), new(b, x)],
            // Punctuation siblings must not hide an ancestor during adjacent-root comparison.
            "nested-source" => [new(a, x), new(a + "-sibling", y), new(Path.Combine(a, "nested"), Path.Combine(_root, "z"))],
            "nested-destination" => [new(a, x), new(b, x + "-sibling"), new(Path.Combine(_root, "c"), Path.Combine(x, "nested"))],
            "own-overlap" => [new(a, Path.Combine(a, "nested"))],
            "chain" => [new(a, b), new(b, x)],
            "cycle" => [new(a, b), new(b, a)],
            "destination-ancestor" => [new(a, x), new(Path.Combine(x, "nested"), y)],
            "source-volume-root" => [new(Path.GetPathRoot(_root)!, x)],
            "destination-volume-root" => [new(a, Path.GetPathRoot(_root)!)],
            _ => throw new ArgumentException(fault)
        };
        await WriteRelocations(mappings);
        var resolver = new FileProcessingWorkspaceResolver(_app);
        Assert.ThrowsExactly<InvalidDataException>(() => resolver.Relocate(a));
        Assert.ThrowsExactly<InvalidDataException>(() => resolver.IdentityPath(x));
    }

    [TestMethod]
    public async Task Relocation_map_count_and_file_size_are_bounded_and_replacements_are_not_cached()
    {
        var source = Path.Combine(_root, "source"); var destination = Path.Combine(_root, "destination");
        var resolver = new FileProcessingWorkspaceResolver(_app);
        await WriteRelocations([new(source, destination)]);
        Assert.AreEqual(destination, resolver.Relocate(source));
        await WriteRelocations([new(source, destination + "-revised")]);
        Assert.AreEqual(destination + "-revised", resolver.Relocate(source));
        await WriteRelocations(Enumerable.Range(0, FileProcessingWorkspaceResolver.MaximumRelocationMappings + 1)
            .Select(i => new FileProcessingWorkspaceResolver.Relocation(source + i, destination + i)).ToArray());
        Assert.ThrowsExactly<InvalidDataException>(() => resolver.Relocate(source));
        await File.WriteAllTextAsync(Path.Combine(_app, "artifact-workspaces", "relocations.json"),
            "{\"version\":1,\"mappings\":[]}" + new string(' ', FileProcessingWorkspaceResolver.MaximumRelocationBytes));
        Assert.ThrowsExactly<InvalidDataException>(() => resolver.Relocate(source));
    }

    [TestMethod]
    public async Task Whole_directory_inputs_do_not_ingest_an_earlier_managed_workspace()
    {
        var original = Path.Combine(_downloads, "original.zip"); await File.WriteAllTextAsync(original, "original");
        var workspace = await new FileProcessingWorkspaceResolver(_app).ResolveAsync("operations", Key, 1, _downloads, null, default);
        var staged = Path.Combine(workspace, "staged.zip"); await File.WriteAllTextAsync(staged, "staged");
        CollectionAssert.AreEqual(new[] {original}, FileProcessingFiles.Enumerate(_downloads, excludeManagedWorkspaces: true));
        CollectionAssert.AreEqual(new[] {staged}, FileProcessingFiles.Enumerate(workspace));
    }

    [TestMethod]
    public void Placement_does_not_change_processing_identity()
    {
        var source = FileArtifactProcessor.Intent("source", "{\"requirement\":\"notRequired\"}", null, new());
        var overrideIntent = FileArtifactProcessor.Intent("source", "{\"requirement\":\"notRequired\"}", null,
            new() {WorkspaceRootDirectory = _downloads});
        Assert.AreEqual(source, overrideIntent);
    }
}
