using Bakabase.Modules.Acquisition.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Downloader.Cloud;
using Bakabase.Service.Components.Workflow;

namespace Bakabase.Tests;

[TestClass]
public sealed class AcquisitionSourceAttentionTagsTests
{
    [TestMethod]
    [DataRow("ORIGINAL_HTTP_RESUME_UNSUPPORTED")]
    [DataRow("ORIGINAL_HTTP_RANGE_INVALID")]
    public void Listing_a_share_cannot_resolve_its_original_file_range_failure(string code)
    {
        CollectionAssert.AreEqual(new[] {"sourceNeedsVerification"}, ExecutionProblemClassifier.Classify([code]).ToArray());
        Assert.AreEqual(SourceAvailabilityStatuses.Unknown, ExecutionProblemClassifier.AvailabilityStatus(code));
        Assert.IsFalse(ExecutionProblemClassifier.IsRefreshableSourceCode(code));
        CollectionAssert.AreEqual(new[] {code}, WorkflowSourceRunProblems.Extract("CloudProviderException: " + code).ToArray());
    }

    [TestMethod]
    [DataRow("SOURCE_UNSUPPORTED")]
    [DataRow("DOWNLOAD_METHOD_UNAVAILABLE")]
    [DataRow("NATIVE_DOCUMENT_EXPORT_UNSUPPORTED")]
    [DataRow("SHAREPOINT_FOLDER_REQUIRED")]
    public void UnsupportedDownloadCapabilityDoesNotClaimMissingInformationOrAnExpiredLink(string code)
    {
        CollectionAssert.AreEqual(new[] {"downloadSourceUnsupported"}, ExecutionProblemClassifier.Classify([code]).ToArray());
        Assert.AreEqual(SourceAvailabilityStatuses.Unknown, ExecutionProblemClassifier.AvailabilityStatus(code));
        Assert.IsFalse(ExecutionProblemClassifier.IsRefreshableSourceCode(code));
    }

    [TestMethod]
    [DataRow("BUILDER_NO_RESULTS")]
    [DataRow("HANDOFF_NO_MAIN_SOURCE")]
    [DataRow("SOURCE_INVALID")]
    [DataRow("SOURCE_URL_INVALID")]
    [DataRow("TERABOX_SOURCE_INVALID")]
    [DataRow("MEGA_SHARE_INVALID")]
    public void MissingOrIncompleteDownloadInformationHasItsOwnTag(string code) =>
        CollectionAssert.AreEqual(new[] {"downloadInfoMissing"}, ExecutionProblemClassifier.Classify([code]).ToArray());

    [TestMethod]
    [DataRow("SOURCE_SELECTION_REQUIRED", "sourceSelectionRequired")]
    [DataRow("SOURCE_DISABLED", "sourceSelectionRequired")]
    [DataRow("ONE_MIRROR_PER_GROUP", "sourceSelectionRequired")]
    [DataRow("PLAN_REPLAN_SOURCE_CHANGED", "sourceSelectionRequired")]
    [DataRow("PIXELDRAIN_DIRECTORY_REQUIRED", "sourceSelectionRequired")]
    [DataRow("PIXELDRAIN_TRANSFER_QUOTA_EXCEEDED", "downloadQuotaExceeded")]
    [DataRow("PIXELDRAIN_DOWNLOAD_QUOTA_EXCEEDED", "downloadQuotaExceeded")]
    [DataRow("SOURCE_DOWNLOAD_QUOTA_EXCEEDED", "downloadQuotaExceeded")]
    [DataRow("OUTPUT_DIRECTORY_REQUIRED", "configurationRequired")]
    [DataRow("ADAPTER_NOT_READY", "configurationRequired")]
    [DataRow("WORKFLOW_INCOMPATIBLE", "configurationRequired")]
    [DataRow("DEPENDENCY_REQUIRED", "dependencyMissing")]
    [DataRow("BAIDU_SHARE_EXPIRED", "sourceUnavailable")]
    [DataRow("BUILDER_SEARCH_FAILED", "other")]
    [DataRow("BUILDER_FAILED", "other")]
    [DataRow("SOURCE_CHECK_PASSED_RETRY_REQUIRED", "other")]
    public void SelectionConfigurationAndExecutionFailuresDoNotInventMissingOrUnsupportedSources(string code, string tag) =>
        CollectionAssert.AreEqual(new[] {tag}, ExecutionProblemClassifier.Classify([code]).ToArray());

    [TestMethod]
    public void WaitingForAChoiceIsDifferentFromHavingNoDownloadInformation()
    {
        CollectionAssert.AreEqual(new[] {"downloadInfoMissing"},
            ExecutionProblemClassifier.Classify([], legacyWaitReason: AcquisitionWaitReason.NoLinks).ToArray());
        CollectionAssert.AreEqual(new[] {"sourceSelectionRequired"},
            ExecutionProblemClassifier.Classify([], legacyWaitReason: AcquisitionWaitReason.ChooseLink).ToArray());
        foreach (var reason in new[] {"cloud.chooseSources", "cloud.chooseMethod"})
            CollectionAssert.AreEqual(new[] {"sourceSelectionRequired"}, ExecutionProblemClassifier.Classify([], reason).ToArray());
        CollectionAssert.AreEqual(new[] {"configurationRequired"}, ExecutionProblemClassifier.Classify([], "cloud.chooseDirectory").ToArray());
        CollectionAssert.AreEqual(new[] {"fileProcessing"},
            ExecutionProblemClassifier.Classify(["PROCESSING_PLAN_INCOMPLETE"], "cloud.fileProcessing").ToArray());
        CollectionAssert.DoesNotContain(ExecutionProblemClassifier.AttentionTags, "planNeedsInput");
    }
}
