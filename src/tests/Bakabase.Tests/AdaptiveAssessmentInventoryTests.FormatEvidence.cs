using System.Text.Json;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Service.Components.FileProcessing;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bakabase.Tests;

public sealed partial class AdaptiveAssessmentInventoryTests
{
    [DataTestMethod]
    [DataRow("application.apk", "377ABCAF271C0004", ".7z")]
    [DataRow("photo.jpg", "526172211A070100", ".rar")]
    public async Task Byte_evidence_reaches_model_with_exact_input_id_independent_of_extension(
        string name, string header, string expectedFormat)
    {
        using var fixture = new FormatEvidenceFixture();
        var input = fixture.Input(name, Convert.FromHexString(header));
        var model = new EvidenceModel();
        var service = new AdaptiveFileProcessingAssessmentService(model,
            NullLogger<AdaptiveFileProcessingAssessmentService>.Instance);
        var decision = await service.AssessAsync(input, "Unpack the outer package.", 0);
        StringAssert.Contains(model.UserMessage, "\"inputId\":1,\"status\":\"recognizedArchiveSignature\",\"archiveFormat\":\"" + expectedFormat + "\"");
        Assert.IsTrue(decision.ShouldContinue, decision.ValidationError);
        Assert.AreEqual(name, decision.Operations.Single().Input);
        CollectionAssert.AreEqual(Convert.FromHexString(header), File.ReadAllBytes(input.Files.Single()));
        CollectionAssert.AreEqual(new[] {input.Files.Single()}, Directory.GetFiles(fixture.Root));
    }

    [TestMethod]
    public async Task Unrecognized_media_prefix_is_not_evidence_that_a_file_is_not_an_archive()
    {
        using var fixture = new FormatEvidenceFixture();
        var input = fixture.Input("wrapper.mp4", Convert.FromHexString("00000018667479706D70343200504B0304"));
        using var result = JsonDocument.Parse(await AdaptiveFileProcessingAssessmentService.DescribeFormatEvidenceAsync(input, ["wrapper.mp4"], default));
        var evidence = result.RootElement.GetProperty("files")[0];
        Assert.AreEqual("notRecognizedAtOffsetZero", evidence.GetProperty("status").GetString());
        Assert.AreEqual(JsonValueKind.Null, evidence.GetProperty("archiveFormat").ValueKind);
        StringAssert.Contains(AdaptiveFileProcessingAssessmentService.SystemPrompt, "does not rule out a prefixed");
        StringAssert.Contains(AdaptiveFileProcessingAssessmentService.SystemPrompt, "Never authorize deletion from format evidence");
    }

    [TestMethod]
    public void Large_media_collections_are_not_opened_and_archive_entry_candidates_have_a_fixed_bound()
    {
        var media = Enumerable.Range(0, 2000).Select(index => $"images/{index}.jpg").ToArray();
        var onlyMedia = Input(media);
        Assert.AreEqual(0, AdaptiveFileProcessingAssessmentService.FormatEvidenceCandidates(onlyMedia.Manifest, media.Take(200).ToArray()).Length);
        var files = media.Take(100).Concat(Enumerable.Range(0, 30).Select(index => $"archives/{index}.7z"))
            .Concat(["split.7z.001", "split.7z.002"]).ToArray();
        var manifest = Input(files).Manifest;
        var selected = AdaptiveFileProcessingAssessmentService.FormatEvidenceCandidates(manifest, files);
        Assert.AreEqual(AdaptiveFileProcessingAssessmentService.MaxFormatEvidenceFiles, selected.Length);
        Assert.IsTrue(selected.All(index => files[index].StartsWith("archives/", StringComparison.Ordinal)));
        var split = media.Take(100).Concat(["split.7z.001", "split.7z.002"]).ToArray();
        CollectionAssert.AreEqual(new[] {100}, AdaptiveFileProcessingAssessmentService.FormatEvidenceCandidates(Input(split).Manifest, split));
    }

    [TestMethod]
    public async Task Expanded_application_does_not_hide_extensionless_outer_archives_from_format_evidence()
    {
        using var fixture = new FormatEvidenceFixture();
        var zip = Convert.FromHexString("504B030414000000");
        var files = Enumerable.Range(0, 130)
            .Select(index => ($"wrapper/game/manual/{index}.png", new byte[] {1, 2, 3}, 3L))
            .Concat([
                ("wrapper/game/game.exe", new byte[] {77, 90}, 2L),
                ("wrapper/game/assets.med", new byte[] {1}, 4 * 1024 * 1024L),
                ("wrapper/game/save/original.zip", zip, (long) zip.Length),
                ("wrapper/remaining/packagezip", zip, 2 * 1024 * 1024L),
                ("wrapper/companionzip~abcdef", zip, 1024 * 1024L),
                ("wrapper/readme", new byte[] {1}, 1L)
            ]).ToArray();
        var input = fixture.Input(files);
        var inventory = AdaptiveFileProcessingAssessmentService.DescribeManifest(input.Manifest);
        using var result = JsonDocument.Parse(await AdaptiveFileProcessingAssessmentService.DescribeFormatEvidenceAsync(
            input, inventory.Paths, default));
        var evidence = result.RootElement.GetProperty("files").EnumerateArray().ToArray();
        var inspected = evidence.Select(item => inventory.Paths[item.GetProperty("inputId").GetInt32() - 1]).ToArray();
        CollectionAssert.AreEquivalent(new[] {"wrapper/game/save/original.zip", "wrapper/remaining/packagezip",
            "wrapper/companionzip~abcdef"}, inspected);
        Assert.IsTrue(evidence.All(item => item.GetProperty("archiveFormat").GetString() == ".zip"));
        Assert.AreEqual(files.Length - 3, result.RootElement.GetProperty("uninspectedFiles").GetInt32());
        Assert.AreEqual(files.Length, Directory.GetFiles(fixture.Root, "*", SearchOption.AllDirectories).Length);
    }

