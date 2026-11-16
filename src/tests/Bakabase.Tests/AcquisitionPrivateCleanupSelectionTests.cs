using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.Service.Components.Acquisition.Planning;
using Bakabase.Service.Components.FileProcessing;

namespace Bakabase.Tests;

[TestClass]
public sealed class AcquisitionPrivateCleanupSelectionTests
{
    private static readonly string FirstPath = Path.Combine(Path.GetTempPath(), "cleanup-selection-fixture", "first.bin");
    private static readonly string SecondPath = Path.Combine(Path.GetTempPath(), "cleanup-selection-fixture", "second.bin");
    private static readonly string ThirdPath = Path.Combine(Path.GetTempPath(), "cleanup-selection-fixture", "third.bin");
    private static readonly string Selection = new('a', 64);

    private static AcquisitionTaskDeleteCleanupGroup Group(string id, params string[] paths) => new(id,
        "intermediate", Path.GetDirectoryName(FirstPath)!, true, null,
        [new(1, id, "files.process")], paths.Select(p => new AcquisitionTaskDeleteCleanupFile(p, 12)).ToArray(),
        paths.Length, paths.Length * 12);

    private static ArtifactWorkspaceCleanupPreview Candidate(AcquisitionTaskDeleteCleanupGroup group) =>
        new(group.ArtifactId, "operation-" + group.ArtifactId, group.Directory, group.Eligible, null,
            group.Files.Count, group.Bytes, new string('f', 64))
        {
            PreviewFiles = group.Files.Select(f => (f.Path, f.Bytes)).ToArray(),
            PrivateSelectionFingerprint = Selection
        };

    private static AcquisitionTaskDeletionCleanupService.Plan Plan(int task,
        IReadOnlyList<AcquisitionTaskDeleteCleanupGroup> groups, IReadOnlyList<ArtifactWorkspaceCleanupPreview>? candidates = null)
    {
        var files = AcquisitionTaskDeletionCleanupService.Unique(groups.SelectMany(g => g.Files));
        return new(new(files.Count, files.Sum(f => f.Bytes), groups), "version",
            new ArtifactDeliveryCleanupConsent("consent", DateTimeOffset.UtcNow, task, 1, 1, true, [1], [], true),
            candidates ?? groups.Select(Candidate).ToArray());
    }

    [TestMethod]
    public void Same_task_identical_private_selections_are_executed_once_with_both_node_attributions()
    {
        var first = Group("first", FirstPath, SecondPath);
        var second = Group("second", SecondPath, FirstPath);
        var plans = new Dictionary<int, AcquisitionTaskDeletionCleanupService.Plan> {[1] = Plan(1, [first, second])};
        AcquisitionTaskDeletionService.ProtectOverlappingSelections(plans);
        var merged = plans[1].Summary.Groups.Single();
        Assert.IsTrue(merged.Eligible, merged.ReasonCode);
        CollectionAssert.AreEquivalent(new[] {"first", "second"}, merged.Nodes.Select(n => n.NodeId).ToArray());
        Assert.AreEqual(2, plans[1].Summary.FileCount);
        Assert.AreEqual(24L, plans[1].Summary.Bytes);
        Assert.AreEqual("first", plans[1].Candidates.Single().ArtifactId);
        Assert.AreEqual(new string('f', 64), plans[1].Candidates.Single().SelectionFingerprint,
            "Keep the original candidate-specific proof for execution revalidation.");
    }

