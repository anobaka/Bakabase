using Bakabase.Modules.Downloader.Cloud;
using Bakabase.Service.Components.Workflow;

namespace Bakabase.Tests;

[TestClass]
public sealed class CloudCapacityAttentionTests
{
    [DataTestMethod]
    [DataRow("TERABOX_DOWNLOAD_IDENTITY_REJECTED")]
    [DataRow("TERABOX_DOWNLOAD_ACCESS_DENIED")]
    public void DownloadAuthorizationFailureIsNotShareExpiryOrClearedByPublicListing(string code)
    {
        CollectionAssert.AreEqual(new[] {"accessRequired"}, ExecutionProblemClassifier.Classify([code]).ToArray());
        Assert.AreEqual(SourceAvailabilityStatuses.AccessRequired, ExecutionProblemClassifier.AvailabilityStatus(code));
        Assert.AreEqual(code, ExecutionProblemClassifier.NormalizedReason(code));
        Assert.IsFalse(ExecutionProblemClassifier.IsRefreshableSourceCode(code));
        CollectionAssert.AreEqual(new[] {code}, WorkflowSourceRunProblems.Extract("CloudProviderException: " + code).ToArray());
    }

    [TestMethod]
    public void AccountCapacityRemainsDistinctFromLinkValidityAndNetworkFailures()
    {
        foreach (var code in new[] {"PIKPAK_STORAGE_QUOTA_EXCEEDED", "CLOUD_STORAGE_QUOTA_EXCEEDED"})
        {
            CollectionAssert.AreEqual(new[] {"cloudStorageFull"}, ExecutionProblemClassifier.Classify([code]).ToArray());
            Assert.AreEqual(SourceAvailabilityStatuses.Unknown, ExecutionProblemClassifier.AvailabilityStatus(code));
            Assert.IsFalse(ExecutionProblemClassifier.IsRefreshableSourceCode(code),
                "A public share listing cannot establish account capacity or justify another transfer.");
        }
        CollectionAssert.AreEqual(new[] {"networkIssue"},
            ExecutionProblemClassifier.Classify(["PIKPAK_TRANSFER_QUOTA_EXCEEDED"]).ToArray());
        CollectionAssert.AreEqual(new[] {"sourceUnavailable", "cloudStorageFull"},
            ExecutionProblemClassifier.Classify(["PIKPAK_SHARE_EXPIRED", "PIKPAK_STORAGE_QUOTA_EXCEEDED"]).ToArray());
    }
}
