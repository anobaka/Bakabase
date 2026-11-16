using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain;
using Bakabase.Modules.Downloader.Cloud;
using Bakabase.Service.Components.CloudDownloads;

namespace Bakabase.Tests;

[TestClass]
public sealed class AcquisitionStageDownloadDirectoryTests
{
    private string root = null!;
    [TestInitialize] public void Setup() {root = Path.Combine(Path.GetTempPath(), "stage-download-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);}
    [TestCleanup] public void Cleanup() {if (Directory.Exists(root)) Directory.Delete(root, true);}
    private CloudDownloadTarget Target(string directory, string? binding = null) => new()
    {
        Id = "part", SelectedSourceId = "source",
        Sources = [new CloudSource {Id = "source", Url = "https://example.test/a.zip", ProviderId = "http"}],
        StageDirectories = new() {Download = new(directory, "borrowed", "userDirectoryRevision") {BindingId = binding}}
    };
    [TestMethod]
    public async Task ExplicitDownloadUsesExactDirectoryWithoutGeneratedWrapper()
    {
        var target = Target(Path.Combine(root, "selected"), Guid.NewGuid().ToString("N"));
        var actual = await CloudStageDirectoryPath.PrepareDownloadAsync(target, new Storage(), null, default);
        Assert.AreEqual(target.StageDirectories!.Download!.Directory, actual); Assert.IsTrue(Directory.Exists(actual));
        Assert.AreEqual(0, Directory.GetFileSystemEntries(actual).Length);
    }
    [TestMethod]
    public void ExplicitIntentIncludesSelectedPathWithoutChangingSourceIdentity()
    {
        var first = Target(Path.Combine(root, "one"), Guid.NewGuid().ToString("N"));
        var second = Target(Path.Combine(root, "two"), Guid.NewGuid().ToString("N"));
        Assert.AreEqual(DownloadArtifactCoordinator.Source(first), DownloadArtifactCoordinator.Source(second));
        Assert.AreNotEqual(DownloadArtifactCoordinator.DownloadIntent(first, root), DownloadArtifactCoordinator.DownloadIntent(second, root));
        Assert.AreEqual(DownloadArtifactCoordinator.DownloadIntent(first, root), DownloadArtifactCoordinator.DownloadIntent(first, Path.Combine(root, "unavailable-old")));
    }
    [TestMethod]
    public void HistoricalUnboundDirectoryDoesNotOverrideDownloadIdentity()
    {
        var target = Target(Path.Combine(root, "old"));
        Assert.IsNull(CloudStageDirectoryPath.Download(target));
        Assert.AreEqual(DownloadArtifactCoordinator.DownloadIntent(target, root),
            DownloadArtifactCoordinator.DownloadIntent(target with {StageDirectories = null}, root));
    }
    [TestMethod]
    public void InvalidBindingCannotSelectDownloadPath() => Assert.ThrowsExactly<InvalidOperationException>(() =>
        CloudStageDirectoryPath.Download(Target(root, "not-server-binding")));
    private sealed class Storage : IUserStoragePolicy
    {
        public bool IsRestricted => false;
        public IReadOnlyList<UserStorageRoot> GetRoots(UserStoragePurpose purpose = UserStoragePurpose.UserFiles) => [];
        public bool IsPathAllowed(string path, UserStoragePurpose purpose = UserStoragePurpose.UserFiles) => true;
        public void EnsurePathAllowed(string path, UserStoragePurpose purpose = UserStoragePurpose.UserFiles) { }
        public void EnsureTreeMutationAllowed(string path) { }
    }
}
