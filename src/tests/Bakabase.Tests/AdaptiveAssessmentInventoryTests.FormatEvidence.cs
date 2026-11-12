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
        internal ArtifactRecord Input(string relative, byte[] bytes)
        {
            var file = Path.Combine(Root, relative);
            File.WriteAllBytes(file, bytes);
            ArtifactManifestEntry[] entries = [new(relative, bytes.Length, new string('a', 64))];
            var fingerprint = ArtifactManifestFiles.Fingerprint(entries);
            return new("format-fixture", "operation", "files", Root, [file], new(entries, fingerprint),
                fingerprint, ArtifactLocationState.Available, true, [], null);
        }
        public void Dispose() => Directory.Delete(Root, true);
    }
}
