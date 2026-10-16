using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Service.Components.Federation;
using Bakabase.Service.Controllers;

namespace Bakabase.Tests.Federation;

[TestClass]
public sealed class ManagedLibraryBrowserFilesTests
{
    private string _root = null!;
    [TestInitialize]
    public void Setup() { _root = Path.Combine(Path.GetTempPath(), "bakabase-managed-files-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(_root); }
    [TestCleanup]
    public void Cleanup() { Directory.Delete(_root, true); }
    private Resource Folder => new() {Id = 42, Path = _root};
    private string FileAt(string name) { var path = Path.Combine(_root, name); Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, "test"); return path; }

    [TestMethod]
    public void Unconfigured_resources_can_browse_supported_files_without_native_candidates()
    {
        FileAt("cover.png"); FileAt("movie.mp4"); FileAt("game.exe"); FileAt("notes.txt");
        var result = ManagedLibraryBrowserFiles.Read(Folder, null, 0, 100, default);
        Assert.AreEqual(3, result.Items.Count);
        Assert.IsFalse(result.PlayableFilesConfigured);
        Assert.IsFalse(result.HasMore);
        Assert.IsTrue(result.Items.All(x => !x.Key.EndsWith(".exe")));
    }

    [TestMethod]
    public void Configured_zero_matches_do_not_fall_back_to_other_files()
    {
        FileAt("cover.png");
        var options = new ResourceProfilePlayableFileOptions {Extensions = ["mp4"]};
        var page = ManagedLibraryBrowserFiles.Read(Folder, options, 0, 100, default);
        Assert.AreEqual(0, page.Items.Count);
        Assert.IsTrue(page.PlayableFilesConfigured);
        Assert.IsFalse(ManagedLibraryBrowserFiles.CanRead(Folder, options, Path.Combine(_root, "cover.png")));
        Assert.IsFalse(ManagedLibraryBrowserFiles.MayUseAlternateProvider(Folder, Path.Combine(_root, "cover.png")));
    }

    [TestMethod]
    public void All_file_browsing_is_explicit_and_initial_file_selects_a_bounded_page()
    {
        for (var i = 0; i < 205; i++) FileAt($"{i:000}.png");
        var options = new ResourceProfilePlayableFileOptions {Extensions = ["mp4"]};
        Assert.AreEqual(0, ManagedLibraryBrowserFiles.Read(Folder, options, 0, 100, default).Items.Count);
        var first = ManagedLibraryBrowserFiles.Read(Folder, options, 0, 100, default, allFiles: true);
        Assert.AreEqual(100, first.Items.Count);
        Assert.IsTrue(first.PlayableFilesConfigured);
        var remaining = ManagedLibraryBrowserFiles.Read(Folder, options, first.NextOffset!.Value, 100, default, allFiles: true);
        var target = remaining.Items[50].Key;
        var selected = ManagedLibraryBrowserFiles.Read(Folder, options, 0, 100, default, allFiles: true, initialPath: target);
        Assert.AreEqual(100, selected.Offset);
        Assert.IsTrue(selected.Items.Any(item => item.Key == target));
        // Merely listing all files does not loosen the configured playback endpoint.
        Assert.IsFalse(ManagedLibraryBrowserFiles.CanRead(Folder, options, target));
    }

    [TestMethod]
    public void Configured_extensions_and_filename_rules_apply_to_listing_and_delivery()
    {
        var accepted = FileAt("episode-01.mp4");
        var denied = FileAt("extra.mp4"); FileAt("episode-02.png");
        var options = new ResourceProfilePlayableFileOptions {Extensions = [".mp4"], FileNamePattern = "^episode-"};
        var page = ManagedLibraryBrowserFiles.Read(Folder, options, 0, 100, default);
        Assert.AreEqual(1, page.Items.Count);
        Assert.IsTrue(ManagedLibraryBrowserFiles.CanRead(Folder, options, accepted));
        Assert.IsFalse(ManagedLibraryBrowserFiles.CanRead(Folder, options, denied));
    }

    [TestMethod]
    public void Large_folders_return_bounded_pages_and_no_duplicate_entries()
    {
        for (var i = 0; i < 205; i++) FileAt($"{i:000}.png");
        var first = ManagedLibraryBrowserFiles.Read(Folder, null, 0, 100, default);
        var second = ManagedLibraryBrowserFiles.Read(Folder, null, first.NextOffset!.Value, 100, default);
        var last = ManagedLibraryBrowserFiles.Read(Folder, null, second.NextOffset!.Value, 100, default);
        Assert.AreEqual(100, first.Items.Count); Assert.AreEqual(100, second.Items.Count); Assert.AreEqual(5, last.Items.Count);
        Assert.IsTrue(first.HasMore); Assert.IsTrue(second.HasMore); Assert.IsFalse(last.HasMore);
        Assert.AreEqual(205, first.Items.Concat(second.Items).Concat(last.Items).Select(x => x.Key).Distinct().Count());
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => ManagedLibraryBrowserFiles.Read(Folder, null, 0, 101, default));
    }

