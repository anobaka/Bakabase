using System.Collections;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Service.Components.Acquisition.Planning;
using Bakabase.Service.Components.FileProcessing;

namespace Bakabase.Tests.Artifacts;

[TestClass]
public class RelocatedStageManifestTests
{
    private sealed class SinglePassList<T>(IReadOnlyList<T> items) : IReadOnlyList<T>
    {
        private int enumerations;
        public int Count => items.Count;
        public T this[int index] => items[index];
        public IEnumerator<T> GetEnumerator()
        {
            Assert.AreEqual(1, ++enumerations, "A stage proof scanned its manifest more than once.");
            return items.GetEnumerator();
        }
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private static ArtifactManifestEntry Entry(string path, int length = 17) => new(path, length, new string('a', 64));
    private static AcquisitionStageDirectoryMoveFile Moved(ArtifactManifestEntry e) => new(e.RelativePath, e.Length, 0, Completed: true);

    [TestMethod]
    public void Large_stage_receipts_and_relocated_manifests_match_in_one_pass_in_any_order()
    {
        var files = Enumerable.Range(0, 86_141).Select(i => Entry($"group/{i:D6}.dat", i)).ToArray();
        var moved = files.Skip(1).Reverse().Select(Moved).ToArray();
        Assert.IsTrue(AcquisitionProcessingStageCommit.MatchesMovedFiles(new SinglePassList<ArtifactManifestEntry>(files),
            new SinglePassList<AcquisitionStageDirectoryMoveFile>(moved), [files[0].RelativePath]));
        var original = new ArtifactManifest(new SinglePassList<ArtifactManifestEntry>(files), "manifest", ["folder"]);
        var reversed = original with {Entries = new SinglePassList<ArtifactManifestEntry>(files.Reverse().ToArray())};
        var root = Path.GetTempPath();
        Assert.IsTrue(new FileProcessingRelocatedResult(root, root, reversed).Matches(root, original));
    }

    [TestMethod]
    public void Shared_or_changed_paths_cannot_prove_a_complete_stage_move()
    {
        var a = Entry("a"); var b = Entry("b");
        Assert.IsFalse(AcquisitionProcessingStageCommit.MatchesMovedFiles([a, b], [Moved(a)], ["a"]));
        Assert.IsFalse(AcquisitionProcessingStageCommit.MatchesMovedFiles([a, a], [Moved(a)], ["b"]));
        Assert.IsFalse(AcquisitionProcessingStageCommit.MatchesMovedFiles([a, b], [Moved(a) with {Completed = false}], ["b"]));
        Assert.IsFalse(AcquisitionProcessingStageCommit.MatchesMovedFiles([a, b], [Moved(a) with {Length = 1}], ["b"]));
        Assert.IsFalse(AcquisitionProcessingStageCommit.MatchesMovedFiles([a, b], [Moved(a)], ["B"]));
        Assert.IsFalse(AcquisitionProcessingStageCommit.MatchesMovedFiles([a, b], [Moved(a)], []));
    }

    [TestMethod]
    public void Owned_subset_requires_distinct_ordinal_paths_and_matching_lengths_and_hashes()
    {
        var a = Entry("a"); var b = Entry("b");
        Assert.IsTrue(FileProcessingRelocatedResult.CoversEntries([a, b], [b with {Sha256 = b.Sha256.ToUpperInvariant()}]));
        Assert.IsFalse(FileProcessingRelocatedResult.CoversEntries([a, b], [b, b]));
        Assert.IsFalse(FileProcessingRelocatedResult.CoversEntries([a, a], [a]));
        Assert.IsFalse(FileProcessingRelocatedResult.CoversEntries([a, b], [b with {RelativePath = "B"}]));
        Assert.IsFalse(FileProcessingRelocatedResult.CoversEntries([a, b], [b with {Sha256 = new string('b', 64)}]));
        Assert.IsFalse(FileProcessingRelocatedResult.CoversEntries([a, b], [b with {Length = 1}]));
    }
}
