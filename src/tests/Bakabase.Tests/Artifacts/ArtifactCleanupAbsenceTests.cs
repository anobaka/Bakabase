using Bakabase.Service.Components.FileProcessing;

namespace Bakabase.Tests.Artifacts;

[TestClass]
public sealed class ArtifactCleanupAbsenceTests
{
    private string root = null!;
    [TestInitialize] public void Setup() { root = Path.Combine(Path.GetTempPath(), "cleanup-absence-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root); }
    [TestCleanup] public void Cleanup() { if (Directory.Exists(root)) Directory.Delete(root, true); }

    [TestMethod]
    public void Removed_nested_directories_are_absent_only_below_a_readable_registered_root()
    {
        var parent = Path.Combine(root, "nested"); Directory.CreateDirectory(parent);
        var path = Path.Combine(parent, "gone.bin");
        Assert.AreEqual(ArtifactCleanupPathPresence.ConfirmedAbsent, ArtifactCleanupAbsence.Probe(root, path));
        Directory.Delete(parent);
        Assert.AreEqual(ArtifactCleanupPathPresence.ConfirmedAbsent, ArtifactCleanupAbsence.Probe(root, path));
        Directory.Delete(root);
        Assert.AreEqual(ArtifactCleanupPathPresence.Unavailable, ArtifactCleanupAbsence.Probe(root, path));
    }

    [TestMethod]
    public void A_removed_subtree_is_absent_but_a_file_replacing_its_parent_is_unavailable()
    {
        var directory = Path.Combine(root, "removed");
        var path = Path.Combine(directory, "also-removed", "gone.bin");
        Assert.AreEqual(ArtifactCleanupPathPresence.ConfirmedAbsent, ArtifactCleanupAbsence.Probe(root, path));
        File.WriteAllText(directory, "a new unrelated file");
        Assert.AreEqual(ArtifactCleanupPathPresence.Unavailable, ArtifactCleanupAbsence.Probe(root, path));
    }

    [DataTestMethod]
    [DataRow("io")]
    [DataRow("permission")]
    public void Storage_or_permission_errors_are_not_absence(string kind)
    {
        Exception failure = kind == "io" ? new IOException("unreachable") : new UnauthorizedAccessException("denied");
        Assert.AreEqual(ArtifactCleanupPathPresence.Unavailable,
            ArtifactCleanupAbsence.Probe(root, Path.Combine(root, "file.bin"), _ => throw failure));
    }

    [TestMethod]
    public async Task Existing_directory_or_file_and_late_reappearance_are_never_absence()
    {
        var path = Path.Combine(root, "file.bin");
        var reads = 0;
        Assert.AreEqual(ArtifactCleanupPathPresence.Present, ArtifactCleanupAbsence.Probe(root, path, name =>
        {
            if (++reads == 2) File.WriteAllText(name, "new file");
            _ = File.GetAttributes(name);
        }));
        Assert.AreEqual("new file", await File.ReadAllTextAsync(path));
        Directory.CreateDirectory(Path.Combine(root, "folder"));
        Assert.AreEqual(ArtifactCleanupPathPresence.Present, ArtifactCleanupAbsence.Probe(root, Path.Combine(root, "folder")));
    }

    [TestMethod]
    public async Task Resumed_deletion_confirms_the_original_name_without_touching_an_SMB_placeholder()
    {
        var file = Path.Combine(root, "member.bin");
        var held = Path.Combine(root, ".smbdeleteAAA59fc4.4");
        await File.WriteAllTextAsync(held, "held by a client");
        // The directory lookup is cached as present even though opening that name returns ENOENT.
        Assert.AreEqual(ArtifactCleanupPathPresence.Present, ArtifactCleanupAbsence.Probe(root, file, _ => { }));
        Assert.IsTrue(ArtifactCleanupAbsence.AllConfirmedAbsent(root, [file],
            observe: ArtifactCleanupAbsence.OpenPendingDeletionFile));
        Assert.AreEqual("held by a client", await File.ReadAllTextAsync(held));

        await File.WriteAllTextAsync(file, "new content");
        Assert.AreEqual(ArtifactCleanupPathPresence.Present,
            ArtifactCleanupAbsence.Probe(root, file, ArtifactCleanupAbsence.OpenPendingDeletionFile));
        // An open failure with the name still in a readable directory is inconclusive.
        Assert.AreEqual(ArtifactCleanupPathPresence.Unavailable,
            ArtifactCleanupAbsence.Probe(root, file, _ => throw new FileNotFoundException()));
        Assert.IsFalse(ArtifactCleanupAbsence.AllConfirmedAbsent(root, [file],
            observe: ArtifactCleanupAbsence.OpenPendingDeletionFile));
        Assert.AreEqual("new content", await File.ReadAllTextAsync(file));
    }

    [TestMethod]
    public async Task Full_nonempty_selection_is_required_including_files_after_a_ui_preview_limit()
    {
        var paths = Enumerable.Range(0, 201).Select(i => Path.Combine(root, $"file-{i}.bin")).ToArray();
        Assert.IsFalse(ArtifactCleanupAbsence.AllConfirmedAbsent(root, []));
        Assert.IsFalse(ArtifactCleanupAbsence.AllConfirmedAbsent(root, [paths[0], paths[0]]));
        await File.WriteAllTextAsync(paths[200], "still present");
        Assert.IsFalse(ArtifactCleanupAbsence.AllConfirmedAbsent(root, paths));
        Assert.IsTrue(ArtifactCleanupAbsence.AllConfirmedAbsent(root, paths.Take(200).ToArray()));
        File.Delete(paths[200]);
        Assert.IsTrue(ArtifactCleanupAbsence.AllConfirmedAbsent(root, paths));
    }

    [TestMethod]
    public void Outside_root_and_symbolic_link_paths_are_unavailable()
    {
        Assert.AreEqual(ArtifactCleanupPathPresence.Unavailable,
            ArtifactCleanupAbsence.Probe(root, Path.Combine(root, "..", "gone.bin")));
        if (OperatingSystem.IsWindows()) return;
        var linked = Path.Combine(root, "link"); Directory.CreateSymbolicLink(linked, root);
        Assert.AreEqual(ArtifactCleanupPathPresence.Unavailable,
            ArtifactCleanupAbsence.Probe(root, Path.Combine(linked, "gone.bin")));
        Directory.Delete(linked);
    }

    [TestMethod]
    public void Cancellation_does_not_become_a_successful_noop() => Assert.ThrowsExactly<OperationCanceledException>(() =>
        ArtifactCleanupAbsence.Probe(root, Path.Combine(root, "gone.bin"), new CancellationToken(true)));
}
