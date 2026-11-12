using Bakabase.Service.Components.CloudDownloads.Rclone;

namespace Bakabase.Tests.CloudDownloads;

[TestClass]
public sealed class RcloneDownloadSnapshotStoreTests
{
    private string _root = null!;
    private string _version = null!;
    [TestInitialize]
    public void Setup()
    {
        _root = Path.Combine(Path.GetTempPath(), "rclone-snapshot-" + Guid.NewGuid().ToString("N"));
        _version = Guid.NewGuid().ToString("N");
    }
    [TestCleanup]
    public void Cleanup() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }

    [TestMethod]
    public async Task ListingIsFrozenAcrossRestartAndKeptPrivate()
    {
        var calls = 0;
        Task<IReadOnlyList<RcloneRemoteFile>> Create(CancellationToken _) {calls++; return Task.FromResult<IReadOnlyList<RcloneRemoteFile>>([new("a.txt", 3)]);}
        var first = await new RcloneDownloadSnapshotStore(_root).GetOrCreateAsync("pixeldrain", "pixeldrain.rclone", "operation", "identity", _version, Create, default);
        var next = await new RcloneDownloadSnapshotStore(_root).GetOrCreateAsync("pixeldrain", "pixeldrain.rclone", "operation", "identity", _version, Create, default);
        Assert.AreEqual(1, calls);
        Assert.AreEqual(first.Files.Single(), next.Files.Single());
        var file = Directory.GetFiles(_root, "manifest.json", SearchOption.AllDirectories).Single();
        if (!OperatingSystem.IsWindows()) Assert.AreEqual((UnixFileMode)0x180, File.GetUnixFileMode(file));
    }

    [DataTestMethod]
    [DataRow(true, "RCLONE_OPERATION_CHANGED")]
    [DataRow(false, "RCLONE_ACCOUNT_CHANGED")]
    public async Task ExistingOperationCannotChangeSourceOutputOrAccountGeneration(bool changedIdentity, string code)
    {
        Task<IReadOnlyList<RcloneRemoteFile>> Create(CancellationToken _) => Task.FromResult<IReadOnlyList<RcloneRemoteFile>>([new("a.txt", 3)]);
        var store = new RcloneDownloadSnapshotStore(_root);
        await store.GetOrCreateAsync("pixeldrain", "pixeldrain.rclone", "operation", "identity", _version, Create, default);
        var error = await Assert.ThrowsExactlyAsync<RcloneRemoteException>(() => store.GetOrCreateAsync("pixeldrain", "pixeldrain.rclone",
            "operation", changedIdentity ? "other-output" : "identity", changedIdentity ? _version : Guid.NewGuid().ToString("N"),
            _ => throw new AssertFailedException("A saved operation must not discover a different listing."), default));
        Assert.AreEqual(code, error.Code);
    }

    [TestMethod]
    public async Task SameOperationConcurrentRequestsOnlyEnumerateOnce()
    {
        var calls = 0;
        async Task<IReadOnlyList<RcloneRemoteFile>> Create(CancellationToken _) {Interlocked.Increment(ref calls); await Task.Yield(); return [new("a.txt", 3)];}
        var store = new RcloneDownloadSnapshotStore(_root);
        await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => store.GetOrCreateAsync("pikpak", "pikpak.rclone",
            "operation", "identity", _version, Create, default)));
        Assert.AreEqual(1, calls);
    }

    [TestMethod]
    public async Task EmptyDiscoveryDoesNotPublishASuccessSnapshot()
    {
        var store = new RcloneDownloadSnapshotStore(_root);
        var error = await Assert.ThrowsExactlyAsync<RcloneRemoteException>(() => store.GetOrCreateAsync("pixeldrain", "pixeldrain.rclone",
            "operation", "identity", _version, _ => Task.FromResult<IReadOnlyList<RcloneRemoteFile>>([]), default));
        Assert.AreEqual("RCLONE_EMPTY_REMOTE", error.Code);
        Assert.IsFalse(Directory.Exists(_root));
    }
}