    [DataTestMethod]
    [DataRow("partial-overlap")]
    [DataRow("metadata-changed")]
    [DataRow("bytes-changed")]
    [DataRow("missing-private-proof")]
    [DataRow("ineligible")]
    [DataRow("registered-output")]
    [DataRow("not-accepted")]
    [DataRow("content-checks-required")]
    public void Overlap_is_not_coalesced_without_complete_matching_private_authority(string scenario)
    {
        var first = Group("first", scenario == "partial-overlap" ? [FirstPath, ThirdPath] : [FirstPath]);
        var second = Group("second", scenario == "partial-overlap" ? [FirstPath, SecondPath] : [FirstPath]);
        if (scenario == "bytes-changed") second = second with {Files = [new(FirstPath, 13)], Bytes = 13};
        if (scenario == "ineligible") second = second with {Eligible = false, ReasonCode = "ReferencedPath"};
        if (scenario == "registered-output") second = second with {Kind = "processedFiles"};
        var a = Candidate(first); var b = Candidate(second);
        if (scenario == "metadata-changed") b = b with {PrivateSelectionFingerprint = new string('b', 64)};
        if (scenario == "missing-private-proof") b = b with {PrivateSelectionFingerprint = null};
        if (scenario == "registered-output") b = b with {PublishedArtifactId = "final"};
        var plan = Plan(1, [first, second], [a, b]);
        if (scenario == "not-accepted") plan = plan with {Consent = plan.Consent! with {DeliveryAccepted = false}};
        if (scenario == "content-checks-required") plan = plan with {Consent = plan.Consent! with {SkipFileVerification = false}};
        var plans = new Dictionary<int, AcquisitionTaskDeletionCleanupService.Plan> {[1] = plan};
        AcquisitionTaskDeletionService.ProtectOverlappingSelections(plans);
        Assert.AreEqual(2, plans[1].Summary.Groups.Count);
        Assert.IsTrue(plans[1].Summary.Groups.All(g => !g.Eligible && g.Shared));
        Assert.AreEqual(0, plans[1].Candidates.Count);
        Assert.AreEqual(0, plans[1].Summary.Bytes);
    }

    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Another_task_still_blocks_identical_private_files_even_after_local_deduplication(bool duplicateInsideFirstTask)
    {
        var a = Group("first", FirstPath);
        var b = Group("second", FirstPath);
        var outside = Group("outside", FirstPath);
        var plans = new Dictionary<int, AcquisitionTaskDeletionCleanupService.Plan>
        {
            [1] = Plan(1, duplicateInsideFirstTask ? [a, b] : [a]),
            [2] = Plan(2, [outside])
        };
        AcquisitionTaskDeletionService.ProtectOverlappingSelections(plans);
        Assert.IsTrue(plans.Values.SelectMany(p => p.Summary.Groups).All(g => !g.Eligible && g.Shared));
        Assert.IsTrue(plans.Values.All(p => p.Candidates.Count == 0 && p.Summary.Bytes == 0));
    }

    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Same_task_strict_private_subset_keeps_larger_original_execution_proof(bool smallerFirst)
    {
        var larger = Group("larger", FirstPath, SecondPath, ThirdPath);
        var smaller = Group("smaller", FirstPath, SecondPath);
        var a = Candidate(larger) with {PrivateSelectionFingerprint = new string('c', 64), SelectionFingerprint = "larger-proof"};
        var b = Candidate(smaller) with {PrivateSelectionFingerprint = new string('d', 64), SelectionFingerprint = "smaller-proof"};
        var plans = new Dictionary<int, AcquisitionTaskDeletionCleanupService.Plan>
            {[1] = Plan(1, smallerFirst ? [smaller, larger] : [larger, smaller], [a, b])};
        AcquisitionTaskDeletionService.ProtectOverlappingSelections(plans);
        var group = plans[1].Summary.Groups.Single();
        Assert.IsTrue(group.Eligible); Assert.AreEqual("larger", group.ArtifactId);
        CollectionAssert.AreEquivalent(new[] {"larger", "smaller"}, group.Nodes.Select(n => n.NodeId).ToArray());
        Assert.AreSame(a, plans[1].Candidates.Single());
        Assert.AreEqual("larger-proof", plans[1].Candidates.Single().SelectionFingerprint);
        Assert.AreEqual(3, plans[1].Summary.FileCount); Assert.AreEqual(36L, plans[1].Summary.Bytes);
    }

