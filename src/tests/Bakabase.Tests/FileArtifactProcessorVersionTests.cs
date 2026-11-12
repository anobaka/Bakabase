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
public sealed class FileArtifactProcessorVersionTests
{
    private string _root = null!;
    private string _appData = null!;
    private BakabaseDbContext _db = null!;
    private ArtifactOperationStore _store = null!;
    private ServiceProvider _services = null!;
    private FileArtifactProcessor _processor = null!;
    private FileProcessingPlanExecutor _plans = null!;
    private RecursiveArchiveExecutor _recursive = null!;
    private readonly Archives _archives = new();
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [TestInitialize]
    public async Task Initialize()
    {
        _root = Path.Combine(Path.GetTempPath(), "file-artifact-version-" + Guid.NewGuid().ToString("N"));
        _appData = Path.Combine(_root, "appdata");
        Directory.CreateDirectory(_appData);
        var options = new DbContextOptionsBuilder<BakabaseDbContext>()
            .UseSqlite("Data Source=" + Path.Combine(_root, "test.db")).Options;
        _db = new(options);
        await _db.Database.EnsureCreatedAsync();
        _store = new(_db, () => _appData);
        _services = new ServiceCollection().AddScoped(_ => new BakabaseDbContext(options))
            .AddScoped<IArtifactOperationStore>(sp =>
                new ArtifactOperationStore(sp.GetRequiredService<BakabaseDbContext>(), () => _appData))
            .BuildServiceProvider();
        _plans = new(_archives);
        _recursive = new(_archives);
        _processor = new(_store, _plans, _recursive, () => _appData,
            _services.GetRequiredService<IServiceScopeFactory>());
    }

