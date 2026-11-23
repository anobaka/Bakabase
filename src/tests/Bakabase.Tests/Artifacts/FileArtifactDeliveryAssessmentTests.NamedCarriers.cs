using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Service.Components.FileProcessing;

namespace Bakabase.Tests.Artifacts;

public sealed partial class FileArtifactDeliveryAssessmentTests
{
    private async Task<ArtifactRecord> NamedCarriers(params (string Name, bool Zip)[] files)
    {
        var directory = Path.Combine(root, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        foreach (var (name, zip) in files)
        {
            var path = Path.Combine(directory, name);
            if (zip)
            {
                using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
                using var member = archive.CreateEntry("ordinary-member.txt").Open();
                member.Write("real archived payload"u8);
            }
            else await File.WriteAllTextAsync(path, "%PDF-1.4\n%%EOF");
        }
        return await store.AdoptAsync(new(Guid.NewGuid().ToString("N"), Guid.NewGuid().ToString("N"), "files.process", "fixture"),
            new(directory, Directory.GetFiles(directory), "processedFiles", true,
                MetadataJson: Json(new FileArtifactProcessingMetadata(null, Json(Extract), null))), "fixture");
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Large_delivery_review_names_real_disguised_archives_before_unknown_companions(bool unknownCompanion)
    {
        var files = Enumerable.Range(0, 16).Select(i => ($"note-{i}.pdf", false)).ToList();
        files.AddRange([("a.zip.pdf", true), ("b.ZIP.PDF", true), ("c.zip.pdf", false)]);
        if (unknownCompanion) files.Add(("incomplete", false));
        var artifact = await NamedCarriers(files.ToArray());
        var original = artifact.Manifest.Fingerprint;
        var item = Workset(artifact);
        var outcome = await activity.ProcessItemAsync(Context(item), item, default);
        Assert.IsNotNull(outcome.Suspension);
        using var json = JsonDocument.Parse(outcome.Suspension.PromptJson!);
        var prompt = json.RootElement;
        Assert.AreEqual(FileArtifactDeliveryAssessment.ResultUncertain, prompt.GetProperty("reason").GetString());
        CollectionAssert.AreEqual(unknownCompanion ? new[] {"a.zip.pdf", "b.ZIP.PDF", "incomplete"} : new[] {"a.zip.pdf", "b.ZIP.PDF"},
            prompt.GetProperty("files").Deserialize<string[]>()!);
        var types = prompt.GetProperty("archiveSignatures").Deserialize<Dictionary<string, string>>()!;
        CollectionAssert.AreEquivalent(new[] {"a.zip.pdf", "b.ZIP.PDF"}, types.Keys.ToArray());
        Assert.IsTrue(types.Values.All(x => x == ".zip"));
        Assert.IsFalse(prompt.GetProperty("automaticOuterPackaging").GetBoolean());
        Assert.IsTrue((await ArtifactManifestFiles.ValidateAsync(artifact.RootDirectory, artifact.Manifest, default)).Available);
        Assert.AreEqual(original, artifact.Manifest.Fingerprint);
    }

    [TestMethod]
    [DataRow("collection.rar.epub")]
    [DataRow("document.zip.docx")]
    [DataRow("application.zip.jar")]
    [DataRow("software.exe")]
    public async Task Normal_document_and_application_formats_keep_their_delivery_semantics(string package)
    {
        var files = Enumerable.Range(0, 10).Select(i => ($"doc{i}.pdf", false)).ToList();
        files.Add(("actual.zip.pdf", false));
        files.Add((package, true));
        if (package.EndsWith(".exe")) files.Add(("bundled.zip.pdf", true));
        var artifact = await NamedCarriers(files.ToArray());
        var result = await FileArtifactDeliveryAssessment.AssessAsync(artifact, default, true);
        Assert.IsFalse(result.Uncertain);
        Assert.IsEmpty(result.Files);
    }

    [TestMethod]
    public async Task Explicitly_retained_incomplete_original_always_reaches_delivery_review()
    {
        var artifact = await NamedCarriers(Enumerable.Range(1, 12).Select(i => ($"doc{i}.pdf", false)).ToArray());
        var saved = JsonSerializer.Deserialize<FileArtifactProcessingMetadata>(artifact.MetadataJson!, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        artifact = artifact with {MetadataJson = Json(saved with {RetainedArchiveFiles = ["doc3.pdf"]})};
        var result = await FileArtifactDeliveryAssessment.AssessAsync(artifact, default);
        Assert.IsTrue(result.Uncertain);
        Assert.IsFalse(result.Automatic);
        CollectionAssert.AreEqual(new[]{"doc3.pdf"}, result.Files.ToArray());
    }

    [TestMethod]
    public async Task Uninspected_candidates_remain_uncertain_without_an_archive_label_or_unbounded_io()
    {
        var files = Enumerable.Range(0, FileArtifactDeliveryAssessment.MaximumCompanionProbeFiles + 3)
            .Select(i => ($"possible{i:000}.zip.pdf", false)).ToArray();
        var artifact = await NamedCarriers(files);
        var result = await FileArtifactDeliveryAssessment.AssessAsync(artifact, default, true);
        Assert.IsTrue(result.Uncertain);
        Assert.IsFalse(result.Automatic);
        CollectionAssert.AreEqual(files.Skip(FileArtifactDeliveryAssessment.MaximumCompanionProbeFiles)
            .Select(x => x.Item1).ToArray(), result.Files.ToArray());
        Assert.IsEmpty(await FileArtifactDeliveryAssessment.ReadReviewSignaturesAsync(artifact, result.Files, default));
    }

    [TestMethod]
    public async Task Missing_or_changed_candidates_are_unlabelled_and_unrelated_gallery_files_are_never_probed()
    {
        var files = Enumerable.Range(0, 20).Select(i => ($"image{i}.jpg", false)).ToList();
        files.Add(("lost.zip.pdf", true));
        var artifact = await NamedCarriers(files.ToArray());
        foreach(var file in artifact.Files) File.Delete(file);
        var result = await FileArtifactDeliveryAssessment.AssessAsync(artifact, default, true);
        CollectionAssert.AreEqual(new[] {"lost.zip.pdf"}, result.Files.ToArray());
        Assert.IsTrue(result.Uncertain);
        Assert.IsEmpty(await FileArtifactDeliveryAssessment.ReadReviewSignaturesAsync(artifact, result.Files, default));
    }
}