    [DataTestMethod]
    [DataRow("other-checkpoint")]
    [DataRow("missing-proof")]
    [DataRow("file-list-truncated")]
    [DataRow("candidate-list-mismatch")]
    [DataRow("bytes-changed")]
    [DataRow("ineligible")]
    [DataRow("not-accepted")]
    [DataRow("content-checks-required")]
    [DataRow("wrong-task-scope")]
    [DataRow("registered-output")]
    public void Strict_subset_does_not_override_missing_proof_or_cleanup_boundaries(string scenario)
    {
        var larger = Group("larger", FirstPath, SecondPath);
        var smaller = Group("smaller", FirstPath);
        if (scenario == "bytes-changed") smaller = smaller with {Files = [new(FirstPath, 13)], Bytes = 13};
        if (scenario == "ineligible") smaller = smaller with {Eligible = false, ReasonCode = "ReferencedPath"};
        if (scenario == "file-list-truncated") larger = larger with {FilesTruncated = true};
        if (scenario == "registered-output") smaller = smaller with {Kind = "processedFiles"};
        var a = Candidate(larger); var b = Candidate(smaller);
        if (scenario == "missing-proof") b = b with {PrivateSelectionFingerprint = null};
        if (scenario == "candidate-list-mismatch") a = a with {PreviewFiles = [(FirstPath, 12L)]};
        if (scenario == "registered-output") b = b with {PublishedArtifactId = "published"};
        if (scenario == "other-checkpoint") b = b with
            {ResultDirectory = Path.Combine(Path.GetDirectoryName(FirstPath)!, "other-checkpoint", "result")};
        var plan = Plan(1, [larger, smaller], [a, b]);
        if (scenario == "not-accepted") plan = plan with {Consent = plan.Consent! with {DeliveryAccepted = false}};
        if (scenario == "content-checks-required") plan = plan with {Consent = plan.Consent! with {SkipFileVerification = false}};
        if (scenario == "wrong-task-scope") plan = plan with {Consent = plan.Consent! with {TaskId = 2}};
        var plans = new Dictionary<int, AcquisitionTaskDeletionCleanupService.Plan> {[1] = plan};
        AcquisitionTaskDeletionService.ProtectOverlappingSelections(plans);
        Assert.AreEqual(2, plans[1].Summary.Groups.Count, scenario);
        Assert.IsTrue(plans[1].Summary.Groups.All(g => !g.Eligible && g.Shared), scenario);
        Assert.AreEqual(0, plans[1].Candidates.Count);
    }

    [TestMethod]
    public void Strict_subset_coalescing_keeps_registered_result_candidate_and_outside_task_protection()
    {
        var larger = Group("larger", FirstPath, SecondPath);
        var smaller = Group("smaller", FirstPath);
        var registered = Group("smaller", ThirdPath) with {Kind = "processedFiles"};
        var registeredProof = Candidate(registered) with {PublishedArtifactId = "published"};
        var plan = Plan(1, [larger, smaller, registered], [Candidate(larger), Candidate(smaller), registeredProof]);
        var plans = new Dictionary<int, AcquisitionTaskDeletionCleanupService.Plan> {[1] = plan};
        AcquisitionTaskDeletionService.ProtectOverlappingSelections(plans);
        Assert.AreEqual(2, plans[1].Summary.Groups.Count);
        Assert.IsTrue(plans[1].Summary.Groups.All(g => g.Eligible));
        Assert.AreEqual(2, plans[1].Candidates.Count);
        Assert.IsTrue(plans[1].Candidates.Contains(registeredProof));

        var outside = Group("outside", FirstPath);
        plans = new() {[1] = plan, [2] = Plan(2, [outside])};
        AcquisitionTaskDeletionService.ProtectOverlappingSelections(plans);
        Assert.IsFalse(plans[1].Summary.Groups.Single(g => g.ArtifactId == "larger").Eligible);
        Assert.IsFalse(plans[2].Summary.Groups.Single().Eligible);
        Assert.IsTrue(plans[1].Candidates.Contains(registeredProof));
    }
}