    [TestMethod]
    public void Archive_candidates_cannot_starve_the_bounded_extensionless_outer_sample()
    {
        var files = Enumerable.Range(0, 40).Select(index => $"wrapper/archives/{index}.zip")
            .Concat(Enumerable.Range(0, 20).Select(index => $"wrapper/package{index}")).ToArray();
        var manifest = new ArtifactManifest(files.Select((path, index) =>
            new ArtifactManifestEntry(path, (index + 1) * 1024 * 1024L, new string('a', 64))).ToArray(), "fingerprint");
        var selected = AdaptiveFileProcessingAssessmentService.FormatEvidenceCandidates(manifest, files);
        Assert.AreEqual(AdaptiveFileProcessingAssessmentService.MaxFormatEvidenceFiles, selected.Length);
        Assert.AreEqual(AdaptiveFileProcessingAssessmentService.MaxOuterFormatEvidenceFiles,
            selected.Count(index => files[index].StartsWith("wrapper/package", StringComparison.Ordinal)));
        CollectionAssert.Contains(selected, files.Length - 1);
        Assert.AreEqual(selected.Length, selected.Distinct().Count());
        CollectionAssert.AreEqual(selected, AdaptiveFileProcessingAssessmentService.FormatEvidenceCandidates(manifest, files));
    }

    [TestMethod]
    public void Application_payloads_dense_branches_and_small_extensionless_files_are_not_sampled()
    {
        var files = new[] {"wrapper/app/main.exe", "wrapper/app/engine", "wrapper/assets.med", "wrapper/payload.dat",
                "wrapper/movie.mp4", "wrapper/readme"}
            .Concat(Enumerable.Range(0, 50).Select(index => $"wrapper/assets/blob{index}")).ToArray();
        var manifest = new ArtifactManifest(files.Select(path => new ArtifactManifestEntry(path,
            path.EndsWith("readme", StringComparison.Ordinal) ? 100 : 32 * 1024 * 1024L,
            new string('a', 64))).ToArray(), "fingerprint");
        Assert.AreEqual(0, AdaptiveFileProcessingAssessmentService.FormatEvidenceCandidates(manifest, files).Length);
    }

    [TestMethod]
    public void Extensionless_candidates_stay_bound_to_visible_inventory_ids()
    {
        var files = Enumerable.Range(0, 30).Select(index => $"wrapper/game/{index}.jpg")
            .Concat(["wrapper/first/package", "wrapper/second/package"]).ToArray();
        var manifest = new ArtifactManifest(files.Select(path =>
            new ArtifactManifestEntry(path, 2 * 1024 * 1024L, new string('a', 64))).ToArray(), "fingerprint");
        var visible = new[] {files[^1], files[0], files[1]};
        CollectionAssert.AreEqual(new[] {0}, AdaptiveFileProcessingAssessmentService.FormatEvidenceCandidates(manifest, visible));
    }

    [TestMethod]
    public async Task Missing_changed_and_linked_files_produce_no_trusted_format_and_cancellation_propagates()
    {
        using var fixture = new FormatEvidenceFixture();
        var input = fixture.Input("selected.bin", Convert.FromHexString("377ABCAF271C0004"));
        File.AppendAllText(input.Files.Single(), "changed");
        await AssertUnavailable(input);
        File.Delete(input.Files.Single());
        await AssertUnavailable(input);
        if (!OperatingSystem.IsWindows())
        {
            var outside = Path.Combine(fixture.Root, "unselected.bin");
            File.WriteAllBytes(outside, Convert.FromHexString("377ABCAF271C0004"));
            File.CreateSymbolicLink(input.Files.Single(), outside);
            await AssertUnavailable(input);
        }
        using var stop = new CancellationTokenSource(); stop.Cancel();
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() =>
            AdaptiveFileProcessingAssessmentService.DescribeFormatEvidenceAsync(input, ["selected.bin"], stop.Token));

        static async Task AssertUnavailable(ArtifactRecord artifact)
        {
            using var result = JsonDocument.Parse(await AdaptiveFileProcessingAssessmentService.DescribeFormatEvidenceAsync(artifact, ["selected.bin"], default));
            var evidence = result.RootElement.GetProperty("files")[0];
            Assert.AreEqual("unavailable", evidence.GetProperty("status").GetString());
            Assert.AreEqual(JsonValueKind.Null, evidence.GetProperty("archiveFormat").ValueKind);
        }
    }

    private sealed class FormatEvidenceFixture : IDisposable
    {
        internal string Root { get; } = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "adaptive-format-" + Guid.NewGuid().ToString("N"))).FullName;
        internal ArtifactRecord Input(string relative, byte[] bytes) => Input((relative, bytes, bytes.LongLength));
        internal ArtifactRecord Input(params (string Relative, byte[] Header, long Length)[] files)
        {
            foreach (var (relative, header, length) in files)
            {
                var file = Path.Combine(Root, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                using var stream = File.Create(file);
                stream.Write(header);
                stream.SetLength(length);
            }
            var entries = files.Select(file => new ArtifactManifestEntry(file.Relative, file.Length, new string('a', 64))).ToArray();
            var fingerprint = ArtifactManifestFiles.Fingerprint(entries);
            return new("format-fixture", "operation", "files", Root, files.Select(file => Path.Combine(Root, file.Relative)).ToArray(), new(entries, fingerprint),
                fingerprint, ArtifactLocationState.Available, true, [], null);
        }
        public void Dispose() => Directory.Delete(Root, true);
    }
}
