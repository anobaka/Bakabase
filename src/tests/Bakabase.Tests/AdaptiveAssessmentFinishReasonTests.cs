using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Service.Components.FileProcessing;
using Microsoft.Extensions.AI;

namespace Bakabase.Tests;

[TestClass]
public class AdaptiveAssessmentFinishReasonTests
{
    [TestMethod]
    public void ReportsTimeoutOnlyWithPositiveProviderEvidence()
    {
        var sdkTimeout = new System.Threading.Tasks.TaskCanceledException(
            "The operation was cancelled because it exceeded the configured timeout of 0:01:40.");
        Assert.IsTrue(AdaptiveAssessmentDiagnostic.IsProviderTimeout(
            new System.AggregateException(sdkTimeout, new System.TimeoutException())));
        Assert.IsFalse(AdaptiveAssessmentDiagnostic.IsProviderTimeout(
            new System.Threading.Tasks.TaskCanceledException("Cancelled by caller")));
        Assert.IsFalse(AdaptiveAssessmentDiagnostic.IsProviderTimeout(
            new System.AggregateException(sdkTimeout, new System.Net.Http.HttpRequestException("Connection refused"))));
    }

    private static readonly ArtifactRecord Input = new("id", "op", "processedFiles", "/input",
        ["file"], new([new("file", 4, "sha")], "fingerprint"), "fingerprint",
        ArtifactLocationState.Available, true, [], null);

    [TestMethod]
    public void LengthStopDoesNotAuthorizeDeliveryEvenWhenThePartialReplyContainsValidJson()
    {
        var message = new ChatMessage(ChatRole.Assistant, """{"status":"final"}""");
        var limited = AdaptiveFileProcessingAssessmentService.ParseAndValidate(
            new ChatResponse(message) { FinishReason = ChatFinishReason.Length }, Input, ["file"], false);
        Assert.IsTrue(limited.NeedsHumanReview);
        StringAssert.StartsWith(limited.Reason!, "LLM_OUTPUT_TRUNCATED");
        Assert.IsFalse(limited.IsFinal);
        Assert.AreEqual(0, limited.Operations.Count);

        var complete = AdaptiveFileProcessingAssessmentService.ParseAndValidate(
            new ChatResponse(message) { FinishReason = ChatFinishReason.Stop }, Input, ["file"], false);
        Assert.IsTrue(complete.IsFinal);
    }
}