    [TestMethod]
    public void Delivery_cannot_escape_resource_or_use_prefix_siblings()
    {
        var allowed = FileAt("nested/image.png");
        var outside = Path.Combine(Path.GetTempPath(), "outside.png");
        Assert.IsTrue(ManagedLibraryBrowserFiles.CanRead(Folder, null, allowed));
        Assert.IsTrue(ManagedLibraryBrowserFiles.CanRead(Folder with {Path = _root + Path.DirectorySeparatorChar}, null, allowed));
        Assert.IsFalse(ManagedLibraryBrowserFiles.CanRead(Folder, null, outside));
        Assert.IsFalse(ManagedLibraryBrowserFiles.CanRead(Folder, null, Path.Combine(_root, "..", "outside.png")));
        Assert.IsFalse(ManagedLibraryBrowserFiles.CanRead(Folder, null, _root + "-other/image.png"));
        Assert.IsFalse(ManagedLibraryBrowserFiles.CanRead(Folder, null, "nested/image.png"));
        Assert.IsFalse(ManagedLibraryBrowserFiles.CanRead(Folder, null, FileAt("game.exe")));
    }

    [TestMethod]
    public void Single_file_resource_does_not_authorize_its_siblings()
    {
        var file = FileAt("one.png"); var sibling = FileAt("two.png");
        var resource = new Resource {Id = 1, IsFile = true, Path = file};
        Assert.AreEqual(1, ManagedLibraryBrowserFiles.Read(resource, null, 0, 100, default).Items.Count);
        Assert.IsTrue(ManagedLibraryBrowserFiles.CanRead(resource, null, file));
        Assert.IsFalse(ManagedLibraryBrowserFiles.CanRead(resource, null, sibling));
    }

    [TestMethod]
    public void Missing_resource_and_cancellation_are_not_reported_as_empty_success()
    {
        Assert.ThrowsExactly<FileNotFoundException>(() => ManagedLibraryBrowserFiles.Read(
            new Resource {Path = Path.Combine(_root, "gone.png")}, null, 0, 100, default));
        FileAt("one.png");
        Assert.ThrowsExactly<OperationCanceledException>(() => ManagedLibraryBrowserFiles.Read(Folder, null, 0, 100, new CancellationToken(true)));
    }

    [TestMethod]
    public void Child_symlinks_are_not_a_path_around_resource_membership()
    {
        if (OperatingSystem.IsWindows()) Assert.Inconclusive("Creating symlinks requires an optional Windows privilege.");
        var outside = Path.Combine(Path.GetTempPath(), "bakabase-outside-" + Guid.NewGuid().ToString("N") + ".png");
        try
        {
            File.WriteAllText(outside, "outside");
            var link = Path.Combine(_root, "linked.png");
            File.CreateSymbolicLink(link, outside);
            Assert.AreEqual(0, ManagedLibraryBrowserFiles.Read(Folder, null, 0, 100, default).Items.Count);
            Assert.IsFalse(ManagedLibraryBrowserFiles.CanRead(Folder, null, link));
        }
        finally { File.Delete(outside); }
    }

    [TestMethod]
    public void Resource_details_can_list_only_this_resource_and_its_real_descendants()
    {
        var file = FileAt("nested/page.png");
        Assert.IsTrue(ManagedLibraryResourceFiles.IsWithin(Folder, _root));
        Assert.IsTrue(ManagedLibraryResourceFiles.IsWithin(Folder, Path.GetDirectoryName(file)));
        Assert.IsTrue(ManagedLibraryResourceFiles.IsWithin(Folder, file));
        Assert.IsFalse(ManagedLibraryResourceFiles.IsWithin(Folder, Path.GetDirectoryName(_root)));
        Assert.IsFalse(ManagedLibraryResourceFiles.IsWithin(Folder, _root + "-sibling"));
        Assert.IsFalse(ManagedLibraryResourceFiles.IsWithin(Folder, "nested"));
        Assert.IsFalse(ManagedLibraryResourceFiles.IsWithin(new Resource {Path = file}, _root));
        if (!OperatingSystem.IsWindows())
        {
            var link = Path.Combine(_root, "escape");
            Directory.CreateSymbolicLink(link, Path.GetDirectoryName(_root)!);
            Assert.IsFalse(ManagedLibraryResourceFiles.IsWithin(Folder, link));
        }
    }

