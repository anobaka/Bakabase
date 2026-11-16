using Bakabase.Service.Components.FileProcessing;
using Microsoft.Extensions.AI;

namespace Bakabase.Tests;

public sealed partial class AdaptiveAssessmentInventoryTests
{
    [TestMethod]
    public void Sfx_entry_proposal_binds_the_complete_visible_family_without_selecting_another_package()
    {
        var first = "package/bundle.part1.exe";
        var second = "package/bundle.part2.rar";
        var input = Input(first, second, "package/other.part1.exe", "package/other.part2.rar", "key.txt");
        var decision = Decide(input, """{"status":"continue","operations":[{"type":"extract","inputId":1}]}""");
        Assert.IsTrue(decision.ShouldContinue, decision.ValidationError);
        Assert.AreEqual("package/bundle.part*", AdaptiveFileProcessingAssessmentService.BuildPlan(decision)!.Steps.Single().Selector);
        Assert.IsTrue(Decide(input, """{"status":"final","operations":[]}""").NeedsHumanReview);
    }

    [DataTestMethod]
    [DataRow("missing-middle")]
    [DataRow("ambiguous-entry")]
    [DataRow("hidden-volume")]
    [DataRow("later-volume")]
    public void Sfx_AI_selection_does_not_authorize_incomplete_hidden_or_wrong_entry_members(string scenario)
    {
        var paths = new List<string> {"bundle.part1.exe", "bundle.part2.rar"};
        var selected = 1;
        if (scenario == "missing-middle") paths[1] = "bundle.part3.rar";
        if (scenario == "ambiguous-entry") paths.Add("bundle.part1.rar");
        if (scenario == "later-volume") selected = 2;
        var visible = paths.ToArray();
        if (scenario == "hidden-volume") paths.Add("bundle.part3.rar");
        var decision = AdaptiveFileProcessingAssessmentService.ParseAndValidate(
            new ChatResponse(new ChatMessage(ChatRole.Assistant,
                "{\"status\":\"continue\",\"operations\":[{\"type\":\"extract\",\"inputId\":" + selected + "}]}")),
            Input(paths.ToArray()), visible, scenario == "hidden-volume");
        Assert.IsTrue(decision.NeedsHumanReview, scenario);
        Assert.IsNull(AdaptiveFileProcessingAssessmentService.BuildPlan(decision));
    }

    [TestMethod]
    public void An_unrelated_later_volume_never_expands_an_explicit_executable_selection()
    {
        var decision = Decide(Input("bundle.part1.exe", "other.part2.rar"),
            """{"status":"continue","operations":[{"type":"extract","inputId":1}]}""");
        Assert.IsTrue(decision.ShouldContinue, decision.ValidationError);
        Assert.AreEqual("bundle.part1.exe", AdaptiveFileProcessingAssessmentService.BuildPlan(decision)!.Steps.Single().Selector);
    }
}
