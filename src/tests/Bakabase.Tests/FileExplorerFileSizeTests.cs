using Bakabase.InsideWorld.Business.Components.FileExplorer;
using Bakabase.InsideWorld.Business.Components.FileExplorer.Information;

namespace Bakabase.Tests;

[TestClass]
public sealed class FileExplorerFileSizeTests
{
    private string root = null!;
    [TestInitialize] public void Setup() => Directory.CreateDirectory(root = Path.Combine(Path.GetTempPath(), "explorer-size-" + Guid.NewGuid().ToString("N")));
    [TestCleanup] public void Cleanup() => Directory.Delete(root, true);

    [DataTestMethod]
    [DataRow(IwFsType.Unknown, ".bin")]
    [DataRow(IwFsType.CompressedFileEntry, ".zip")]
    [DataRow(IwFsType.Image, ".png")]
    [DataRow(IwFsType.Video, ".mp4")]
    [DataRow(IwFsType.Audio, ".mp3")]
    public void Listing_and_lazy_metadata_keep_zero_and_changed_file_lengths(IwFsType type, string suffix)
    {
        var path = Path.Combine(root, "file" + suffix);
        File.WriteAllBytes(path, []);
        Assert.AreEqual(0L, new IwFsEntry(path, type).Size);
        Assert.AreEqual(0L, new IwFsEntryLazyInfo(path, type).Size);
        File.WriteAllBytes(path, [1, 2, 3]);
        Assert.AreEqual(3L, new IwFsEntry(path, type).Size);
        Assert.AreEqual(3L, new IwFsEntryLazyInfo(path, type).Size);
        File.Delete(path);
        Assert.IsNull(new IwFsEntry(path, type).Size);
        Assert.IsNull(new IwFsEntryLazyInfo(path, type).Size);
    }

    [TestMethod]
    public void Directory_is_not_recursively_sized_and_symlink_is_not_followed()
    {
        File.WriteAllBytes(Path.Combine(root, "payload"), [1, 2, 3]);
        Assert.IsNull(new IwFsEntry(root, IwFsType.Directory).Size);
        Assert.IsNull(new IwFsEntryLazyInfo(root, IwFsType.Directory).Size);
        var link = Path.Combine(root, "link.zip");
        try { File.CreateSymbolicLink(link, Path.Combine(root, "payload")); }
        catch (UnauthorizedAccessException) { Assert.Inconclusive("Symlink creation is not available."); }
        Assert.IsNull(new IwFsEntry(link, IwFsType.CompressedFileEntry).Size);
        Assert.IsNull(new IwFsEntryLazyInfo(link, IwFsType.CompressedFileEntry).Size);
    }
}
