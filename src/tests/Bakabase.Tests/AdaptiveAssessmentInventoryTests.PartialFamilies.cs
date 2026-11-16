using Bakabase.Service.Components.FileProcessing;
using Microsoft.Extensions.AI;

namespace Bakabase.Tests;

public sealed partial class AdaptiveAssessmentInventoryTests
{
    [DataTestMethod]
    [DataRow("parts/archive.7z.001", "parts/archive.7z.002", "parts/archive.7z.00*")]
    [DataRow("parts/archive.part1.rar", "parts/archive.part2.rar", "parts/archive.part*")]
    [DataRow("parts/archive.zip", "parts/archive.z01", "parts/archive.z*")]
    public void CompleteVisibleFamilyCanBeProcessedWithUnrelatedUnshownFiles(string first, string second, string selector)
    {
        var input = Input(Enumerable.Range(0, 1051).Select(i => $"game/assets/{i:D4}.png")
            .Concat([first, second]).ToArray());
        var response = new ChatResponse(new ChatMessage(ChatRole.Assistant,
            """{"status":"continue","operations":[{"type":"extract","inputId":1}]}"""));
        var decision = AdaptiveFileProcessingAssessmentService.ParseAndValidate(response, input, [first, second], true);
        Assert.IsTrue(decision.ShouldContinue, decision.ValidationError);
        Assert.AreEqual(selector, AdaptiveFileProcessingAssessmentService.BuildPlan(decision)!.Steps.Single().Selector);
    }

    [DataTestMethod]
    [DataRow("hidden-volume")]
    [DataRow("hidden-same-prefix-nonmember")]
    [DataRow("missing-first")]
    [DataRow("wrong-entry")]
    [DataRow("multiple-operations")]
    [DataRow("hidden-namesake-directory")]
    public void PartialFamilyProposalsCannotSelectHiddenOrUnprovenFiles(string scenario)
    {
        var registered = new List<string> {"parts/archive.7z.001", "parts/archive.7z.002", "other/image.png"};
        var visible = new[] {registered[0], registered[1]};
        var operation = """{"type":"extract","inputId":1}""";
        switch (scenario)
        {
            case "hidden-volume": registered.Add("parts/archive.7z.003"); break;
            case "hidden-same-prefix-nonmember": registered.Add("parts/archive.7z.001.backup"); break;
            case "missing-first":
                registered.RemoveAt(0); registered.Insert(1, "parts/archive.7z.003");
                visible = [registered[0], registered[1]]; break;
            case "wrong-entry": operation = """{"type":"extract","inputId":2}"""; break;
            case "multiple-operations": operation += "," + """{"type":"retain","inputId":2}"""; break;
            case "hidden-namesake-directory":
                registered = ["archive.7z.001", "archive.7z.002", "nested/archive.7z.001"];
                visible = [registered[0], registered[1]]; break;
        }
        var input = Input(registered.ToArray());
        var response = new ChatResponse(new ChatMessage(ChatRole.Assistant,
            "{\"status\":\"continue\",\"operations\":[" + operation + "]}"));
        var decision = AdaptiveFileProcessingAssessmentService.ParseAndValidate(response, input, visible, true);
        Assert.IsTrue(decision.NeedsHumanReview, scenario);
        StringAssert.Contains(decision.ValidationError!, scenario == "hidden-namesake-directory"
            ? "selector is ambiguous" : "multipart archive");
        Assert.IsNull(AdaptiveFileProcessingAssessmentService.BuildPlan(decision));
    }
}
