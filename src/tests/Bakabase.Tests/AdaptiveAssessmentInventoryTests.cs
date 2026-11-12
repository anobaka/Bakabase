using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Service.Components.FileProcessing;
using Microsoft.Extensions.AI;

namespace Bakabase.Tests;

[TestClass]
public sealed partial class AdaptiveAssessmentInventoryTests
{
    private static ArtifactRecord Input(params string[] files) => new("id", "op", "processedFiles", "/local/root",
        files, new(files.Select(f => new ArtifactManifestEntry(f, 12, new string('c', 64))).ToArray(), "fingerprint"),
        "fingerprint", ArtifactLocationState.Available, true, [], null);

    private static AdaptiveFileProcessingDecision Decide(ArtifactRecord input, string json)
    {
        var view = AdaptiveFileProcessingAssessmentService.DescribeManifest(input.Manifest);
        return AdaptiveFileProcessingAssessmentService.ParseAndValidate(
            new ChatResponse(new ChatMessage(ChatRole.Assistant, json)) {FinishReason = ChatFinishReason.Stop},
            input, view.Paths, view.Truncated);
    }

    [TestMethod]
    public void NestedMultilingualInventoryUsesDistinctDirectoriesAndShortUnambiguousIds()
    {
        var root = "转存目录/下载目录/游戏/升级补丁/游戏数据/";
        var files = Enumerable.Range(0, 90).Select(i => root + i % 3 + "/数据" + i + ".rvdata")
            .Concat([root + "本体.7z.001", root + "本体.7z.002"]).ToArray();
        var input = Input(files);
        var view = AdaptiveFileProcessingAssessmentService.DescribeManifest(input.Manifest);
        Assert.IsFalse(view.Truncated);
        Assert.IsTrue(view.Json.Contains("本体.7z.001"));
        Assert.IsFalse(view.Json.Contains("/local/root") || view.Json.Contains(new string('c', 64)));
        using var json = JsonDocument.Parse(view.Json);
        var folders = json.RootElement.GetProperty("folders").EnumerateArray().ToArray();
        Assert.AreEqual(4, folders.Length);
        foreach (var group in folders)
        foreach (var file in group.GetProperty("files").EnumerateArray())
            Assert.AreEqual(string.Join("/", new[] {json.RootElement.GetProperty("prefix").GetString(), group.GetProperty("folder").GetString(), file.GetProperty("name").GetString()}.Where(p => p != ".")),
                view.Paths[file.GetProperty("id").GetInt32() - 1]);
        var extract = Decide(input, """{"status":"continue","operations":[{"type":"extract","inputId":91,"destination":null}]}""");
        var plan = AdaptiveFileProcessingAssessmentService.BuildPlan(extract);
        Assert.AreEqual(1, plan!.Steps.Count);
        Assert.AreEqual(root + "本体.7z.00*", plan.Steps[0].Selector);
        Assert.IsTrue(Decide(input, """{"status":"final"}""").NeedsHumanReview);
    }

    [TestMethod]
    public void Shared_prefix_is_factored_once_and_ids_still_address_exact_original_paths()
    {
        var common = string.Join('/', Enumerable.Repeat("long-author-directory", 7));
        var input = Input(Enumerable.Range(1, 80).Select(i => $"{common}/group{i}/archive{i}.zip").ToArray());
        var view = AdaptiveFileProcessingAssessmentService.DescribeManifest(input.Manifest);
        Assert.IsFalse(view.Truncated);
        using var json = JsonDocument.Parse(view.Json);
        Assert.AreEqual(common, json.RootElement.GetProperty("prefix").GetString());
        Assert.AreEqual(1, view.Json.Split(common).Length - 1);
        var decision = Decide(input, """{"status":"continue","operations":[{"type":"extract","inputId":80}]}""");
        Assert.AreEqual($"{common}/group80/archive80.zip", decision.Operations.Single().Input);
        var mixed = AdaptiveFileProcessingAssessmentService.DescribeManifest(Input("root.txt", "sub/item.zip").Manifest);
        using var mixedJson = JsonDocument.Parse(mixed.Json);
        Assert.AreEqual(".", mixedJson.RootElement.GetProperty("prefix").GetString());
    }

