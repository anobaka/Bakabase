using System.Text.Json;
using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.InsideWorld.Business;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.InsideWorld.Business.Components.Compression;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Service.Components.FileProcessing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Tests.Artifacts;

[TestClass]
public sealed class FileDispositionIntegrationTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private string _root = null!, _appData = null!;
    private BakabaseDbContext _db = null!;
    private ArtifactOperationStore _store = null!;
    private ServiceProvider _services = null!;
    private FileArtifactProcessor _processor = null!;
    private readonly Archives _archives = new();
    private FileArtifactPublisher Publisher => new(_store, new Storage(), () => _appData,
        _services.GetRequiredService<IServiceScopeFactory>());
    private ArchiveQuarantineService CleanupService => new(_store, _appData, new Storage());

    [TestInitialize]
    public async Task Initialize()
    {
        _root = Path.Combine(Path.GetTempPath(), "file-disposition-integration-" + Guid.NewGuid().ToString("N"));
        _appData = Path.Combine(_root, "appdata"); Directory.CreateDirectory(_appData);
        var options = new DbContextOptionsBuilder<BakabaseDbContext>()
            .UseSqlite("Data Source=" + Path.Combine(_root, "isolated.db")).Options;
        _db = new(options); await _db.Database.EnsureCreatedAsync();
        _store = new(_db, () => _appData);
        _services = new ServiceCollection().AddScoped(_ => new BakabaseDbContext(options))
            .AddScoped<IArtifactOperationStore>(sp => new ArtifactOperationStore(sp.GetRequiredService<BakabaseDbContext>(), () => _appData))
            .BuildServiceProvider();
        _processor = new(_store, new FileProcessingPlanExecutor(_archives), new RecursiveArchiveExecutor(_archives),
            () => _appData, _services.GetRequiredService<IServiceScopeFactory>());
    }

    [TestCleanup]
    public async Task Cleanup()
    {
        await _services.DisposeAsync(); await _db.DisposeAsync();
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Measured_execution_publication_and_cleanup_preserve_unknown_keys(bool recursive)
    {
        var input = await Input(recursive ? "outer" : "inner");
        var processed = await Process(input, ExtractionPlan(), recursive: recursive);
        var final = await Publish(processed, "delivery");
        var proof = JsonSerializer.Deserialize<FileArtifactProcessingMetadata>(processed.MetadataJson!, Json)!.FileDisposition;
        Assert.IsTrue(FileDispositionProofs.ValidFor(proof, input, processed));
        Assert.AreEqual("preserved", proof!.Entries.Single(e => e.Source.RelativePath == "license.key").Disposition);
        Assert.AreEqual("replaced", proof.Entries.Single(e => e.Source.RelativePath == "archive.zip").Disposition);
        Assert.AreEqual("external-key", await File.ReadAllTextAsync(Path.Combine(final.RootDirectory, "license.key")));
        if (recursive) Assert.IsTrue(final.Manifest.Entries.Any(e => e.RelativePath.EndsWith("inside.key")));
        Assert.IsTrue((await ArtifactFileCoverage.EvaluateAsync(_store, input, [final.Id])).Complete);

        var deleted = await CleanupService.DeleteAfterDeliveryAsync(input.Id, [final.Id]);
        Assert.IsTrue(deleted.Completed, deleted.Message);
        Assert.IsTrue(input.Files.All(f => !File.Exists(f)));
        Assert.IsTrue((await ArtifactManifestFiles.ValidateAsync(final.RootDirectory, final.Manifest)).Available);
        Assert.IsNotNull(await _store.GetArtifactAsync(input.Id), "History survives file retirement.");
        Assert.AreEqual(recursive ? 2 : 1, _archives.Extractions);
    }

    [TestMethod]
    [DataRow("license.key")]
    [DataRow("config.dat")]
    public async Task Valid_but_incomplete_publication_cannot_delete_key_or_archive(string omittedName)
    {
        var input = await Input("inner");
        var processed = await Process(input, ExtractionPlan());
        var subset = processed.Files.Where(p => Path.GetFileName(p) != omittedName).ToArray();
        var partial = await _store.AdoptAsync(new("partial", "partial", "custom.selection", processed.Fingerprint),
            new(processed.RootDirectory, subset, "processedFiles", true, [processed.Id]), "selector");
        var final = await Publish(partial, "partial-delivery");
        Assert.IsTrue((await ArtifactManifestFiles.ValidateAsync(final.RootDirectory, final.Manifest)).Available);
        var result = await CleanupService.DeleteAfterDeliveryAsync(input.Id, [final.Id]);
        Assert.IsFalse(result.Completed);
        StringAssert.Contains(result.Message!, ArtifactFileCoverage.IncompleteCode);
        Assert.IsTrue(input.Files.All(File.Exists));
        Assert.AreEqual(ArtifactLocationState.Available, (await _store.GetArtifactAsync(input.Id))!.State);
        Assert.AreEqual(0, (await CleanupService.GetReceiptsAsync()).Count, "Unknown coverage must not first quarantine originals.");
    }

    [TestMethod]
    public async Task Dynamic_boundaries_then_nested_processing_keep_receipts_without_repeating_extraction()
    {
        var input = await Input("outer");
        var plan = JsonSerializer.Serialize(new FileProcessingPlan {Requirement = "required", Steps =
        [new() {Id = "open", Op = "extractArchive", Selector = "*.zip"},
            new() {Id = "rename", Input = "open", Op = "renameFile", Selector = "inner.zip", TargetName = "renamed.zip"}]}, Json);
        var first = await Process(input, plan, boundary: "open");
        var finalBoundary = await Process(input, plan, boundary: "rename");
        Assert.AreEqual(1, _archives.Extractions);
        var originalProof = JsonSerializer.Deserialize<FileArtifactProcessingMetadata>(first.MetadataJson!, Json)!.FileDisposition!;
        Assert.IsTrue(originalProof.Entries.Single(e => e.Source.RelativePath == "archive.zip").Outputs.Any(e => e.RelativePath.EndsWith("inner.zip")),
            "The earlier boundary's immutable proof must not use the later active filenames.");
        var finalProcessing = await Process(finalBoundary, ExtractionPlan());
        var published = await Publish(finalProcessing, "dynamic-delivery");
        Assert.AreEqual(2, _archives.Extractions);
        Assert.IsTrue((await ArtifactFileCoverage.EvaluateAsync(_store, input, [published.Id])).Complete);
        Assert.IsTrue((await CleanupService.DeleteAfterDeliveryAsync(input.Id, [published.Id])).Completed);
        Assert.IsTrue((await ArtifactManifestFiles.ValidateAsync(published.RootDirectory, published.Manifest)).Available);
        Assert.IsNotNull(await _store.GetArtifactAsync(first.Id));
    }

    [TestMethod]
    public async Task Legacy_deleting_receipt_without_measured_replacement_proof_stops_before_unlink()
    {
        var input = await Input("inner");
        var processed = await Process(input, ExtractionPlan());
        var final = await Publish(processed, "legacy-delivery");
        var quarantine = await CleanupService.QuarantineAsync(input.Id, [final.Id]);
        Assert.IsTrue(quarantine.Completed, quarantine.Message);
        var receiptPath = Path.Combine(_appData, "file-processing", "quarantine-receipts", quarantine.ReceiptId! + ".json");
        var receipt = JsonSerializer.Deserialize<ArchiveQuarantineService.Receipt>(await File.ReadAllTextAsync(receiptPath), Json)!;
        receipt.Status = "Deleting";
        await File.WriteAllTextAsync(receiptPath, JsonSerializer.Serialize(receipt, Json));
        // Only the isolated test ledger is changed, emulating an old build which had no proof.
        await _db.Set<ArtifactDbModel>().Where(a => a.Id == processed.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.MetadataJson, (string?)null));
        var current = (await _store.GetArtifactAsync(input.Id))!;
        var result = await CleanupService.DeleteAfterDeliveryAsync(input.Id, [final.Id]);
        Assert.IsFalse(result.Completed);
        StringAssert.Contains(result.Message!, ArtifactFileCoverage.IncompleteCode);
        Assert.IsTrue(current.Files.All(File.Exists));
        Assert.AreEqual("Deleting", (await CleanupService.GetReceiptsAsync()).Single().Status);
        Assert.IsTrue((await ArtifactManifestFiles.ValidateAsync(final.RootDirectory, final.Manifest)).Available);
    }

    private async Task<ArtifactRecord> Input(string content)
    {
        var root = Path.Combine(_root, "downloads"); Directory.CreateDirectory(root);
        var zip = Path.Combine(root, "archive.zip"); var key = Path.Combine(root, "license.key");
        await File.WriteAllTextAsync(zip, content); await File.WriteAllTextAsync(key, "external-key");
        return await _store.AdoptAsync(new("download", "download-intent", "download", "external-source"),
            new(root, [zip, key], "downloadedFiles", true), "download-owner");
    }

    private static string ExtractionPlan() => JsonSerializer.Serialize(new FileProcessingPlan {Requirement = "required",
        Steps = [new() {Id = "open", Op = "extractArchive", Selector = "*.zip"}]}, Json);

    private async Task<ArtifactRecord> Process(ArtifactRecord input, string plan, bool recursive = false, string? boundary = null)
    {
        var config = new FileArtifactProcessingConfig {Recursive = new() {Enabled = recursive, MinimumFreeBytes = 0}};
        var intent = FileArtifactProcessor.Intent(input.Id, plan, null, config) + (boundary == null ? "" : ":boundary:" + boundary);
        var result = await _processor.ExecuteAsync(intent, input, plan, null, config, "processor", null, null, null,
            default, stopAfterStepId: boundary);
        Assert.IsNotNull(result.Artifact, result.Message); return result.Artifact;
    }

    private async Task<ArtifactRecord> Publish(ArtifactRecord input, string name)
    {
        var result = await Publisher.PublishAsync(input.Id, input, Path.Combine(_root, name), "publisher");
        Assert.IsNotNull(result.Artifact, result.Message); return result.Artifact;
    }

    private sealed class Archives : IArchiveExtractionService
    {
        public int Extractions { get; private set; }
        public Task<ArchivePasswordProbe> ProbePasswordAsync(string entryFile, IReadOnlyList<string?> candidates,
            Action<string>? onOutput, CancellationToken ct) => Task.FromResult(new ArchivePasswordProbe(true, null, [], "", ""));
        public Task<ArchiveFormatProbeResult?> ProbeAuthorizedFormatAsync(string path, IReadOnlyList<string?> passwords,
            CancellationToken ct) => Task.FromResult<ArchiveFormatProbeResult?>(new(".zip"));
        public async Task<ArchiveExtractionResult> ExtractAsync(ArchiveExtractionRequest request, Action<int>? progress, CancellationToken ct)
        {
            Extractions++;
            var value = await File.ReadAllTextAsync(request.Files.Single(), ct);
            if (value == "outer")
            {
                await File.WriteAllTextAsync(Path.Combine(request.Directory, "inner.zip"), "inner", ct);
                await File.WriteAllTextAsync(Path.Combine(request.Directory, "inside.key"), "nested-key", ct);
            }
            else
            {
                await File.WriteAllTextAsync(Path.Combine(request.Directory, "program.txt"), "program", ct);
                await File.WriteAllTextAsync(Path.Combine(request.Directory, "config.dat"), "config", ct);
            }
            return new(true, request.Directory, "");
        }
    }

    private sealed class Storage : IUserStoragePolicy
    {
        public bool IsRestricted => false;
        public IReadOnlyList<UserStorageRoot> GetRoots(UserStoragePurpose purpose = UserStoragePurpose.UserFiles) => [];
        public bool IsPathAllowed(string path, UserStoragePurpose purpose = UserStoragePurpose.UserFiles) => true;
        public void EnsurePathAllowed(string path, UserStoragePurpose purpose = UserStoragePurpose.UserFiles) { }
        public void EnsureTreeMutationAllowed(string path) { }
    }
}
