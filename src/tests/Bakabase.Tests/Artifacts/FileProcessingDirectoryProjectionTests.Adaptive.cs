using System.Text.Json;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Service.Components.FileProcessing;
using Microsoft.Extensions.AI;

namespace Bakabase.Tests.Artifacts;

public sealed partial class FileProcessingDirectoryProjectionTests
{
    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task AdaptiveDirectoryPromotionKeepsAllFilesGroupedAndRejectsCollisionsBeforeCopying(bool collision)
    {
        const string prefix = "wrapper/wrapper/wrapper";
        var originalPaths = new List<string>();
        foreach (var variant in new[] {"WAV/with-effects", "WAV/without-effects", "MP3/with-effects", "MP3/without-effects", "subtitles"})
        for (var n = 1; n <= 5; n++) originalPaths.Add(await FileAt(prefix + "/" + variant + "/" + n + ".media", variant + n));
        originalPaths.Add(await FileAt(prefix + "/cover.jpg", "cover"));
        originalPaths.Add(await FileAt(prefix + "/manual.pdf", "manual"));
        var unknown = await FileAt("activation.key", "preserve unknown sibling"); originalPaths.Add(unknown);
        if (collision) originalPaths.Add(await FileAt("cover.jpg", "different existing cover"));
        var empty = DirectoryAt(prefix + "/Save");
        var manifest = await ArtifactManifestFiles.CaptureAsync(input, originalPaths, directories: [empty]);
        var artifact = new ArtifactRecord("input", "original", "currentFiles", input, originalPaths, manifest,
            manifest.Fingerprint, ArtifactLocationState.Available, true, [], null);
        var view = AdaptiveFileProcessingAssessmentService.DescribeManifest(manifest);
        using var inventory = JsonDocument.Parse(view.Json);
        var ids = new Dictionary<int, string>();
        foreach (var row in inventory.RootElement.GetProperty("directories").EnumerateArray())
        {
            var parent = row.GetProperty("parentId").GetInt32();
            ids[row.GetProperty("id").GetInt32()] = (parent == 0 ? "" : ids[parent] + "/") + row.GetProperty("name").GetString();
        }
        var reply = JsonSerializer.Serialize(new {status = "continue", operations = new[] {
            new {type = "rebaseDirectory", directoryId = ids.Single(pair => pair.Value == prefix).Key,
                inputId = (int?)null, destination = "."}}});
        var decision = AdaptiveFileProcessingAssessmentService.ParseAndValidate(new ChatResponse(new ChatMessage(ChatRole.Assistant, reply)),
            artifact, view.Paths, view.Truncated);
        Assert.IsTrue(decision.ShouldContinue, decision.ValidationError);
        var plan = AdaptiveFileProcessingAssessmentService.BuildPlan(decision)!;
        var tracker = new TrackingFiles();
        var result = await Execute(plan, originalPaths.ToArray(), tracker, [empty]);
        Assert.IsTrue(originalPaths.All(File.Exists));
        Assert.AreEqual("preserve unknown sibling", await File.ReadAllTextAsync(unknown));
        if (collision)
        {
            Assert.IsFalse(result.Completed); Assert.AreEqual(FileProcessingPathProjection.FailureCode, result.FailureCode);
            Assert.AreEqual(0, tracker.Copies); Assert.AreEqual(0, tracker.ProjectionMoves);
            Assert.AreEqual("different existing cover", await File.ReadAllTextAsync(Path.Combine(input, "cover.jpg")));
        }
        else
        {
            Assert.IsTrue(result.Completed, result.Message); Assert.AreEqual(28, result.Files.Count);
            CollectionAssert.AreEquivalent(originalPaths.Select(path => Path.GetRelativePath(input, path).Replace('\\', '/'))
                .Select(path => path.StartsWith(prefix + "/") ? path[(prefix.Length + 1)..] : path).ToArray(), Paths(result));
            Assert.IsTrue(Directory.Exists(Path.Combine(result.OutputDirectory!, "Save")));
            Assert.IsTrue(result.FileLineage!.Entries.All(entry => !entry.Replaced));
        }
    }
}