    [TestMethod]
    public void Invalid_proposal_identifies_the_rejected_field_without_losing_response_values()
    {
        var input = Input("file.zip");
        var decision = Decide(input, """{"status":"continue","operations":[{"type":"extract","inputId":999}]}""");
        Assert.AreEqual("LLM_DECISION_INVALID", decision.DiagnosticCode);
        StringAssert.Contains(decision.ValidationError!, "operations[0].inputId");
        StringAssert.Contains(decision.RawText!, "999");
        var count = Decide(input, JsonSerializer.Serialize(new {status = "continue", operations =
            Enumerable.Range(0, 25).Select(_ => new {type = "move", inputId = 1, destination = "files"})}));
        StringAssert.Contains(count.ValidationError!, "25 exceeds maximum 24");
    }

    [TestMethod]
    public void IncompleteOrOverbroadMultipartSelectionCannotBecomeAnExecutablePlan()
    {
        var extract = """{"status":"continue","operations":[{"type":"extract","inputId":1,"destination":null}]}""";
        foreach (var input in new[] {
            Input("p/game.7z.001"),
            Input("p/game.7z.001", "p/game.7z.003"),
            Input("p/game.7z.001", "p/game.7z.002", "p/game.7z.001.backup")
        })
            Assert.IsTrue(Decide(input, extract).NeedsHumanReview);

        Assert.IsTrue(Decide(Input("game/Data/blob.bin.001", "game/Data/blob.bin.002"), """{"status":"final"}""").IsFinal);
    }

    [TestMethod]
    public void RejectsAnEntireProposalWhenAReferenceIsOutOfRangeOrAmbiguous()
    {
        var input = Input("a.7z", "2", "nested/a.7z");
        Assert.IsTrue(Decide(input, """{"status":"continue","operations":[{"type":"extract","inputId":2},{"type":"extract","inputId":4}]}""").NeedsHumanReview);
        Assert.IsTrue(Decide(input, """{"status":"continue","operations":[{"type":"extract","inputId":1}]}""").NeedsHumanReview);
        Assert.IsTrue(Decide(input, """{"status":"continue","operations":[{"type":"extract","inputId":2,"input":"nested/a.7z"}]}""").NeedsHumanReview);
        Assert.AreEqual("nested/a.7z", Decide(input, """{"status":"continue","operations":[{"type":"extract","input":"nested/a.7z"}]}""").Operations.Single().Input);
        Assert.AreEqual("2", Decide(input, """{"status":"continue","operations":[{"type":"extract","inputId":2}]}""").Operations.Single().Input);
    }

    [TestMethod]
    public void RepeatedOrOverlappingInputsCannotBeCompiledAsDependentOperationsOnAnOldInventory()
    {
        foreach (var pair in new[] {
            new[] {new AdaptiveFileProcessingOperation("rename", "game.001", "game.7z"), new("extract", "game.001")},
            new[] {new AdaptiveFileProcessingOperation("extract", "game.7z.00*"), new("move", "game.7z.001", "files")},
            new[] {new AdaptiveFileProcessingOperation("extract", "game.zip"), new("extract", "game.zip")}
        })
        {
            var decision = new AdaptiveFileProcessingDecision("continue", null, null, pair, [], []);
            Assert.ThrowsException<InvalidOperationException>(() => AdaptiveFileProcessingAssessmentService.BuildPlan(decision));
        }
    }

    [TestMethod]
    public void InventoryBoundsLongPathsAndRefusesToFinalizeAnUnseenTail()
    {
        var files = Enumerable.Range(0, 210).Select(i => i + "/" + new string('文', 220)).ToArray();
        var input = Input(files);
        var view = AdaptiveFileProcessingAssessmentService.DescribeManifest(input.Manifest);
        Assert.IsTrue(view.Truncated);
        Assert.IsTrue(Encoding.UTF8.GetByteCount(view.Json) <= 16 * 1024);
        Assert.IsTrue(view.Paths.Length < 200);
        Assert.IsTrue(Decide(input, """{"status":"final"}""").NeedsHumanReview);
    }
}
