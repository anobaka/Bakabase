using System.Diagnostics;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.Service.Components.FileProcessing;

namespace Bakabase.Tests.Artifacts;

[TestClass]
public sealed class ArtifactCleanupPathReferencesTests
{
    private static readonly string Root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "reference-index-fixture"));
    private static string P(params string[] parts) => Path.Combine([Root, .. parts]);

    [TestMethod]
    public void Normalized_snapshot_matches_previous_guard_for_aliases_ancestors_and_configuration_roots()
    {
        FileProcessingWorkspaceResolver.Relocation[] aliases =
        [new(P("legacy", "task-a"), P("work", "task-a")),
            new(P("legacy", "task-b"), P("work", "task-b")),
            new(P("unrelated-old"), P("unrelated-new"))];
        string[] roots = [P("work", "task-a", "result", "x.bin"), P("work", "task-b", "result"),
            P("current", "snapshot"), P("legacy", "task-a", "another.bin")];
        var paths = new List<string>
        {
            "relative/path", "", P(), P("legacy"), P("legacy", "task-a"), P("legacy", "task-b", "result", "child"),
            P("work"), P("work", "task-a"), P("work", "task-a", "result"), P("work", "task-a", "result", "x.bin"),
            P("work", "task-a", "result", "x.bin-extra"), P("work", "task-b", "result-other"),
            P("current", "snapshot", "child"), P("current", "snapshot") + Path.DirectorySeparatorChar,
            P("current", "other", "..", "snapshot"), P("CURRENT", "snapshot"), P("图像", "with spaces"),
            Path.GetPathRoot(Root)!
        };
        var random = new Random(25481);
        for (var i = 0; i < 1200; i++)
            paths.Add(P(random.Next(2) == 0 ? "legacy" : "work", "task-" + (char)('a' + random.Next(4)),
                random.Next(2) == 0 ? "result" : "result-other", "file-" + random.Next(20)));
        var indexed = new ArtifactCleanupPathReferences(roots, aliases, default);
        foreach (var ancestors in new[] {false, true})
        foreach (var path in paths)
            Assert.AreEqual(LegacyRefers(roots, aliases, path, ancestors), indexed.Refers(path, ancestors),
                $"Ancestor mode {ancestors}, path {path}");
    }

    [TestMethod]
    public void Reverse_alias_ancestors_are_preserved_but_allocation_root_mode_does_not_claim_siblings()
    {
        var aliases = new[] {new FileProcessingWorkspaceResolver.Relocation(P("old", "task"), P("ssd", "task"))};
        var indexed = new ArtifactCleanupPathReferences([P("ssd", "task", "private.bin")], aliases, default);
        Assert.IsTrue(indexed.Refers(P("old")));
        Assert.IsTrue(indexed.Refers(P("old", "task")));
        Assert.IsTrue(indexed.Refers(P("old", "task", "private.bin")));
        Assert.IsFalse(indexed.Refers(P("old"), ancestors: false));
        Assert.IsFalse(indexed.Refers(P("ssd"), ancestors: false));
        Assert.IsFalse(indexed.Refers(P("old", "task-other")));
        Assert.IsFalse(indexed.Refers(P("ssd", "other")));
    }

    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Matching_duplicate_or_overlapping_aliases_still_refuse_ambiguous_relocation(bool nested)
    {
        var aliases = new[] {new FileProcessingWorkspaceResolver.Relocation(P("old"), P("new-a")),
            new FileProcessingWorkspaceResolver.Relocation(nested ? P("old", "child") : P("old"), P("new-b"))};
        var indexed = new ArtifactCleanupPathReferences([P("safe")], aliases, default);
        Assert.ThrowsException<InvalidOperationException>(() => indexed.Refers(P("old", "child", "file")));
        Assert.ThrowsException<InvalidOperationException>(() => LegacyRefers([P("safe")], aliases, P("old", "child", "file"), true));
    }

    [TestMethod]
    public void Snapshot_keeps_mapping_and_candidate_inputs_immutable_and_observes_cancellation()
    {
        using var cancellation = new CancellationTokenSource();
        var aliases = new List<FileProcessingWorkspaceResolver.Relocation> {new(P("old"), P("new"))};
        var roots = new List<string> {P("new", "x")};
        var indexed = new ArtifactCleanupPathReferences(roots, aliases, cancellation.Token);
        aliases.Clear(); roots.Clear();
        Assert.IsTrue(indexed.Refers(P("old", "x")));
        var count = indexed.NormalizedPathCount;
        for (var i = 0; i < 100; i++) Assert.IsTrue(indexed.Refers(P("old", "x")));
        Assert.AreEqual(count, indexed.NormalizedPathCount);
        cancellation.Cancel();
        Assert.ThrowsException<OperationCanceledException>(() => indexed.Refers(P("old", "x")));
        Assert.ThrowsException<OperationCanceledException>(() => new ArtifactCleanupPathReferences([], [], cancellation.Token));
    }

    [TestMethod]
    public void Fifty_thousand_references_do_not_multiply_every_private_file_by_every_relocation()
    {
        const int referenceCount = 50_597, aliasCount = 260, privateFileCount = 180;
        var aliases = Enumerable.Range(0, aliasCount).Select(i =>
            new FileProcessingWorkspaceResolver.Relocation(P("old", "task-" + i), P("new", "task-" + i))).ToArray();
        var roots = Enumerable.Range(0, privateFileCount).Select(i => P("current", "private-" + i)).ToArray();
        var indexed = new ArtifactCleanupPathReferences(roots, aliases, default);
        var clock = Stopwatch.StartNew();
        for (var i = 0; i < referenceCount; i++) Assert.IsFalse(indexed.Refers(P("library", "resource-" + i)));
        // Count primitive normalized-path comparisons, not machine-dependent wall time.
        // The old nested loop performs roughly 2.3 billion reverse alias probes for this
        // shape. The new bound is references × (aliases + 2 × private files), plus setup.
        Assert.IsTrue(indexed.PathComparisonCount < (long)referenceCount * (aliasCount + 2 * privateFileCount + 1) + 100_000);
        Assert.IsTrue(indexed.NormalizedPathCount <= referenceCount + 2 * aliasCount + privateFileCount);
        Console.WriteLine($"Reference scaling: {referenceCount} references, {aliasCount} aliases, {privateFileCount} private roots; " +
                          $"{indexed.PathComparisonCount} comparisons, {indexed.NormalizedPathCount} normalized paths, {clock.ElapsedMilliseconds}ms.");
    }

    [TestMethod]
    public void Thousands_of_private_files_do_not_multiply_each_unrelated_reference()
    {
        const int privateFileCount = 5000, referenceCount = 5000;
        var roots = Enumerable.Range(0, privateFileCount)
            .Select(i => P("private", "game", "files", i.ToString("D5") + ".bin")).ToArray();
        var indexed = new ArtifactCleanupPathReferences(roots, [], default);
        for (var i = 0; i < referenceCount; i++)
            Assert.IsFalse(indexed.Refers(P("library", "other-" + i, "data.bin")));

        // The former roots.Any loop takes 50 million comparisons for this shape.
        // The bound now depends on path depth and log(candidate count), not their product.
        Assert.IsTrue(indexed.PathComparisonCount < (long)referenceCount * 64,
            $"Unexpected reference scan work: {indexed.PathComparisonCount}");
        Assert.IsTrue(indexed.Refers(P("private", "game")));
        Assert.IsFalse(indexed.Refers(P("private", "game"), ancestors: false));
        Assert.IsTrue(indexed.Refers(roots[^1], ancestors: false));
        Assert.IsFalse(indexed.Refers(P("private", "game-extra")));
    }

    [TestMethod]
    public void Prefix_index_preserves_nested_roots_siblings_and_reverse_aliases()
    {
        string[] roots = [P("a"), P("a-other"), P("nested", "folder", "item.bin"),
            P("nested", "folder-extra", "other.bin"), P("图像", "文件")];
        FileProcessingWorkspaceResolver.Relocation[] aliases = [new(P("old", "nested"), P("nested"))];
        var indexed = new ArtifactCleanupPathReferences(roots, aliases, default);
        string[] references = [P("a", "child"), P("a-else"), P("nested"), P("nested", "folder"),
            P("nested", "folder", "item.bin", "child"), P("nested", "folder", "item.bin-extra"),
            P("old"), P("old", "nested"), P("old", "unrelated"), P("图像"), P("图像", "文件-其他"),
            P("A", "child"), Path.GetPathRoot(Root)!];
        foreach (var ancestors in new[] {false, true})
        foreach (var reference in references)
            Assert.AreEqual(LegacyRefers(roots, aliases, reference, ancestors), indexed.Refers(reference, ancestors),
                $"Ancestor mode {ancestors}, path {reference}");
    }

    private static bool LegacyRefers(IReadOnlyList<string> candidateRoots,
        IReadOnlyList<FileProcessingWorkspaceResolver.Relocation> aliases, string value, bool ancestors)
    {
        static bool Under(string file, string parent) => Path.IsPathFullyQualified(parent) &&
            (ArtifactManifestFiles.PathComparer.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(file)),
                 Path.TrimEndingDirectorySeparator(Path.GetFullPath(parent))) ||
             Path.GetFullPath(file).StartsWith(Path.TrimEndingDirectorySeparator(Path.GetFullPath(parent)) + Path.DirectorySeparatorChar,
                 OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal));
        string Relocate(string path)
        {
            var full = Path.GetFullPath(path);
            var alias = aliases.SingleOrDefault(a => Under(full, a.SourceRoot));
            return alias == null ? full : Path.GetFullPath(Path.Combine(alias.DestinationRoot, Path.GetRelativePath(alias.SourceRoot, full)));
        }
        if (!Path.IsPathFullyQualified(value)) return false;
        var resolved = Relocate(value);
        return candidateRoots.Select(Relocate).Any(root => Under(resolved, root) || ancestors &&
            (Under(root, resolved) || aliases.Any(a => Under(a.SourceRoot, value) &&
                (Under(a.DestinationRoot, root) || Under(root, a.DestinationRoot)))));
    }
}
