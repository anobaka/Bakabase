using System.Text.Json;
using Bakabase.InsideWorld.Business;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.InsideWorld.Business.Components.Compression;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Service.Components.FileProcessing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Tests.Artifacts;

[TestClass]
public sealed partial class FileArtifactProcessorTests
{
    private string _root = null!;
    private string _appData = null!;
    private BakabaseDbContext _db = null!;
    private ArtifactOperationStore _store = null!;
    private ServiceProvider _services = null!;
    private FileArtifactProcessor _processor = null!;
    private readonly FixtureArchives _archives = new();
    private static readonly FileArtifactProcessingConfig Config = new();
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [TestInitialize]
    public async Task Initialize()
    {
        _root = Path.Combine(Path.GetTempPath(), "file-artifact-processor-" + Guid.NewGuid().ToString("N"));
        _appData = Path.Combine(_root, "appdata");
        Directory.CreateDirectory(_appData);
        var options = new DbContextOptionsBuilder<BakabaseDbContext>()
            .UseSqlite("Data Source=" + Path.Combine(_root, "test.db")).Options;
        _db = new(options);
        await _db.Database.EnsureCreatedAsync();
        _store = new(_db, () => _appData);
        _services = new ServiceCollection().AddScoped(_ => new BakabaseDbContext(options))
            .AddScoped<IArtifactOperationStore>(sp => new ArtifactOperationStore(sp.GetRequiredService<BakabaseDbContext>(), () => _appData))
            .BuildServiceProvider();
        _processor = new(_store, new FileProcessingPlanExecutor(_archives), new RecursiveArchiveExecutor(_archives),
            () => _appData, _services.GetRequiredService<IServiceScopeFactory>(), maintenance:
                new ArtifactWorkspaceMaintenance(_db, _store, new FileProcessingPlanExecutor(_archives), new RecursiveArchiveExecutor(_archives), _appData));
    }

