using System.Text.Json;
using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.InsideWorld.Business.Components.Compression;
using Bakabase.Modules.Downloader.Cloud;
using Bakabase.Modules.PostParser.Models.Domain;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Service.Components.CloudDownloads;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bakabase.Tests.CloudDownloads;

[TestClass]
public sealed class CloudFileProcessingTests
{
    private string _root = null!;
    private string _downloads = null!;
    private string _appData = null!;
    private ServiceProvider _services = null!;
    private readonly FixtureArchives _archives = new();
    private ProcessCloudFilesActivity Activity => new(_ => _appData);

    [TestInitialize]
    public void Initialize()
    {
        _root = Path.Combine(Path.GetTempPath(), "cloud-processing-" + Guid.NewGuid().ToString("N"));
        _downloads = Path.Combine(_root, "downloads");
        _appData = Path.Combine(_root, "appdata");
        Directory.CreateDirectory(_downloads);
        Directory.CreateDirectory(_appData);
        _services = new ServiceCollection()
            .AddSingleton<IArchiveExtractionService>(_archives)
            .AddSingleton<IUserStoragePolicy>(new RestrictedStorage(_downloads))
            .BuildServiceProvider();
    }

    [TestCleanup]
    public void Cleanup()
    {
        _services.Dispose();
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    private WorkflowExecutionContext Context(DownloadWorkset item) => new()
    {
        RunId = 7201, WorkflowDefinitionId = 1, TriggerKind = CloudWorkflow.Trigger,
        Payload = item, ActivityConfigJson = "{}", Services = _services, Logger = NullLogger.Instance
    };
    private static string Json<T>(T value) => JsonSerializer.Serialize(value, WorkflowJson.Options);
    private static DownloadWorkset Item(WorkflowItemOutcome outcome) =>
        (DownloadWorkset)(outcome.Suspension?.Item ?? outcome.Replacement!);
    private static DownloadWorkset Snapshot(DownloadWorkset item) =>
        JsonSerializer.Deserialize<DownloadWorkset>(Json(item), WorkflowJson.Options)!;
    private static PostExtractionPlan Extract(string? password = null) => new()
    {Requirement = "required", Steps = [new() {Id = "open", Op = "extractArchive", Password = password}]};
    private DownloadWorkset Workset(params CloudDownloadTarget[] targets) =>
        new() {Id = "workset", OutputDirectory = _downloads, Targets = targets.ToList()};

    private async Task<CloudDownloadTarget> Target(string id, PostExtractionPlan? plan, string? password = null,
        string name = "archive.zip")
    {
        var directory = Path.Combine(_downloads, id);
        Directory.CreateDirectory(directory);
        var file = Path.Combine(directory, name);
        await File.WriteAllTextAsync(file, "original-" + id);
        var source = new CloudSource {Id = "source-" + id, ProviderId = "gofile", Url = "https://gofile.io/d/" + id,
            ArchivePassword = password, ExtractionPlanJson = plan == null ? null : Json(plan)};
        return new() {Id = id, Title = id, Sources = [source], SelectedSourceId = source.Id, Files = [file],
            DownloadDirectory = directory, DownloadTaskId = id.GetHashCode()};
    }

    [TestMethod]
    public async Task Each_target_uses_only_its_manifest_and_preserves_originals_and_excluded_siblings()
    {
        var first = await Target("first", Extract(), "correct");
        var second = await Target("second", new() {Requirement = "notRequired"}, name: "readme.txt");
        var excluded = (await Target("excluded", Extract("wrong"))) with {Excluded = true};
        var unrelated = Path.Combine(Path.GetDirectoryName(first.Files[0])!, "unrelated.txt");
        await File.WriteAllTextAsync(unrelated, "unrelated");
        var item = Workset(first, second, excluded);
        var result = await Activity.ProcessItemAsync(Context(item), item, default);
        Assert.IsNull(result.Suspension);
        var output = Item(result);
        Assert.AreEqual(1, _archives.Extractions);
        foreach (var target in output.Targets.Take(2))
        {
            Assert.AreEqual(1, target.ProcessedFiles.Count);
            Assert.IsTrue(target.ProcessedDirectory!.StartsWith(Path.Combine(_appData, "file-processing", "cloud", "7201")));
            CollectionAssert.AreEqual(item.Targets.Single(t => t.Id == target.Id).Files, target.Files);
            Assert.AreEqual("original-" + target.Id, await File.ReadAllTextAsync(target.Files[0]));
            Assert.IsFalse(target.ProcessedFiles.Any(f => Path.GetFileName(f) == "unrelated.txt"));
        }
        Assert.AreEqual(excluded, output.Targets[2]);
        Assert.AreEqual("unrelated", await File.ReadAllTextAsync(unrelated));
        using var summary = JsonDocument.Parse(Json(output.ToWorkflowOutputSummary()));
        Assert.AreEqual(1, summary.RootElement.GetProperty("targets")[0].GetProperty("processedFileCount").GetInt32());
        Assert.AreEqual(output.Targets[0].ProcessedDirectory,
            summary.RootElement.GetProperty("targets")[0].GetProperty("processedDirectory").GetString());
    }

    [TestMethod]
    public async Task Password_resume_preserves_completed_siblings_and_does_not_repeat_extraction()
    {
        var first = await Target("done", Extract("correct"), "unused-fallback");
        var second = await Target("waiting", Extract("wrong"), "correct");
        var item = Workset(first, second);
        var stopped = await Activity.ProcessItemAsync(Context(item), item, default);
        Assert.AreEqual("cloud.fileProcessing", stopped.Suspension?.Reason);
        var saved = Snapshot(Item(stopped));
        Assert.IsNotNull(saved.Targets[0].ProcessedDirectory);
        Assert.IsNull(saved.Targets[1].ProcessedDirectory);
        Assert.AreEqual("PasswordUnknown", saved.Targets[1].ProcessingWaitReason);
        var prompt = JsonSerializer.Deserialize<CloudFileProcessingPrompt>(stopped.Suspension!.PromptJson!, WorkflowJson.Options)!;
        Assert.AreEqual("waiting", prompt.TargetId);
        CollectionAssert.AreEqual(new[] {"wrong"}, prompt.Tried!.ToArray());
        Assert.AreEqual(1, _archives.Extractions, "An explicit step password must take precedence over the source fallback.");
        await Assert.ThrowsExceptionAsync<ArgumentException>(() => Activity.ResumeAsync(Context(saved), saved,
            Json(new CloudFileProcessingSignal {TargetId = "done", Password = "correct"}), default));
        var resumed = await Activity.ResumeAsync(Context(saved), saved,
            Json(new CloudFileProcessingSignal {TargetId = "waiting", Password = "correct"}), default);
        Assert.IsNull(resumed.Suspension);
        Assert.AreEqual(2, _archives.Extractions);
        Assert.AreEqual(saved.Targets[0].ProcessedDirectory, Item(resumed).Targets[0].ProcessedDirectory);
        CollectionAssert.AreEqual(saved.Targets[0].ProcessedFiles, Item(resumed).Targets[0].ProcessedFiles);
        var repeated = await Activity.ProcessItemAsync(Context(Item(resumed)), Snapshot(Item(resumed)), default);
        Assert.IsNull(repeated.Suspension);
        Assert.AreEqual(2, _archives.Extractions);
        Assert.IsTrue(item.Targets.SelectMany(t => t.Files).All(File.Exists));
    }

    [TestMethod]
    public async Task Missing_and_incomplete_plans_wait_and_accept_only_the_current_target_answer()
    {
        var first = await Target("first", new() {Requirement = "notRequired"}, name: "first.txt");
        var second = await Target("no-plan", null, "correct");
        var item = Workset(first, second);
        var stopped = await Activity.ProcessItemAsync(Context(item), item, default);
        var saved = Snapshot(Item(stopped));
        Assert.AreEqual("ExtractionPlanUnknown", saved.Targets[1].ProcessingWaitReason);
        Assert.AreEqual(0, _archives.Extractions);
        Assert.IsNotNull(saved.Targets[0].ProcessedDirectory);
        await Assert.ThrowsExceptionAsync<ArgumentException>(() => Activity.ResumeAsync(Context(saved), saved,
            Json(new CloudFileProcessingSignal {TargetId = "not-in-this-run", ExtractionPlanJson = Json(Extract())}), default));
        var incomplete = await Activity.ResumeAsync(Context(saved), saved,
            Json(new CloudFileProcessingSignal {TargetId = second.Id, ExtractionPlanJson = Json(new PostExtractionPlan {Requirement = "required"})}), default);
        Assert.AreEqual("ExtractionPlanUnknown", Item(incomplete).Targets[1].ProcessingWaitReason);
        var resumed = await Activity.ResumeAsync(Context(Item(incomplete)), Snapshot(Item(incomplete)),
            Json(new CloudFileProcessingSignal {TargetId = second.Id, ExtractionPlanJson = Json(Extract())}), default);
        Assert.IsNull(resumed.Suspension);
        Assert.AreEqual(1, _archives.Extractions);
        Assert.AreEqual("correct", JsonSerializer.Deserialize<PostExtractionPlan>(Item(resumed).Targets[1].ProcessingPlanJson!, WorkflowJson.Options)!.Steps[0].Password);
        Assert.AreEqual(saved.Targets[0].ProcessedDirectory, Item(resumed).Targets[0].ProcessedDirectory);
    }

    [DataTestMethod]
    [DataRow("missing")]
    [DataRow("outside")]
    [DataRow("empty")]
    [DataRow("duplicate")]
    [DataRow("symbolic-link")]
    public async Task Invalid_manifests_are_rejected_before_any_processing(string mode)
    {
        var target = await Target("invalid", Extract("correct"));
        var outside = Path.Combine(_root, "unrelated.zip");
        await File.WriteAllTextAsync(outside, "do-not-touch");
        string[] files = mode switch
        {
            "missing" => [Path.Combine(_downloads, "missing.zip")],
            "outside" => [outside], "empty" => [], "duplicate" => [target.Files[0], target.Files[0]],
            _ => target.Files.ToArray()
        };
        if (mode == "symbolic-link")
        {
            if (OperatingSystem.IsWindows()) Assert.Inconclusive("This symlink fixture requires Unix symlink creation.");
            var link = Path.Combine(_downloads, "linked.zip");
            File.CreateSymbolicLink(link, outside);
            files = [link];
        }
        var item = Workset(target with {Files = files.ToList()});
        await Assert.ThrowsExceptionAsync<InvalidDataException>(() => Activity.ProcessItemAsync(Context(item), item, default));
        Assert.AreEqual(0, _archives.Extractions);
        Assert.AreEqual("do-not-touch", await File.ReadAllTextAsync(outside));
        Assert.AreEqual(0, Directory.GetFileSystemEntries(_appData).Length);
    }

    [TestMethod]
    public async Task Restricted_input_and_linked_state_directory_are_rejected()
    {
        var target = await Target("state", Extract("correct"));
        var outside = Workset(target) with {OutputDirectory = _root};
        await Assert.ThrowsExceptionAsync<UnauthorizedAccessException>(() => Activity.ProcessItemAsync(Context(outside), outside, default));
        if (OperatingSystem.IsWindows()) return;
        Directory.CreateSymbolicLink(Path.Combine(_appData, "file-processing"), _downloads);
        var item = Workset(target);
        await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => Activity.ProcessItemAsync(Context(item), item, default));
        Assert.AreEqual(0, _archives.Extractions);
    }

