using Bakabase.Modules.Downloader.Cloud;
using Bakabase.Service.Components.Workflow;

namespace Bakabase.Tests;

[TestClass]
public sealed class LocalStorageAttentionTests
{
    [DataTestMethod]
    [DataRow("LOCAL_STORAGE_FULL", true)]
    [DataRow("LOCAL_STORAGE_RESERVE_LOW", true)]
    [DataRow("DiskSpaceLimit", true)]
    [DataRow("LOCAL_STORAGE_ACCESS_DENIED", false)]
    [DataRow("LOCAL_STORAGE_UNAVAILABLE", false)]
    [DataRow("LOCAL_STORAGE_WRITE_FAILED", false)]
    [DataRow("PUBLICATION_IO_ERROR", false)]
    [DataRow("BAIDU_OUTPUT_DIRECTORY_FAILED", false)]
    [DataRow("BAIDU_LOCAL_PATH_TOO_LONG", false)]
    [DataRow("CLOUD_STORAGE_QUOTA_EXCEEDED", false)]
    [DataRow("LOCAL_STORAGE_SOMETHING_UNKNOWN", false)]
    [DataRow("untrusted message LOCAL_STORAGE_FULL", false)]
    [DataRow(null, false)]
    public void CapacityFacetRequiresAnExactKnownCapacityReason(string? code, bool expected)
    {
        Assert.AreEqual(expected, ExecutionProblemClassifier.IsSpaceInsufficient(code));
    }

    [TestMethod]
    public void ArtifactPublicationWaitIsFileProcessingWithoutInventingDownloadCompletion()
    {
        CollectionAssert.AreEqual(new[] {"localStorage", "fileProcessing"},
            ExecutionProblemClassifier.Classify(["LOCAL_STORAGE_RESERVE_LOW"], "artifacts.publication").ToArray());
        CollectionAssert.AreEqual(new[] {"localStorage", "fileProcessing"},
            ExecutionProblemClassifier.Classify(["LOCAL_STORAGE_ACCESS_DENIED"], "artifacts.publication").ToArray());
        Assert.IsFalse(ExecutionProblemClassifier.IsSpaceInsufficient("LOCAL_STORAGE_ACCESS_DENIED"));
    }

    [DataTestMethod]
    [DataRow("BAIDU_DOWNLOAD_PARTIAL_UNVERIFIED")]
    [DataRow("BAIDU_DOWNLOAD_CHECKPOINT_INVALID")]
    [DataRow("BAIDU_OUTPUT_UNEXPECTED_FILE")]
    public void UnverifiedLocalContinuationRequiresReviewAndDoesNotImplyShareExpiryOrDiskFull(string code)
    {
        CollectionAssert.AreEqual(new[] {"other"}, ExecutionProblemClassifier.Classify([code]).ToArray());
        CollectionAssert.AreEqual(new[] {code}, WorkflowSourceRunProblems.Extract("Provider: " + code).ToArray());
        Assert.AreEqual(SourceAvailabilityStatuses.Unknown, ExecutionProblemClassifier.AvailabilityStatus(code));
        Assert.IsFalse(ExecutionProblemClassifier.IsRefreshableSourceCode(code));
    }

    [DataTestMethod]
    [DataRow("LOCAL_STORAGE_FULL")]
    [DataRow("LOCAL_STORAGE_WRITE_FAILED")]
    [DataRow("LOCAL_STORAGE_ACCESS_DENIED")]
    [DataRow("LOCAL_STORAGE_UNAVAILABLE")]
    [DataRow("LOCAL_STORAGE_RESERVE_LOW")]
    public void LocalStorageFailureIsNotCloudQuotaOrShareValidity(string code)
    {
        CollectionAssert.AreEqual(new[] {"localStorage"}, ExecutionProblemClassifier.Classify([code]).ToArray());
        Assert.AreEqual(SourceAvailabilityStatuses.Unknown, ExecutionProblemClassifier.AvailabilityStatus(code));
        Assert.AreEqual(code, ExecutionProblemClassifier.NormalizedReason(code));
        Assert.IsFalse(ExecutionProblemClassifier.IsRefreshableSourceCode(code));
        CollectionAssert.AreEqual(new[] {code}, WorkflowSourceRunProblems.Extract(
            $"LocalStorageException: {code}; https://example.invalid/private?token=SECRET_VALUE").ToArray());
    }

    [DataTestMethod]
    [DataRow("BAIDU_OUTPUT_DIRECTORY_FAILED", "LOCAL_STORAGE_WRITE_FAILED")]
    [DataRow("PUBLICATION_IO_ERROR", "LOCAL_STORAGE_WRITE_FAILED")]
    [DataRow("RECOVERY_STORAGE_UNAVAILABLE", "LOCAL_STORAGE_UNAVAILABLE")]
    [DataRow("StorageUnavailable", "LOCAL_STORAGE_UNAVAILABLE")]
    [DataRow("DiskSpaceLimit", "LOCAL_STORAGE_FULL")]
    public void ExistingStructuredReasonsUseStorageFacetWithoutInventingMissingOsEvidence(string oldCode, string normalized)
    {
        Assert.AreEqual(normalized, ExecutionProblemClassifier.NormalizedReason(oldCode));
        CollectionAssert.AreEqual(new[] {"localStorage"}, ExecutionProblemClassifier.Classify([oldCode]).ToArray());
        if (normalized == "LOCAL_STORAGE_WRITE_FAILED")
            StringAssert.Contains(ExecutionProblemClassifier.Message(oldCode), "does not establish a full disk");
    }

    [TestMethod]
    public void IndependentSourceAndCloudCapacityProblemsRemainVisibleBesideLocalStorage()
    {
        CollectionAssert.AreEqual(new[] {"sourceUnavailable", "cloudStorageFull", "localStorage"},
            ExecutionProblemClassifier.Classify(["BAIDU_SHARE_EXPIRED", "PIKPAK_STORAGE_QUOTA_EXCEEDED", "LOCAL_STORAGE_FULL"]).ToArray());
        CollectionAssert.AreEqual(new[] {"localStorage", "fileProcessing"},
            ExecutionProblemClassifier.Classify(["StorageUnavailable"], "cloud.fileProcessing").ToArray());
        CollectionAssert.AreEqual(new[] {"other"}, ExecutionProblemClassifier.Classify(["LOCAL_STORAGE_SOMETHING_UNKNOWN"]).ToArray());
        Assert.AreEqual(0, WorkflowSourceRunProblems.Extract("LOCAL_STORAGE_SOMETHING_UNKNOWN https://private.invalid?token=SECRET_VALUE").Count);
    }
    [DataTestMethod]
    [DataRow("FILE_DELIVERY_ARCHIVE_REVIEW_REQUIRED")]
    [DataRow("FILE_DELIVERY_RESULT_UNCERTAIN")]
    [DataRow("FILE_DELIVERY_INPUT_CHANGED")]
    [DataRow("FILE_DELIVERY_REVIEW_CHANGED")]
    [DataRow("FILE_CLEANUP_COVERAGE_INCOMPLETE")]
    public void DeliveryEvidenceNeedsFileReviewAndDoesNotInvalidateRemoteSource(string code)
    {
        CollectionAssert.AreEqual(new[] {"fileProcessing"}, ExecutionProblemClassifier.Classify([code]).ToArray());
        Assert.AreEqual(SourceAvailabilityStatuses.Unknown, ExecutionProblemClassifier.AvailabilityStatus(code));
        Assert.IsFalse(ExecutionProblemClassifier.IsRefreshableSourceCode(code));
    }

}