    [TestCleanup]
    public async Task Cleanup()
    {
        await _services.DisposeAsync();
        await _db.DisposeAsync();
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    private async Task<ArtifactRecord> Input(bool nested = false)
    {
        var directory = Path.Combine(_root, "downloads");
        Directory.CreateDirectory(directory);
        var file = Path.Combine(directory, nested ? "outer.zip" : "single.zip");
        await File.WriteAllTextAsync(file, nested ? "outer" : "single");
        return await _store.AdoptAsync(new("download", "download-intent", "download", "source-v1"),
            new(directory, new[] {file}, Managed: true), "download-owner");
    }

    private static string Plan(bool nested = false, bool rename = false)
    {
        var steps = new List<FileProcessingStep> {new() {Id = "open", Op = "extractArchive", Selector = "*.zip"}};
        if (nested) steps.Add(new() {Id = "inner", Input = "open", Op = "extractArchive", Selector = "inner.zip"});
        if (rename) steps.Add(new() {Id = "rename", Input = "open", Op = "renameFile", Selector = "payload.txt", TargetName = "renamed.txt"});
        return JsonSerializer.Serialize(new FileProcessingPlan {Requirement = "required", Steps = steps}, Json);
    }

    private static string Intent(string plan) => FileArtifactProcessor.Intent("download-intent", plan, null, Config);
    private Task<FileArtifactProcessingResult> Execute(string intent, ArtifactRecord input, string plan,
        string consumer = "workflow:1:target", string? password = null) => _processor.ExecuteAsync(intent, input,
        plan, null, Config, consumer, null, password, null, default);

    [TestMethod]
    public async Task Separate_runs_with_same_intent_reuse_one_extraction_and_artifact()
    {
        var input = await Input();
        var plan = Plan();
        var intent = Intent(plan);
        var first = await Execute(intent, input, plan, "workflow:1:target");
        var second = await Execute(intent, input, plan, "workflow:2:target");
        Assert.IsNotNull(first.Artifact);
        Assert.AreEqual(first.Artifact.Id, second.Artifact!.Id);
        Assert.AreEqual(1, _archives.Extractions);
        CollectionAssert.AreEqual(new[] {input.Id}, first.Artifact.ParentArtifactIds.ToArray());
        Assert.AreEqual("payload", await File.ReadAllTextAsync(first.Artifact.Files.Single()));
        Assert.IsTrue(File.Exists(input.Files.Single()));
    }

    [TestMethod]
    public async Task Directory_support_preserves_legacy_recipe_digest_and_completed_cache_without_reexecution()
    {
        // Persisted by the v3 recipe before directory manifests existed. Changing this
        // identity would bypass old completed operations even when their files are intact.
        const string legacyIntent = "files-v2:3153b4d16abc2f4db8febbbeb28b8b5aa54b05327396cd231de04623137a1bdf";
        var input = await Input(); var plan = Plan();
        Assert.AreEqual(legacyIntent, Intent(plan));
        var root = Path.Combine(_root, "legacy-result"); Directory.CreateDirectory(root);
        var payload = Path.Combine(root, "payload.txt"); await File.WriteAllTextAsync(payload, "legacy");
        Directory.CreateDirectory(Path.Combine(root, "unregistered-empty"));
        var legacy = await _store.AdoptAsync(new("legacy-completed-operation", legacyIntent, "files.process", input.Fingerprint),
            new(root, [payload], "processedFiles", Managed: true), "legacy-owner");
        var reused = await Execute(Intent(plan), input, plan, "new-version");
        Assert.AreEqual(legacy.Id, reused.Artifact?.Id);
        Assert.AreEqual(legacy.Fingerprint, reused.Artifact?.Fingerprint);
        Assert.IsNull(reused.Artifact?.Manifest.Directories);
        Assert.AreEqual(0, _archives.Extractions);
        Assert.AreEqual(1, await _db.Set<ArtifactOperationDbModel>().CountAsync(operation => operation.Kind == "files.process"));
    }

    [TestMethod]
    public async Task Completed_processing_reclaims_private_snapshots_but_retains_history_originals_and_result()
    {
        var input = await Input(); var plan = Plan(rename: true);
        var result = (await Execute(Intent(plan), input, plan)).Artifact!;
        StringAssert.StartsWith(result.RootDirectory, Path.Combine(input.RootDirectory, ".bakabase-work"));
        var checkpoint = Path.GetDirectoryName(result.RootDirectory)!;
        Assert.IsFalse(Directory.Exists(Path.Combine(checkpoint, "inputs")));
        Assert.IsFalse(Directory.Exists(Path.Combine(checkpoint, "outputs")));
        var state = JsonSerializer.Deserialize<FileProcessingPlanExecutor.State>(await File.ReadAllTextAsync(Path.Combine(checkpoint, "state.json")), Json)!;
        Assert.IsNotNull(state.IntermediatesReclaimedAt);
        Assert.IsTrue(state.Outputs.ContainsKey("open"), "Historical step receipts must remain.");
        Assert.IsTrue(File.Exists(input.Files.Single()));
        Assert.AreEqual("payload", await File.ReadAllTextAsync(result.Files.Single()));
        Assert.AreEqual(result.Id, (await Execute(Intent(plan), input, plan, "another-run")).Artifact!.Id);
        Assert.AreEqual(1, _archives.Extractions);
    }

    [TestMethod]
    public async Task Disconnected_workspace_suspends_same_attempt_and_resumes_after_configuration_changes()
    {
        var original = Path.Combine(_root, "workspace-a"); Directory.CreateDirectory(original);
        var changed = Path.Combine(_root, "workspace-b"); Directory.CreateDirectory(changed);
        string configured = original;
        var processor = new FileArtifactProcessor(_store, new FileProcessingPlanExecutor(_archives), new RecursiveArchiveExecutor(_archives),
            () => _appData, _services.GetRequiredService<IServiceScopeFactory>(), () => configured);
        var input = await Input(nested: true); var plan = Plan(nested: true); var intent = Intent(plan);
        var waiting = await processor.ExecuteAsync(intent, input, plan, null, Config, "same-owner", null, null, null, default);
        Assert.AreEqual("PasswordUnknown", waiting.WaitReason);
        var operation = await _db.Set<ArtifactOperationDbModel>().AsNoTracking().SingleAsync(o => o.IntentKey == intent);
        Directory.Move(original, original + ".disconnected");
        configured = changed;
        var offline = await processor.ExecuteAsync(intent, input, plan, null, Config, "same-owner", null, "correct", null, default);
        Assert.AreEqual("StorageUnavailable", offline.WaitReason);
        Assert.AreEqual(ArtifactOperationState.Running, (await _store.GetOperationAsync(operation.Id))!.State);
        Assert.IsFalse(Directory.EnumerateFileSystemEntries(changed).Any());
        Directory.Move(original + ".disconnected", original);
        var complete = await processor.ExecuteAsync(intent, input, plan, null, Config, "same-owner", null, "correct", null, default);
        Assert.IsNotNull(complete.Artifact);
        Assert.AreEqual(1, (await _store.GetOperationAsync(operation.Id))!.Attempt);
        Assert.AreEqual(1, _archives.OuterExtractions);
        StringAssert.StartsWith(complete.Artifact.RootDirectory, original);
    }

    [TestMethod]
    public async Task Waiting_processing_keeps_private_snapshots_for_password_resume()
    {
        var input = await Input(nested: true); var plan = Plan(nested: true);
        Assert.IsNull((await Execute(Intent(plan), input, plan)).Artifact);
        var metadata = Path.Combine(_appData, "artifact-workspaces", "locations");
        var locationFile = Directory.GetFiles(metadata).Single();
        var location = JsonSerializer.Deserialize<FileProcessingWorkspaceResolver.Location>(await File.ReadAllTextAsync(locationFile), Json)!;
        var checkpoint = Directory.GetDirectories(Path.Combine(location.Directory, "plan")).Single();
        Assert.IsTrue(Directory.Exists(Path.Combine(checkpoint, "inputs")));
        Assert.IsTrue(Directory.Exists(Path.Combine(checkpoint, "outputs")));
        Assert.IsNotNull((await Execute(Intent(plan), input, plan, password: "correct")).Artifact);
        Assert.AreEqual(1, _archives.OuterExtractions);
    }

    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Resolving_final_result_needs_no_original_even_after_quarantine(bool quarantine)
    {
        var input = await Input();
        var plan = Plan();
        var intent = Intent(plan);
        var result = await Execute(intent, input, plan);
        if (quarantine)
        {
            Assert.IsTrue(await _store.TryQuarantineAsync(input.Id));
            var root = Path.Combine(_appData, "quarantine");
            Directory.CreateDirectory(root);
            File.Move(input.Files.Single(), Path.Combine(root, input.Manifest.Entries.Single().RelativePath));
            await _store.SetLocationAsync(input.Id, root, ArtifactLocationState.Quarantined);
        }
        else File.Delete(input.Files.Single());
        Assert.IsNull(await _store.ResolveByIdAsync(input.Id));
        var reused = await _processor.ResolveAsync(intent, default);
        Assert.AreEqual(result.Artifact!.Id, reused!.Id);
        Assert.AreEqual(1, _archives.Extractions);
    }

    [TestMethod]
    public async Task Modified_final_result_is_rebuilt_in_new_operation_generation()
    {
        var input = await Input();
        var plan = Plan();
        var intent = Intent(plan);
        var first = (await Execute(intent, input, plan)).Artifact!;
        var file = first.Files.Single();
        var timestamp = File.GetLastWriteTimeUtc(file);
        await File.WriteAllTextAsync(file, "changed"); // Same byte length as the seven-byte output.
        File.SetLastWriteTimeUtc(file, timestamp);
        Assert.IsNull(await _processor.ResolveAsync(intent, default));
        var recovered = (await Execute(intent, input, plan, "workflow:2:target")).Artifact!;
        Assert.AreEqual(2, _archives.Extractions);
        Assert.AreNotEqual(first.Id, recovered.Id);
        Assert.AreNotEqual(first.RootDirectory, recovered.RootDirectory);
        Assert.AreEqual(2, (await _store.GetOperationAsync(recovered.OperationId))!.Attempt);
        Assert.AreEqual("payload", await File.ReadAllTextAsync(recovered.Files.Single()));
    }

    [TestMethod]
    public async Task Password_wait_and_expired_lease_resume_preserve_completed_plan_steps()
    {
        var input = await Input(nested: true);
        var plan = Plan(nested: true);
        var intent = Intent(plan);
        var waiting = await Execute(intent, input, plan);
        Assert.IsNull(waiting.Artifact);
        Assert.AreEqual("PasswordUnknown", waiting.WaitReason);
        Assert.AreEqual(1, _archives.Extractions);
        var operation = await _db.Set<ArtifactOperationDbModel>().AsNoTracking().SingleAsync(o => o.IntentKey == intent);
        Assert.AreEqual(1, operation.Attempt);
        await _db.Set<ArtifactOperationDbModel>().Where(o => o.Id == operation.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(o => o.LeaseUntil, DateTime.UtcNow.AddMinutes(-1)));
        var resumed = await Execute(intent, input, plan, password: "correct");
        Assert.IsNotNull(resumed.Artifact);
        Assert.AreEqual(2, _archives.Extractions);
        Assert.AreEqual(1, _archives.OuterExtractions, "The successful first layer must not be extracted again.");
        Assert.AreEqual(1, (await _store.GetOperationAsync(operation.Id))!.Attempt);
        Assert.AreEqual("payload", await File.ReadAllTextAsync(resumed.Artifact.Files.Single()));
    }

    [TestMethod]
    public async Task DynamicProcessingBoundariesCommitSeparateArtifactsWithoutReextractingEarlierSteps()
    {
        var input = await Input();
        var plan = Plan(rename: true);
        var finalIntent = Intent(plan);
        var first = await _processor.ExecuteAsync(finalIntent + ":boundary:open", input, plan, null,
            Config, "first-node", null, null, null, default, stopAfterStepId: "open");
        Assert.IsNotNull(first.Artifact); Assert.AreEqual(1, _archives.Extractions);
        var second = await _processor.ExecuteAsync(finalIntent, input, plan, null,
            Config, "second-node", null, null, null, default, stopAfterStepId: "rename");
        Assert.IsNotNull(second.Artifact); Assert.AreEqual(1, _archives.Extractions);
        Assert.AreNotEqual(first.Artifact.Id, second.Artifact.Id);
        Assert.AreEqual("payload.txt", Path.GetFileName(first.Artifact.Files.Single()));
        Assert.AreEqual("renamed.txt", Path.GetFileName(second.Artifact.Files.Single()));
        Assert.AreEqual("rename", JsonSerializer.Deserialize<FileArtifactProcessingMetadata>(second.Artifact.MetadataJson!, Json)!.CompletedThroughStepId);
        File.Delete(input.Files.Single());
        Assert.AreEqual(second.Artifact.Id, (await _processor.ResolveAsync(finalIntent, default))!.Id);
    }

    [TestMethod]
    public async Task ModifiedDynamicFinalStartsANewCheckpointGenerationInsteadOfAdoptingChangedBytes()
    {
        var input = await Input(); var plan = Plan(rename: true); var intent = Intent(plan);
        var first = (await _processor.ExecuteAsync(intent, input, plan, null, Config, "run-one", null, null,
            null, default, stopAfterStepId: "rename")).Artifact!;
        await File.WriteAllTextAsync(first.Files.Single(), "changed");
        var second = (await _processor.ExecuteAsync(intent, input, plan, null, Config, "run-two", null, null,
            null, default, stopAfterStepId: "rename")).Artifact!;
        Assert.AreNotEqual(first.Id, second.Id);
        Assert.AreNotEqual(first.RootDirectory, second.RootDirectory);
        Assert.AreEqual("payload", await File.ReadAllTextAsync(second.Files.Single()));
        Assert.AreEqual("changed", await File.ReadAllTextAsync(first.Files.Single()), "Old outputs remain untouched.");
        Assert.AreEqual(2, _archives.Extractions);
    }

    [TestMethod]
    public async Task Different_concrete_instructions_have_distinct_intents_and_outputs()
    {
        var input = await Input();
        var firstPlan = Plan();
        var revisedPlan = Plan(rename: true);
        Assert.AreNotEqual(Intent(firstPlan), Intent(revisedPlan));
        var first = (await Execute(Intent(firstPlan), input, firstPlan)).Artifact!;
        var revised = (await Execute(Intent(revisedPlan), input, revisedPlan, "workflow:2:target")).Artifact!;
        Assert.AreNotEqual(first.OperationId, revised.OperationId);
        Assert.AreEqual("payload.txt", Path.GetFileName(first.Files.Single()));
        Assert.AreEqual("renamed.txt", Path.GetFileName(revised.Files.Single()));
        Assert.AreEqual(2, _archives.Extractions);
    }

    [DataTestMethod]
    [DataRow("unknown")]
    [DataRow("required")]
    public async Task Recursive_switch_does_not_approve_missing_initial_instructions(string requirement)
    {
        var input = await Input();
        var config = Config with {Recursive = new() {Enabled = true, MinimumFreeBytes = 0}};
        var plan = JsonSerializer.Serialize(new FileProcessingPlan {Requirement = requirement}, Json);
        var intent = FileArtifactProcessor.Intent("download-intent", plan, null, config);
        var result = await _processor.ExecuteAsync(intent, input, plan, null, config, "approval", null, null, null, default);
        Assert.IsNull(result.Artifact); Assert.AreEqual("ExtractionPlanUnknown", result.WaitReason);
        Assert.AreEqual(0, _archives.Extractions);
        Assert.IsTrue(intent.StartsWith(FileArtifactProcessor.IntentVersion, StringComparison.Ordinal));
    }

    [DataTestMethod]
    [DataRow("unknown")]
    [DataRow("required")]
    public async Task Recognizable_header_does_not_approve_missing_initial_instructions(string requirement)
    {
        var directory = Path.Combine(_root, "disguised"); Directory.CreateDirectory(directory);
        var file = Path.Combine(directory, "archive.blob");
        await File.WriteAllBytesAsync(file, Convert.FromHexString("377ABCAF271C0004"));
        var input = await _store.AdoptAsync(new("disguised", "download-disguised", "download", "header-v1"),
            new(directory, [file], Managed: true), "download-owner");
        var config = Config with {Recursive = new() {Enabled = true, MinimumFreeBytes = 0}};
        var plan = JsonSerializer.Serialize(new FileProcessingPlan {Requirement = requirement}, Json);
        var intent = FileArtifactProcessor.Intent("download-disguised", plan, null, config);
        var result = await _processor.ExecuteAsync(intent, input, plan, null, config, "approval", null, null, null, default);
        Assert.IsNull(result.Artifact); Assert.AreEqual("ExtractionPlanUnknown", result.WaitReason);
        Assert.AreEqual(0, _archives.Extractions);
        CollectionAssert.AreEqual(Convert.FromHexString("377ABCAF271C0004"), await File.ReadAllBytesAsync(file));
    }

    [TestMethod]
    public async Task Explicit_keep_preserves_archives_even_when_recursive_switch_is_enabled()
    {
        var input = await Input(true);
        var config = Config with {ArchivePolicy = "keep", Recursive = new() {Enabled = true, MinimumFreeBytes = 0}};
        var intent = FileArtifactProcessor.Intent("download-intent", "{}", null, config);
        var result = await _processor.ExecuteAsync(intent, input, "{}", null, config, "keep", null, null, null, default);
        Assert.IsNotNull(result.Artifact); Assert.AreEqual(0, _archives.Extractions);
        Assert.AreEqual("outer.zip", Path.GetFileName(result.Artifact.Files.Single()));
        Assert.AreEqual("outer", await File.ReadAllTextAsync(result.Artifact.Files.Single()));
        Assert.AreNotEqual(intent, FileArtifactProcessor.Intent("download-intent", "{}", null, config with {ArchivePolicy = "autoDetect"}));
    }

    [TestMethod]
    public async Task Explicit_detection_with_recursion_disabled_extracts_only_the_approved_initial_layer()
    {
        var input = await Input(true);
        var config = Config with {ArchivePolicy = "autoDetect", Recursive = new() {MinimumFreeBytes = 0}};
        var intent = FileArtifactProcessor.Intent("download-intent", "{}", null, config);
        var result = await _processor.ExecuteAsync(intent, input, "{}", null, config, "detect", null, null, null, default);
        Assert.IsNotNull(result.Artifact); Assert.AreEqual(1, _archives.Extractions);
        Assert.AreEqual("inner.zip", Path.GetFileName(result.Artifact.Files.Single()));
        Assert.AreEqual("inner", await File.ReadAllTextAsync(result.Artifact.Files.Single()));
    }

    [TestMethod]
    public async Task Explicit_no_extraction_plan_is_not_changed_into_automatic_detection()
    {
        var input = await Input();
        var config = Config with {Recursive = new() {Enabled = true, MinimumFreeBytes = 0}};
        const string plan = "{\"requirement\":\"notRequired\",\"steps\":[]}";
        var result = await _processor.ExecuteAsync(FileArtifactProcessor.Intent("download-intent", plan, null, config),
            input, plan, null, config, "no-extraction", null, null, null, default);
        Assert.IsNotNull(result.Artifact); Assert.AreEqual(0, _archives.Extractions);
        Assert.AreEqual("single.zip", Path.GetFileName(result.Artifact.Files.Single()));
    }

    [TestMethod]
    public async Task Recursive_instructions_do_not_expand_unselected_original_sibling_archives()
    {
        var root = Path.Combine(_root, "selected"); Directory.CreateDirectory(root);
        var first = Path.Combine(root, "a.zip"); var sibling = Path.Combine(root, "b.zip");
        await File.WriteAllTextAsync(first, "outer"); await File.WriteAllTextAsync(sibling, "single");
        var input = await _store.AdoptAsync(new("selected", "selected-intent", "download", "both"), new(root, new[] {first, sibling}), "owner");
        var plan = JsonSerializer.Serialize(new FileProcessingPlan {Requirement = "required",
            Steps = [new() {Id = "open", Op = "extractArchive", Selector = "a.zip"}]}, Json);
        var config = Config with {Recursive = new() {Enabled = true, MinimumFreeBytes = 0}};
        var intent = FileArtifactProcessor.Intent("selected-intent", plan, "correct", config);
        var result = await _processor.ExecuteAsync(intent, input, plan, "correct", config, "selection", null, null, null, default);
        Assert.IsNotNull(result.Artifact); Assert.AreEqual(2, _archives.Extractions);
        Assert.IsTrue(result.Artifact.Files.Any(f => Path.GetFileName(f) == "b.zip"));
        Assert.AreEqual("single", await File.ReadAllTextAsync(result.Artifact.Files.Single(f => Path.GetFileName(f) == "b.zip")));
    }

    [TestMethod]
    public async Task Probe_timeout_is_typed_and_retry_reuses_completed_rename_and_original_artifact()
    {
        var input = await Input();
        var plan = JsonSerializer.Serialize(new FileProcessingPlan {Requirement = "required", Steps =
        [new() {Id = "rename", Op = "renameFile", Selector = "single.zip", TargetName = "single.dat"},
            new() {Id = "open", Input = "rename", Op = "extractArchive", Selector = "single.dat"}]}, Json);
        var intent = Intent(plan);
        _archives.FailFormatProbe = true;
        var waiting = await Execute(intent, input, plan);
        Assert.IsNull(waiting.Artifact);
        Assert.AreEqual("ArchiveProbeTimedOut", waiting.WaitReason);
        Assert.AreEqual(0, _archives.Extractions);
        var statePath = Directory.GetFiles(_root, "state.json", SearchOption.AllDirectories).Single();
        var before = JsonSerializer.Deserialize<FileProcessingPlanExecutor.State>(await File.ReadAllTextAsync(statePath), Json)!;
        Assert.IsTrue(before.Outputs.ContainsKey("rename"));
        Assert.IsFalse(before.Outputs.ContainsKey("open"));
        var renamed = before.Outputs["rename"].Single().Path;
        var timestamp = File.GetLastWriteTimeUtc(renamed);
        _archives.FailFormatProbe = false;
        var operation = await _db.Set<ArtifactOperationDbModel>().AsNoTracking().SingleAsync(o => o.IntentKey == intent);
        await _db.Set<ArtifactOperationDbModel>().Where(o => o.Id == operation.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(o => o.LeaseUntil, DateTime.UtcNow.AddMinutes(-1)));
        var resumed = await Execute(intent, input, plan);
        Assert.IsNotNull(resumed.Artifact);
        Assert.AreEqual(1, _archives.Extractions);
        Assert.AreEqual(operation.Attempt, (await _store.GetOperationAsync(resumed.Artifact.OperationId))!.Attempt);
        Assert.AreEqual("single", await File.ReadAllTextAsync(input.Files.Single()));
        Assert.AreEqual("payload", await File.ReadAllTextAsync(resumed.Artifact.Files.Single()));
        // Cleanup may reclaim the private rename view, but it cannot rewrite the input artifact.
        if (File.Exists(renamed)) Assert.AreEqual(timestamp, File.GetLastWriteTimeUtc(renamed));
        Assert.AreEqual(input.Id, resumed.Artifact.ParentArtifactIds.Single());
    }

    private sealed class FixtureArchives : IArchiveExtractionService
    {
        public bool FailFormatProbe { get; set; }
        public Task<ArchiveFormatProbeResult?> ProbeAuthorizedFormatAsync(string file,
            IReadOnlyList<string?> passwords, CancellationToken ct)
        {
            if (FailFormatProbe) throw new ArchiveFormatProbeIncompleteException("Fixture timeout", ArchiveFormatProbeFailureCodes.TimedOut);
            return Task.FromResult<ArchiveFormatProbeResult?>(new(".zip"));
        }
        public int Extractions { get; private set; }
        public int OuterExtractions { get; private set; }
        public async Task<ArchivePasswordProbe> ProbePasswordAsync(string entryFile, IReadOnlyList<string?> candidates,
            Action<string>? onOutput, CancellationToken ct)
        {
            var needsPassword = await File.ReadAllTextAsync(entryFile, ct) == "inner";
            return new(!needsPassword || candidates.Contains("correct"), needsPassword ? "correct" : null,
                candidates.Where(p => p != null && p != "correct").Cast<string>().ToList(), "", "Password required.");
        }
        public async Task<ArchiveExtractionResult> ExtractAsync(ArchiveExtractionRequest request, Action<int>? onProgress, CancellationToken ct)
        {
            Extractions++;
            var content = await File.ReadAllTextAsync(request.Files.Single(), ct);
            if (content == "outer")
            {
                OuterExtractions++;
                await File.WriteAllTextAsync(Path.Combine(request.Directory, "inner.zip"), "inner", ct);
            }
            else await File.WriteAllTextAsync(Path.Combine(request.Directory, "payload.txt"), "payload", ct);
            return new(true, request.Directory, "");
        }
    }
}