    [TestMethod]
    public async Task Invalid_plan_waits_for_correction_without_touching_downloads()
    {
        var target = await Target("invalid-plan", Extract());
        var source = target.SelectedSource! with {ExtractionPlanJson = "{not json}"};
        var item = Workset(target with {Sources = [source]});
        var result = await Activity.ProcessItemAsync(Context(item), item, default);
        Assert.AreEqual("ExtractionPlanUnknown", Item(result).Targets[0].ProcessingWaitReason);
        Assert.AreEqual(0, _archives.Extractions);
        Assert.AreEqual("original-invalid-plan", await File.ReadAllTextAsync(target.Files[0]));
    }

    [TestMethod]
    public async Task Download_root_preserves_nested_paths_and_rejects_another_targets_files()
    {
        var target = await Target("nested", new() {Requirement = "notRequired"});
        var nested = Path.Combine(target.DownloadDirectory!, "folder");
        Directory.CreateDirectory(nested);
        var file = Path.Combine(nested, "readme.txt");
        await File.WriteAllTextAsync(file, "nested-content");
        target = target with {Files = [file]};
        var item = Workset(target);
        var processed = Item(await Activity.ProcessItemAsync(Context(item), item, default)).Targets.Single();
        Assert.AreEqual(Path.Combine("folder", "readme.txt"),
            Path.GetRelativePath(processed.ProcessedDirectory!, processed.ProcessedFiles.Single()));
        var sibling = await Target("sibling", new() {Requirement = "notRequired"});
        var invalid = Workset(target with {Files = sibling.Files});
        await Assert.ThrowsExceptionAsync<InvalidDataException>(() => Activity.ProcessItemAsync(Context(invalid), invalid, default));
    }

