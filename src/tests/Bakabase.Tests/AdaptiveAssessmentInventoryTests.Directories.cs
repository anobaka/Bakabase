using System.Text.Json;
using Bakabase.Service.Components.FileProcessing;
using Microsoft.Extensions.AI;

namespace Bakabase.Tests;

public sealed partial class AdaptiveAssessmentInventoryTests
{
    [TestMethod]
    public void DirectoryInventoryProvidesSeparateRealIdsAndOnePromotionCoversAll27Files()
    {
        var prefix = "outer/inner/resource";
        var input = Input(Enumerable.Range(1, 25).Select(i => prefix + (i % 2 == 0 ? "/WAV/effects/" : "/MP3/original/") + i + ".media")
            .Concat([prefix + "/cover.jpg", prefix + "/manual.pdf"]).ToArray());
        var view = AdaptiveFileProcessingAssessmentService.DescribeManifest(input.Manifest);
        Assert.IsFalse(view.Truncated);
        using var inventory = JsonDocument.Parse(view.Json);
        var ids = new Dictionary<int, string>();
        foreach (var row in inventory.RootElement.GetProperty("directories").EnumerateArray())
        {
            var parent = row.GetProperty("parentId").GetInt32();
            ids[row.GetProperty("id").GetInt32()] = (parent == 0 ? "" : ids[parent] + "/") + row.GetProperty("name").GetString();
        }
        var directoryId = ids.Single(row => row.Value == prefix).Key;
        var decision = Decide(input, JsonSerializer.Serialize(new {status = "continue", operations = new[] {
            new {type = "rebaseDirectory", directoryId, inputId = (int?)null, destination = "."}}}));
        Assert.IsTrue(decision.ShouldContinue, decision.ValidationError);
        var step = AdaptiveFileProcessingAssessmentService.BuildPlan(decision)!.Steps.Single();
        Assert.AreEqual("rebaseDirectory", step.Op); Assert.AreEqual(prefix, step.SourceDirectory);
        Assert.AreEqual(".", step.TargetDirectory); Assert.IsNull(step.Selector);
        StringAssert.Contains(AdaptiveFileProcessingAssessmentService.SystemPrompt, "preserving all inner folders");
        StringAssert.Contains(AdaptiveFileProcessingAssessmentService.SystemPrompt, "parent traversal");
    }

    [DataTestMethod]
    [DataRow("{\"type\":\"rebaseDirectory\",\"directoryId\":999,\"destination\":\".\"}", "directoryId")]
    [DataRow("{\"type\":\"rebaseDirectory\",\"directoryId\":0,\"destination\":\".\"}", "directoryId")]
    [DataRow("{\"type\":\"rebaseDirectory\",\"directoryId\":\"1\",\"destination\":\".\"}", "directoryId")]
    [DataRow("{\"type\":\"rebaseDirectory\",\"directoryId\":1,\"inputId\":1,\"destination\":\".\"}", "directoryId")]
    [DataRow("{\"type\":\"rebaseDirectory\",\"directoryId\":1,\"input\":\"outer\",\"destination\":\".\"}", "directoryId")]
    [DataRow("{\"type\":\"rebaseDirectory\",\"directoryId\":1,\"destination\":\"../..\"}", "destination")]
    [DataRow("{\"type\":\"rebaseDirectory\",\"directoryId\":1,\"destination\":\"/absolute\"}", "destination")]
    [DataRow("{\"type\":\"rebaseDirectory\",\"directoryId\":1,\"destination\":null}", "destination")]
    [DataRow("{\"type\":\"move\",\"inputId\":1,\"directoryId\":1,\"destination\":\".\"}", "directoryId")]
    public void DirectoryPromotionRejectsUnboundReferencesAndTraversal(string operation, string field)
    {
        var decision = Decide(Input("outer/inner/file.bin"), "{\"status\":\"continue\",\"operations\":[" + operation + "]}");
        Assert.IsTrue(decision.NeedsHumanReview); StringAssert.Contains(decision.ValidationError!, field);
        StringAssert.Contains(decision.RawText!, operation);
    }

    [TestMethod]
    public void DirectoryPromotionRequiresCompleteInventoryAndCannotMixOldReferences()
    {
        var input = Input("wrapper/a.bin", "wrapper/b.bin");
        var json = """{"status":"continue","operations":[{"type":"rebaseDirectory","directoryId":1,"destination":"."}]}""";
        var response = new ChatResponse(new ChatMessage(ChatRole.Assistant, json));
        foreach (var truncated in new[] {false, true})
        {
            var invalid = AdaptiveFileProcessingAssessmentService.ParseAndValidate(response, input, ["wrapper/a.bin"], truncated);
            Assert.IsTrue(invalid.NeedsHumanReview);
        }
        var mixed = Decide(input, """{"status":"continue","operations":[{"type":"rebaseDirectory","directoryId":1,"destination":"."},{"type":"move","inputId":1,"destination":"media"}]}""");
        Assert.IsTrue(mixed.NeedsHumanReview);
        Assert.ThrowsException<InvalidOperationException>(() => AdaptiveFileProcessingAssessmentService.BuildPlan(new("continue", null, null,
            [new("rebaseDirectory", "wrapper", "."), new("extract", "wrapper/a.bin")], [], [])));
    }

    [TestMethod]
    public void OldPerFileResponsesRemainValidButParentTraversalIsNeverNormalized()
    {
        var input = Input("wrapper/file.zip");
        Assert.IsTrue(Decide(input, """{"status":"continue","operations":[{"type":"extract","inputId":1,"destination":null}]}""").ShouldContinue);
        Assert.IsTrue(Decide(input, """{"status":"continue","operations":[{"type":"move","input":"wrapper/file.zip","destination":"archives"}]}""").ShouldContinue);
        var invalid = Decide(input, """{"status":"continue","operations":[{"type":"move","inputId":1,"destination":"../.."}]}""");
        Assert.IsTrue(invalid.NeedsHumanReview); StringAssert.Contains(invalid.RawText!, "../..");
    }
}
