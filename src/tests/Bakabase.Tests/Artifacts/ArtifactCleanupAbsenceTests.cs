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
            observe: ArtifactCleanupAbsence.OpenFileForObservation));
        Assert.AreEqual("held by a client", await File.ReadAllTextAsync(held));

        await File.WriteAllTextAsync(file, "new content");
        Assert.AreEqual(ArtifactCleanupPathPresence.Present,
            ArtifactCleanupAbsence.Probe(root, file, ArtifactCleanupAbsence.OpenFileForObservation));
        // An open failure with the name still in a readable directory is inconclusive.
        Assert.AreEqual(ArtifactCleanupPathPresence.Unavailable,
            ArtifactCleanupAbsence.Probe(root, file, _ => throw new FileNotFoundException()));
        Assert.IsFalse(ArtifactCleanupAbsence.AllConfirmedAbsent(root, [file],
            observe: ArtifactCleanupAbsence.OpenFileForObservation));
        Assert.AreEqual("new content", await File.ReadAllTextAsync(file));
    }

    [TestMethod]
    public async Task Private_selection_confirms_reported_names_without_opening_listed_payloads_or_claiming_absence()
    {
        var directory = Path.Combine(root, "inputs");
        Directory.CreateDirectory(directory);
        var stale = Path.Combine(directory, "archive.7z");
        var marker = Path.Combine(directory, ".smbdeleteAAA59fc4.4");
        var real = Path.Combine(directory, "second.7z");
        await File.WriteAllTextAsync(marker, "held by the NAS");
        await File.WriteAllTextAsync(real, "visible data");
        await using (var locked = new FileStream(real, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            // Both names were reported present by stat, but the server namespace lists only one.
            CollectionAssert.AreEqual(new[] {real},
                ArtifactCleanupAbsence.ConfirmReportedFiles(root, [stale, real], default).ToArray());
        }
        Assert.AreEqual("held by the NAS", await File.ReadAllTextAsync(marker));
        var offline = Path.Combine(root, "detached");
        Assert.ThrowsExactly<IOException>(() =>
            ArtifactCleanupAbsence.ConfirmReportedFiles(offline, [Path.Combine(offline, "archive.7z")], default));
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
    public void Thousands_of_removed_siblings_need_two_listings_per_accessible_parent()
    {
        var first = Directory.CreateDirectory(Path.Combine(root, "first")).FullName;
        var second = Directory.CreateDirectory(Path.Combine(root, "second")).FullName;
        var files = new List<string>();
        for (var i = 0; i < 3000; i++)
        {
            var parent = i % 2 == 0 ? first : second;
            File.WriteAllText(Path.Combine(parent, $".smbdeleteAAA{i:X4}4.4"), "");
            files.Add(Path.Combine(parent, $"gone-{i}.bin"));
        }
        // Both already-removed branches use the same living ancestor as evidence.
        files.Add(Path.Combine(root, "removed", "branch", "lost.bin"));
        files.Add(Path.Combine(root, "removed", "second-branch", "lost.bin"));
        var scans = new Dictionary<string, int>();
        IEnumerable<string> ListDirectory(string directory)
        {
            scans[directory] = scans.GetValueOrDefault(directory) + 1;
            return Directory.EnumerateFileSystemEntries(directory);
        }

        var evidence = ArtifactCleanupAbsence.ConfirmAbsentFiles(root, files, true,
            ArtifactCleanupAbsence.OpenFileForObservation, default, ListDirectory);
        Assert.IsNotNull(evidence);
        CollectionAssert.AreEquivalent(files.ToArray(), evidence.ToArray());
        Assert.AreEqual(3, scans.Count);
        foreach (var path in new[] {root, first, second}) Assert.AreEqual(2, scans[path]);
        Assert.AreEqual(1500, Directory.EnumerateFiles(first).Count());
    }

    [DataTestMethod]
    [DataRow("permission")]
    [DataRow("offline")]
    [DataRow("reappearing-file")]
    [DataRow("reappearing-directory")]
    public void Second_batch_pass_refuses_changed_names_and_unreadable_directories(string change)
    {
        var parent = Directory.CreateDirectory(Path.Combine(root, "nested")).FullName;
        var path = Path.Combine(parent, "late.bin");
        var scans = 0;
        IEnumerable<string> ListDirectory(string directory)
        {
            if (directory == parent && ++scans == 2)
            {
                if (change == "permission") throw new UnauthorizedAccessException();
                if (change == "offline") throw new IOException();
                if (change == "reappearing-file") File.WriteAllText(path, "new");
                if (change == "reappearing-directory") Directory.CreateDirectory(path);
            }
            return Directory.EnumerateFileSystemEntries(directory);
        }
        // Even an exact-name lookup returning stale ENOENT cannot override fresh directory evidence.
        Assert.IsNull(ArtifactCleanupAbsence.ConfirmAbsentFiles(root, [path], true,
            _ => throw new FileNotFoundException(), default, ListDirectory));
        Assert.AreEqual(2, scans);
    }

    [TestMethod]
    public void Fresh_listings_apply_the_artifact_name_comparer_even_when_the_exact_lookup_is_stale()
    {
        var selected = Path.Combine(root, "listed.BIN");
        File.WriteAllText(Path.Combine(root, "listed.bin"), "preserve");
        var absent = ArtifactCleanupAbsence.AllConfirmedAbsent(root, [selected],
            observe: _ => throw new FileNotFoundException());
        Assert.AreEqual(!Bakabase.InsideWorld.Business.Components.Artifacts.ArtifactManifestFiles.PathComparer
            .Equals("listed.BIN", "listed.bin"), absent);
    }

    [TestMethod]
    public void A_partial_batch_only_proves_the_absent_subset_and_never_accepts_a_link_or_detached_root()
    {
        var gone = Path.Combine(root, "gone.bin");
        var live = Path.Combine(root, "live.bin");
        File.WriteAllText(live, "still present");
        var evidence = ArtifactCleanupAbsence.ConfirmAbsentFiles(root, [gone, live], false,
            ArtifactCleanupAbsence.OpenFileForObservation, default);
        Assert.IsNotNull(evidence);
        CollectionAssert.AreEqual(new[] {gone}, evidence.ToArray());
        Assert.IsFalse(ArtifactCleanupAbsence.AllConfirmedAbsent(root, [gone, live]));
        Assert.IsFalse(ArtifactCleanupAbsence.AllConfirmedAbsent(root, [Path.Combine(root, "..", "outside.bin")]));
        Assert.ThrowsExactly<OperationCanceledException>(() => ArtifactCleanupAbsence.AllConfirmedAbsent(root, [gone],
            new CancellationToken(true)));
        if (!OperatingSystem.IsWindows())
        {
            var link = Path.Combine(root, "linked-parent");
            Directory.CreateSymbolicLink(link, root);
            Assert.IsFalse(ArtifactCleanupAbsence.AllConfirmedAbsent(root, [Path.Combine(link, "gone.bin")]));
            Directory.Delete(link);
        }
        File.Delete(live);
        Directory.Delete(root);
        Assert.IsFalse(ArtifactCleanupAbsence.AllConfirmedAbsent(root, [gone]));
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