    [TestMethod]
    public async Task Historical_snapshot_without_download_root_processes_only_its_explicit_files()
    {
        var target = (await Target("historical", new() {Requirement = "notRequired"})) with {DownloadDirectory = null};
        var unrelated = Path.Combine(Path.GetDirectoryName(target.Files[0])!, "unrelated.txt");
        await File.WriteAllTextAsync(unrelated, "unrelated");
        var item = Workset(target);
        var processed = Item(await Activity.ProcessItemAsync(Context(item), Snapshot(item), default)).Targets.Single();
        Assert.AreEqual(1, processed.ProcessedFiles.Count);
        Assert.AreEqual("archive.zip", Path.GetFileName(processed.ProcessedFiles[0]));
    }

    private sealed class RestrictedStorage(string root) : IUserStoragePolicy
    {
        public bool IsRestricted => true;
        public IReadOnlyList<UserStorageRoot> GetRoots(UserStoragePurpose purpose = UserStoragePurpose.UserFiles) => [];
        public bool IsPathAllowed(string path, UserStoragePurpose purpose = UserStoragePurpose.UserFiles) =>
            Path.GetFullPath(path) == root || Path.GetFullPath(path).StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal);
        public void EnsurePathAllowed(string path, UserStoragePurpose purpose = UserStoragePurpose.UserFiles)
        {if (!IsPathAllowed(path, purpose)) throw new UnauthorizedAccessException("Path outside the configured download root.");}
        public void EnsureTreeMutationAllowed(string path) => EnsurePathAllowed(path);
    }

    private sealed class FixtureArchives : IArchiveExtractionService
    {
        public int Extractions { get; private set; }
        public Task<ArchivePasswordProbe> ProbePasswordAsync(string entryFile, IReadOnlyList<string?> candidates,
            Action<string>? onOutput, CancellationToken ct) => Task.FromResult(new ArchivePasswordProbe(
                candidates.Contains("correct"), "correct", candidates.Where(p => p != null && p != "correct").Cast<string>().ToList(), "", "Password required."));
        public async Task<ArchiveExtractionResult> ExtractAsync(ArchiveExtractionRequest request, Action<int>? onProgress, CancellationToken ct)
        {
            Extractions++;
            Assert.IsFalse(request.DeleteAfterDecompression);
            await File.WriteAllTextAsync(Path.Combine(request.Directory, "payload.txt"), "extracted-payload", ct);
            return new(true, request.Directory, "");
        }
    }
}
