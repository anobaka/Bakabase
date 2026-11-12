using System.Text.Json;
using Bakabase.InsideWorld.Business.Components.Compression;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Service.Components.FileProcessing;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using SharpCompress.Archives.SevenZip;

namespace Bakabase.Tests.Artifacts;

public sealed partial class FileArtifactProcessorTests
{
    [TestMethod]
    public async Task Adaptive_extraction_replaces_both_volumes_and_preserves_unselected_files_on_replay()
    {
        var directory = Directory.CreateDirectory(Path.Combine(_root, "package")).FullName;
        // A 178-byte 7z archive containing only payload.txt. Split the binary stream as 7z -v does.
        var archive = Convert.FromBase64String(
            "N3q8ryccAAM9b2usJAAAAAAAAABuAAAAAAAAAAuI7+EAOBhLmXUPRggyIuLpak6HPOh07TMDH8ufsEraJMrR//70GAABBAYAAQkkAAcLAQABIwMBAQVdAACAAAwaAAgKAUyYrqsAAAUBERkAcABhAHkAbABvAGEAZAAuAHQAeAB0AAAAFAoBAACAPtXesZ0BEgoBAGdx46z6cN0BEwoBAACAPtXesZ0BFQYBACCAgIEAAA==");
        var first = Path.Combine(directory, "game.7z.001");
        var second = Path.Combine(directory, "game.7z.002");
        await File.WriteAllBytesAsync(first, archive[..100]);
        await File.WriteAllBytesAsync(second, archive[100..]);
        var patch = Path.Combine(Directory.CreateDirectory(Path.Combine(directory, "Patch", "Data")).FullName, "Actors.rvdata2");
        await File.WriteAllTextAsync(patch, "patch bytes");
        var key = Path.Combine(directory, "license.key");
        await File.WriteAllTextAsync(key, "key bytes");
        var unrelated = Path.Combine(directory, "keep.7z");
        await File.WriteAllBytesAsync(unrelated, archive);

        var input = await _store.AdoptAsync(new("package", "package-v1", "download", "source-v1"),
            new(directory, [first, second, patch, key, unrelated], Managed: true), "download-owner");
        var manifest = AdaptiveFileProcessingAssessmentService.DescribeManifest(input.Manifest);
        var id = Array.IndexOf(manifest.Paths, "game.7z.001") + 1;
        Assert.IsTrue(id > 0);
        var response = new ChatResponse(new ChatMessage(ChatRole.Assistant, JsonSerializer.Serialize(new
        {
            status = "continue", reason = "Extract the selected split archive", operations = new[] { new { type = "extract", inputId = id } }
        })));
        var decision = AdaptiveFileProcessingAssessmentService.ParseAndValidate(response, input, manifest.Paths, manifest.Truncated);
        var plan = AdaptiveFileProcessingAssessmentService.BuildPlan(decision);
        Assert.IsNotNull(plan, decision.Reason);
        var planJson = JsonSerializer.Serialize(plan, Json);
        var extractor = new SplitFixtureExtractor();
        var processor = new FileArtifactProcessor(_store, new FileProcessingPlanExecutor(extractor),
            new RecursiveArchiveExecutor(extractor), () => _appData, _services.GetRequiredService<IServiceScopeFactory>());
        Task<FileArtifactProcessingResult> Execute(string consumer) =>
            processor.ExecuteAsync(Intent(planJson), input, planJson, null, Config, consumer, null, null, null, default);

        var result = await Execute("first-run");
        Assert.IsNotNull(result.Artifact, result.WaitReason);
        var files = result.Artifact.Files;
        CollectionAssert.AreEquivalent(new[] { "Actors.rvdata2", "license.key", "keep.7z", "payload.txt" },
            files.Select(Path.GetFileName).ToArray());
        Assert.AreEqual("payload from both volumes\n", await File.ReadAllTextAsync(files.Single(f => f.EndsWith("payload.txt"))));
        Assert.AreEqual("patch bytes", await File.ReadAllTextAsync(files.Single(f => f.EndsWith("Actors.rvdata2"))));
        Assert.AreEqual("key bytes", await File.ReadAllTextAsync(files.Single(f => f.EndsWith("license.key"))));
        CollectionAssert.AreEqual(archive, await File.ReadAllBytesAsync(files.Single(f => f.EndsWith("keep.7z"))));
        CollectionAssert.AreEqual(archive, (await File.ReadAllBytesAsync(first)).Concat(await File.ReadAllBytesAsync(second)).ToArray());
        Assert.AreEqual(result.Artifact.Id, (await Execute("replay")).Artifact?.Id);
        Assert.AreEqual(1, extractor.Extractions);
    }

    // Decode exactly the group's registered volumes with SharpCompress, so this regression also
    // proves a readable payload without requiring a platform-specific 7z binary in CI.
    private sealed class SplitFixtureExtractor : IArchiveExtractionService
    {
        public int Extractions { get; private set; }

        public Task<ArchivePasswordProbe> ProbePasswordAsync(string entryFile, IReadOnlyList<string?> candidates,
            Action<string>? onOutput, CancellationToken ct) => Task.FromResult(new ArchivePasswordProbe(true, null, [], "", ""));

        public async Task<ArchiveExtractionResult> ExtractAsync(ArchiveExtractionRequest request,
            Action<int>? onProgress, CancellationToken ct)
        {
            Extractions++;
            CollectionAssert.AreEqual(new[] { "game.7z.001", "game.7z.002" }, request.Files.Select(Path.GetFileName).ToArray());
            using var stream = new MemoryStream();
            foreach (var path in request.Files)
            {
                await using var part = File.OpenRead(path);
                await part.CopyToAsync(stream, ct);
            }
            stream.Position = 0;
            using var archive = SevenZipArchive.Open(stream);
            using var reader = archive.ExtractAllEntries();
            Assert.IsTrue(reader.MoveToNextEntry());
            Assert.AreEqual("payload.txt", reader.Entry.Key);
            using var content = reader.OpenEntryStream();
            await using var output = File.Create(Path.Combine(request.Directory, "payload.txt"));
            await content.CopyToAsync(output, ct);
            return new(true, request.Directory, "");
        }
    }
}
