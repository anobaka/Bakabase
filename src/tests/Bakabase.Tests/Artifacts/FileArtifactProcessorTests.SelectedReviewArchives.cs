using System.IO.Compression;
using System.Text.Json;
using Bakabase.InsideWorld.Business.Components.Compression;
using Microsoft.Extensions.DependencyInjection;
using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Service.Components.FileProcessing;

namespace Bakabase.Tests.Artifacts;

public sealed partial class FileArtifactProcessorTests
{
    [TestMethod]
    [DataRow("authorized", true)]
    [DataRow("wait-retry", true)]
    [DataRow("tampered", true)]
    [DataRow("unregistered-producer", false)]
    [DataRow("copy", false)]
    [DataRow("keep", false)]
    [DataRow("split", false)]
    [DataRow("unscoped", false)]
    [DataRow("non-review", false)]
    public async Task Explicit_review_borrows_only_durable_owned_standalone_archives(string mode, bool expected)
    {
        if (OperatingSystem.IsWindows()) return;
        var root = Path.Combine(_root, "original");
        Directory.CreateDirectory(root);
        var zip = Path.Combine(root, "media.zip.pdf");
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
        using (var stream = archive.CreateEntry("movie.mp4").Open()) stream.Write([1, 2, 3]);
        if (mode == "split")
        {
            byte[] bytes = await File.ReadAllBytesAsync(zip);
            // EOCD disk number names another volume; it cannot prove a standalone archive.
            bytes[bytes.Length - 18] = 1;
            await File.WriteAllBytesAsync(zip, bytes);
        }
        var note = Path.Combine(root, "ordinary.pdf");
        await File.WriteAllTextAsync(note, "independent attachment");
        var source = await _store.AdoptAsync(new("review-origin", "review-origin", "download", "source"),
            new(root, [zip, note], Managed: true), "origin-consumer");
        // Freezing a normal final processing boundary produces a real store operation, owned
        // location and manifest. No file is copied into the caller's control to fake ownership.
        const string plan = """{"requirement":"required","steps":[{"id":"first","op":"renameFile","selector":"ordinary.pdf","targetName":"renamed.pdf"}]}""";
        var kept = await _processor.ExecuteAsync(Intent(plan), source, plan, null, Config, "producer", null, null, null,
            default, replan: false, stopAfterStepId: "first", recovery: null);
        Assert.IsNotNull(kept.Artifact, kept.Message);
        var input = kept.Artifact!;
        if (mode == "unregistered-producer")
            input = await _store.AdoptAsync(new("external-result", "external-result", "custom.external", "source"),
                new(input.RootDirectory, input.Files, "processedFiles", true, [source.Id], input.MetadataJson), "external-consumer");
        var before = await ArtifactManifestFiles.CaptureAsync(input.RootDirectory, input.Files, CancellationToken.None);
        using var owner = mode == "unscoped" ? null : StorageWriteScope.Enter(new("workflow", (await BorrowingOwner()).ToString()));
        var config = new FileArtifactProcessingConfig { BorrowSelectedProcessedArchives = mode != "non-review",
            StoragePolicy = mode switch {
                "copy" => new() { Materialization = "copy", SourceCleanup = "delete" },
                "keep" => new() { CleanupIntermediates = false }, _ => null }};
        const string supplement = """{"requirement":"required","steps":[{"id":"review","op":"extractArchive","selector":"*.zip.pdf"}]}""";
        var extractor = new ReadOnlyZipExtractor();
        var reviewer = new FileArtifactProcessor(_store, new(extractor), new(extractor), () => _appData,
            _services.GetRequiredService<IServiceScopeFactory>());
        Task<FileArtifactProcessingResult> Run() => reviewer.ExecuteAsync(FileArtifactProcessor.Intent("review", supplement, null, config),
            input, supplement, null, config, "review-consumer", null, null, null, default);
        if (mode == "wait-retry")
        {
            extractor.Wait = true;
            Assert.IsNull((await Run()).Artifact);
            Assert.IsTrue(await _store.HasActiveConsumersAsync(input.OperationId));
            extractor.Wait = false;
        }
        if (mode == "tampered")
            extractor.OnExtract = () =>
            {
                var file = input.Files.Single(f => f.EndsWith("media.zip.pdf", StringComparison.Ordinal));
                var mtime = File.GetLastWriteTimeUtc(file);
                var contents = File.ReadAllBytes(file);
                contents[0] ^= 1;
                File.WriteAllBytes(file, contents);
                File.SetLastWriteTimeUtc(file, mtime);
            };
        var result = await Run();
        if (mode == "tampered")
        {
            Assert.IsNull(result.Artifact);
            Assert.AreEqual("NeedsReconciliation", result.WaitReason);
            return;
        }
        Assert.IsNotNull(result.Artifact, result.Message);
        var checkpoint = Directory.GetParent(result.Artifact!.RootDirectory)!.FullName;
        var state = JsonSerializer.Deserialize<FileProcessingPlanExecutor.State>(await File.ReadAllTextAsync(Path.Combine(checkpoint, "state.json")), Json)!;
        Assert.AreEqual(expected ? input.Id : null, state.ReadOnlyVolumeArtifactId, "Borrow only registered immutable input.");
        Assert.AreEqual(!expected, File.Exists(Path.Combine(checkpoint, "inputs", "media.zip.pdf")));
        Assert.IsTrue(File.Exists(Path.Combine(checkpoint, "inputs", "renamed.pdf")));
        if (expected)
        {
            CollectionAssert.AreEqual(new[]{"media.zip.pdf"}, state.ReadOnlyVolumeMembers!.ToArray());
            Assert.IsTrue(await _store.HasActiveConsumersAsync(input.OperationId), "The producer must stay pinned to the workflow.");
        }
        int calls = extractor.Extractions;
        Assert.AreEqual(result.Artifact.Id, (await Run()).Artifact?.Id);
        Assert.AreEqual(calls, extractor.Extractions);
        var after = await ArtifactManifestFiles.CaptureAsync(input.RootDirectory, input.Files, CancellationToken.None);
        Assert.AreEqual(before.Fingerprint, after.Fingerprint);
    }
    private sealed class ReadOnlyZipExtractor : IArchiveExtractionService
    {
        public int Extractions { get; private set; }
        public bool Wait { get; set; }
        public Action? OnExtract { get; set; }
        public Task<ArchivePasswordProbe> ProbePasswordAsync(string entryFile, IReadOnlyList<string?> candidates,
            Action<string>? log, CancellationToken ct) => Task.FromResult(new ArchivePasswordProbe(!Wait, null, [], "", Wait ? "Password required." : ""));
        public Task<ArchivePasswordProbe> ProbePasswordAsync(string entryFile, IReadOnlyList<string?> candidates,
            Action<string>? log, CancellationToken ct, ArchiveOpenOptions? opening) => ProbePasswordAsync(entryFile, candidates, log, ct);
        public async Task<ArchiveExtractionResult> ExtractAsync(ArchiveExtractionRequest request, Action<int>? progress, CancellationToken ct)
        {
            Extractions++;
            OnExtract?.Invoke();
            if (new FileInfo(request.Files.Single()).LinkTarget != null)
                Assert.AreEqual(1, Directory.EnumerateFileSystemEntries(Path.GetDirectoryName(request.Files.Single())!).Count(),
                    "Only the explicitly registered, verified ZIP is visible to the tool.");
            Directory.CreateDirectory(request.Directory);
            await File.WriteAllTextAsync(Path.Combine(request.Directory, "movie.mp4"), "verified media", ct);
            return new(true, request.Directory, "");
        }
    }
}