    [TestMethod]
    public void Resource_children_are_sorted_bounded_and_exclude_hidden_files_and_links()
    {
        foreach (var i in Enumerable.Range(0, 205).Reverse()) FileAt($"entry-{i:000}.txt");
        FileAt(".hidden.txt");
        if (!OperatingSystem.IsWindows()) File.CreateSymbolicLink(Path.Combine(_root, "escape.txt"), Path.Combine(_root, "entry-000.txt"));
        var paths = new List<string>();
        string? cursor = null;
        do
        {
            var page = ManagedLibraryResourceFiles.ReadChildren(Folder, _root, false, cursor, 100, _ => true, default);
            Assert.IsTrue(page.Entries.Count <= 100);
            paths.AddRange(page.Entries.Select(e => e.Name));
            cursor = page.NextCursor;
            Assert.AreEqual(page.HasMore, cursor != null);
        } while (cursor != null);
        CollectionAssert.AreEqual(Enumerable.Range(0, 205).Select(i => $"entry-{i:000}.txt").ToArray(), paths);
        Assert.ThrowsExactly<ArgumentException>(() => ManagedLibraryResourceFiles.ReadChildren(Folder, _root, false, "../escape", 100, _ => true, default));
        Assert.ThrowsExactly<ArgumentException>(() => ManagedLibraryResourceFiles.ReadChildren(Folder, _root, false, null, 101, _ => true, default));
        Assert.ThrowsExactly<UnauthorizedAccessException>(() => ManagedLibraryResourceFiles.ReadChildren(Folder, Path.GetDirectoryName(_root)!, false, null, 100, _ => true, default));
        Assert.ThrowsExactly<OperationCanceledException>(() => ManagedLibraryResourceFiles.ReadChildren(Folder, _root, false, null, 100, _ => true, new(true)));
    }

    [TestMethod]
    public void Alternate_owner_providers_and_archive_entries_need_exact_discovered_identity()
    {
        var candidate = new PlayableItem {Origin = DataOrigin.Manual, Key = "/downloads/album.zip!page.jpg"};
        Assert.IsTrue(ManagedLibraryBrowserFiles.MayUseAlternateProvider(Folder, candidate.Key));
        Assert.IsTrue(ManagedLibraryBrowserFilesController.IsDiscoveredBrowserFile([candidate], candidate.Origin, candidate.Key));
        Assert.IsFalse(ManagedLibraryBrowserFilesController.IsDiscoveredBrowserFile([candidate], DataOrigin.FileSystem, candidate.Key));
        Assert.IsFalse(ManagedLibraryBrowserFilesController.IsDiscoveredBrowserFile([candidate], candidate.Origin, "/downloads/private.jpg"));
        Assert.IsFalse(ManagedLibraryBrowserFilesController.IsDiscoveredBrowserFile([candidate], candidate.Origin, "/downloads/start.exe"));
    }

    [TestMethod]
    public void Only_explicit_unconfigured_real_resource_files_qualify_for_native_fallback()
    {
        var file = FileAt("page.png");
        Assert.IsTrue(ManagedLibraryBrowserFiles.CanExplicitlyOpenUnconfiguredFile(Folder, null, DataOrigin.FileSystem, file));
        Assert.IsTrue(ManagedLibraryBrowserFiles.CanExplicitlyOpenUnconfiguredFile(Folder, null, DataOrigin.Manual, file));
        Assert.IsFalse(ManagedLibraryBrowserFiles.CanExplicitlyOpenUnconfiguredFile(Folder, null, DataOrigin.Steam, file));
        Assert.IsFalse(ManagedLibraryBrowserFiles.CanExplicitlyOpenUnconfiguredFile(Folder, null, DataOrigin.FileSystem, _root));
        Assert.IsFalse(ManagedLibraryBrowserFiles.CanExplicitlyOpenUnconfiguredFile(Folder, null, DataOrigin.FileSystem, FileAt("game.exe")));
        Assert.IsFalse(ManagedLibraryBrowserFiles.CanExplicitlyOpenUnconfiguredFile(Folder,
            new ResourceProfilePlayableFileOptions {Extensions = ["mp4"]}, DataOrigin.FileSystem, file));
    }
}
