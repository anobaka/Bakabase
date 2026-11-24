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
    public void Registered_cleanup_preview_counts_only_the_paths_left_after_a_stage_move()
    {
        var root = Directory.CreateTempSubdirectory("moved-stage-preview-");
        try
        {
            var kept = Path.Combine(root.FullName, "present.txt");
            var moved = Path.Combine(root.FullName, "moved.txt");
            File.WriteAllText(kept, "1234567");
            var manifest = new ArtifactManifest([Entry("present.txt", 7), Entry("moved.txt", 700)], "manifest");
            var source = new ArtifactRecord("artifact", "operation", "processedFiles", root.FullName, [kept, moved],
                manifest, manifest.Fingerprint, ArtifactLocationState.Available, true, [], null);
            var movedPaths = new HashSet<string>(StringComparer.Ordinal) {"moved.txt"};

            var remaining = ArchiveQuarantineService.PreviewMovedSourceFiles(source, movedPaths, default);

            CollectionAssert.AreEqual(new[] {(kept, 7L)}, remaining!.ToArray());
            Assert.IsNull(ArchiveQuarantineService.PreviewMovedSourceFiles(source, new HashSet<string>(StringComparer.Ordinal), default));
            File.WriteAllText(moved, new string('x', 700));
            Assert.IsNull(ArchiveQuarantineService.PreviewMovedSourceFiles(source, movedPaths, default));
            File.Delete(moved);
            File.Delete(kept);
            movedPaths.Add("present.txt");
            Assert.AreEqual(0, ArchiveQuarantineService.PreviewMovedSourceFiles(source, movedPaths, default)!.Count);
        }
        finally { root.Delete(recursive: true); }
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