    [TestCleanup]
    public async Task Cleanup()
    {
        await _services.DisposeAsync();
        await _db.DisposeAsync();
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    [TestMethod]
    public async Task New_demand_ignores_old_success_but_pinned_success_and_new_reuse_remain_valid()
    {
        var input = await Input(signature: true);
        var config = new FileArtifactProcessingConfig
            {ArchivePolicy = "autoDetect", Recursive = new() {MinimumFreeBytes = 0}};
        const string plan = "{\"requirement\":\"notRequired\",\"steps\":[]}";
        var legacyIntent = LegacyIntent("download-intent", plan, config);
        var directory = Path.Combine(_appData, "old-processed");
        Directory.CreateDirectory(directory);
        var oldFile = Path.Combine(directory, "payload.s");
        File.Copy(input.Files.Single(), oldFile);
        // A valid old manifest cannot reveal that a former implementation missed an archive.
        var old = await _store.AdoptAsync(new("old-operation", legacyIntent, "files.process", input.Fingerprint),
            new(directory, [oldFile], "processedFiles", true, [input.Id]), "old-run");
        var bindings = new ArtifactOperationBindingService(_db);
        Assert.IsTrue(await bindings.BindAsync("workflow", "old-run", "target:artifact.process", old.OperationId));

        var pinned = await Execute(legacyIntent, input, plan, config, "old-run");
        Assert.AreEqual(old.Id, pinned.Artifact!.Id);
        Assert.IsTrue(await bindings.BindAsync("workflow", "old-run", "target:artifact.process",
            pinned.Artifact.OperationId));
        Assert.AreEqual(0, _archives.Extractions);

        var currentIntent = FileArtifactProcessor.Intent("download-intent", plan, null, config);
        Assert.IsTrue(legacyIntent.StartsWith(FileArtifactProcessor.IntentVersion, StringComparison.Ordinal));
        Assert.IsTrue(currentIntent.StartsWith(FileArtifactProcessor.IntentVersion, StringComparison.Ordinal));
        Assert.AreNotEqual(legacyIntent, currentIntent);
        Assert.IsNull(await _processor.ResolveAsync(currentIntent, default));
        Assert.AreNotEqual(FileArtifactPublisher.Intent(legacyIntent, Path.Combine(_root, "delivery")),
            FileArtifactPublisher.Intent(currentIntent, Path.Combine(_root, "delivery")));

        var current = await Execute(currentIntent, input, plan, config, "new-run");
        Assert.IsNotNull(current.Artifact);
        Assert.AreNotEqual(old.Id, current.Artifact.Id);
        Assert.AreEqual("payload.txt", Path.GetFileName(current.Artifact.Files.Single()));
        Assert.AreEqual(1, _archives.Extractions);
        var repeated = await Execute(currentIntent, input, plan, config, "another-new-run");
        Assert.AreEqual(current.Artifact.Id, repeated.Artifact!.Id);
        Assert.AreEqual(1, _archives.Extractions);
        Assert.IsNotNull(await _store.ResolveByIdAsync(input.Id), "The download artifact remains reusable.");
        Assert.IsTrue(File.Exists(oldFile), "Versioning must not delete or mutate historical outputs.");
    }

    [TestMethod]
    public async Task Legacy_plan_password_checkpoint_keeps_operation_attempt_and_completed_steps()
    {
        var input = await Input();
        var config = new FileArtifactProcessingConfig();
        var plan = Plan(nestedStep: true);
        var legacyIntent = LegacyIntent("download-intent", plan, config);
        var (key, directory) = await ClaimLegacy(legacyIntent, input, plan, config);
        var checkpoint = await _plans.ExecuteAsync(JsonSerializer.Deserialize<FileProcessingPlan>(plan, Json)!,
            input.RootDirectory, input.Files, Path.Combine(directory, "plan"));
        Assert.IsFalse(checkpoint.Completed);
        Assert.IsTrue(checkpoint.NeedsPassword);
        Assert.AreEqual(1, _archives.OuterExtractions);
        await Expire(key);

        var resumed = await Execute(legacyIntent, input, plan, config, "old-run", planPassword: "correct");

        Assert.IsNotNull(resumed.Artifact);
        Assert.AreEqual(key, resumed.Artifact.OperationId, "A recipe revision must not change a pinned operation key.");
        Assert.AreEqual(1, (await _store.GetOperationAsync(key))!.Attempt);
        Assert.AreEqual(1, _archives.OuterExtractions);
        Assert.AreEqual(2, _archives.Extractions);
        Assert.AreEqual(1, await _db.Set<ArtifactOperationDbModel>().CountAsync(o => o.IntentKey == legacyIntent));
    }

    [TestMethod]
    public async Task Legacy_recursive_password_answer_reaches_its_original_archive_checkpoint()
    {
        var input = await Input();
        var config = new FileArtifactProcessingConfig
            {Recursive = new() {Enabled = true, MinimumFreeBytes = 0}};
        var plan = Plan(nestedStep: false);
        var legacyIntent = LegacyIntent("download-intent", plan, config);
        var (key, directory) = await ClaimLegacy(legacyIntent, input, plan, config);
        var planned = await _plans.ExecuteAsync(JsonSerializer.Deserialize<FileProcessingPlan>(plan, Json)!,
            input.RootDirectory, input.Files, Path.Combine(directory, "plan"));
        Assert.IsTrue(planned.Completed);
        var checkpoint = await _recursive.ExecuteAsync(planned.OutputDirectory!, planned.Files,
            Path.Combine(directory, "recursive"), config.Recursive, archiveCandidates: planned.ExtractedFiles);
        Assert.IsFalse(checkpoint.Completed);
        Assert.AreEqual("PasswordUnknown", checkpoint.WaitReason);
        Assert.IsNotNull(checkpoint.ArchiveId);
        await Expire(key);

        var resumed = await Execute(legacyIntent, input, plan, config, "old-run",
            archiveAnswer: new(checkpoint.ArchiveId, "correct"));

        Assert.IsNotNull(resumed.Artifact);
        Assert.AreEqual(key, resumed.Artifact.OperationId);
        Assert.AreEqual(1, (await _store.GetOperationAsync(key))!.Attempt);
        Assert.AreEqual(1, _archives.OuterExtractions);
        Assert.AreEqual(2, _archives.Extractions);
        Assert.AreEqual("payload", await File.ReadAllTextAsync(resumed.Artifact.Files.Single()));
    }

    private async Task<ArtifactRecord> Input(bool signature = false)
    {
        var root = Path.Combine(_root, "downloads");
        Directory.CreateDirectory(root);
        var file = Path.Combine(root, signature ? "payload.s" : "outer.zip");
        if (signature) await File.WriteAllBytesAsync(file, [0x50, 0x4b, 0x03, 0x04, 0, 0, 0, 0]);
        else await File.WriteAllTextAsync(file, "outer");
        return await _store.AdoptAsync(new("download", "download-intent", "download", "source-v1"),
            new(root, [file], Managed: true), "download-owner");
    }

    private static string Plan(bool nestedStep)
    {
        var steps = new List<FileProcessingStep> {new() {Id = "open", Op = "extractArchive", Selector = "*.zip"}};
        if (nestedStep) steps.Add(new() {Id = "inner", Input = "open", Op = "extractArchive", Selector = "inner.zip"});
        return JsonSerializer.Serialize(new FileProcessingPlan {Requirement = "required", Steps = steps}, Json);
    }

    private async Task<(string Key, string Directory)> ClaimLegacy(string intent, ArtifactRecord input,
        string plan, FileArtifactProcessingConfig config)
    {
        var key = ArtifactManifestFiles.Hash(intent + "\n" + input.Fingerprint + "\n" +
            LegacyIntent("resolved-recipe", plan, config));
        var claim = await _store.ClaimAsync(new(key, intent, "files.process", input.Fingerprint),
            "old-run", TimeSpan.FromMinutes(2));
        Assert.AreEqual(ArtifactClaimStatus.Acquired, claim.Status);
        var directory = Path.Combine(_appData, "artifact-operations", key, "1");
        Directory.CreateDirectory(directory);
        return (key, directory);
    }

    private Task<int> Expire(string key) => _db.Set<ArtifactOperationDbModel>().Where(o => o.Id == key)
        .ExecuteUpdateAsync(s => s.SetProperty(o => o.LeaseUntil, DateTime.UtcNow.AddMinutes(-1)));

    private Task<FileArtifactProcessingResult> Execute(string intent, ArtifactRecord input, string plan,
        FileArtifactProcessingConfig config, string consumer, string? planPassword = null,
        RecursiveArchivePasswordAnswer? archiveAnswer = null) =>
        _processor.ExecuteAsync(intent, input, plan, null, config, consumer, archiveAnswer, planPassword, null, default);

    // Fixed pre-upgrade serialization, deliberately independent of the current Intent implementation.
    private static string LegacyIntent(string input, string planJson, FileArtifactProcessingConfig config)
    {
        var plan = JsonSerializer.Deserialize<FileProcessingPlan>(planJson, Json)!;
        var instructions = new {plan.Requirement, Steps = plan.Steps.Select(s => new
            {s.Id, s.Op, s.Input, s.Selector, s.Extension, s.TargetName, s.TargetDirectory, s.Password})};
        return "files-v2:" + ArtifactManifestFiles.Hash(JsonSerializer.Serialize(new
        {
            Version = "file-artifacts-v2", Input = input, Instructions = instructions, config.ArchivePolicy,
            Recursive = config.Recursive.Enabled, Password = (string?)null, Force = (string?)null
        }, Json));
    }

    private sealed class Archives : IArchiveExtractionService
    {
        public int Extractions { get; private set; }
        public int OuterExtractions { get; private set; }
        public Task<ArchivePasswordProbe> ProbePasswordAsync(string entryFile, IReadOnlyList<string?> candidates,
            Action<string>? onOutput, CancellationToken ct)
        {
            var needsPassword = Path.GetFileName(entryFile) == "inner.zip";
            return Task.FromResult(new ArchivePasswordProbe(!needsPassword || candidates.Contains("correct"),
                needsPassword ? "correct" : null, [], "", "Password required."));
        }
        public async Task<ArchiveExtractionResult> ExtractAsync(ArchiveExtractionRequest request,
            Action<int>? onProgress, CancellationToken ct)
        {
            Extractions++;
            if (Path.GetFileName(request.Files.Single()) == "outer.zip")
            {
                OuterExtractions++;
                await File.WriteAllTextAsync(Path.Combine(request.Directory, "inner.zip"), "inner", ct);
            }
            else await File.WriteAllTextAsync(Path.Combine(request.Directory, "payload.txt"), "payload", ct);
            return new(true, request.Directory, "");
        }
    }
}
