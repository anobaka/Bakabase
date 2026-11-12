using System.Text.Json;
using Bakabase.Service.Components.DownloadIntents;

namespace Bakabase.Tests.CloudDownloads;

[TestClass]
public sealed class ProcessingPlanPreflightTests
{
    [TestMethod]
    public void Instruction_workflow_rejects_missing_unknown_and_empty_required_before_downloading()
    {
        foreach (var (json, code) in new[]
        {
            ((string?)null, "PROCESSING_PLAN_REQUIRED"),
            ("{\"requirement\":\"unknown\",\"steps\":[]}", "PROCESSING_PLAN_UNKNOWN"),
            ("{\"requirement\":\"required\",\"steps\":[]}", "PROCESSING_PLAN_INCOMPLETE")
        })
        {
            var result = ProcessingPlanPreflight.Inspect(json, true);
            Assert.IsTrue(result.BlocksSubmission);
            Assert.AreEqual(code, result.Issues.Single().Code);
        }
    }

    [TestMethod]
    public void Download_only_or_explicit_keep_does_not_require_processing_instructions()
    {
        var result = ProcessingPlanPreflight.Inspect("not valid json", false);
        Assert.IsFalse(result.BlocksSubmission);
        Assert.AreEqual("bypassed", result.Status);
        Assert.AreEqual(0, result.Issues.Count);
        Assert.IsFalse(ProcessingPlanPreflight.Inspect("{\"requirement\":\"notRequired\",\"steps\":[]}", true).BlocksSubmission);
    }

    [TestMethod]
    public void Complete_syntax_does_not_claim_real_file_matching_and_invalid_plan_never_echoes_password()
    {
        var complete = ProcessingPlanPreflight.Inspect("{\"requirement\":\"required\",\"steps\":[{\"id\":\"open\",\"op\":\"extractArchive\",\"selector\":\"*.zip\"}]}", true);
        Assert.AreEqual("complete", complete.Status);
        Assert.IsFalse(complete.BlocksSubmission);
        var invalid = ProcessingPlanPreflight.Inspect("{\"requirement\":\"required\",\"steps\":[{\"id\":\"open\",\"op\":\"renameFile\",\"targetName\":\"../escape\",\"password\":\"private-fixture-secret\"}]}", true);
        Assert.IsTrue(invalid.BlocksSubmission);
        Assert.AreEqual("PROCESSING_PLAN_INVALID", invalid.Issues.Single().Code);
        Assert.IsFalse(JsonSerializer.Serialize(invalid).Contains("private-fixture-secret"));
    }
}
