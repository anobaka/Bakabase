using System.Text.Json;
using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.Abstractions.Components.Network;
using Bakabase.InsideWorld.Models.Configs;
using Bakabase.Service.Components.CloudDownloads.Rclone;
using Bakabase.TestKit.Implementations;

namespace Bakabase.Tests.CloudDownloads;

public sealed partial class RcloneRemoteEngineTests
{
    [TestMethod]
    public async Task PinnedOldStageUsesItsOwnReserveAndVerifiedDeliveryRemainsReusable()
    {
        var profile = new RcloneProfileRef("pikpak", _account);
        var files = await _engine.ListAsync(profile, "folder", default);
        var target = Path.Combine(_root, "destination");
        await File.WriteAllTextAsync(Path.Combine(_profile, "mode"), "failure");
        await Assert.ThrowsExactlyAsync<RcloneRemoteException>(() => _engine.CopyAsync(profile, "folder", target, files, null, default));
        var locator = Directory.GetFiles(_profile, "workspace.json", SearchOption.AllDirectories).Single();
        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(locator));
        var pinned = json.RootElement.GetProperty("directory").GetString()!;
        var preference = Path.Combine(_root, "new-preference"); Directory.CreateDirectory(preference);
        long stageFree = 1;
        var guard = new StorageWriteGuard(new StoragePolicy(), path =>
            ("disk", path.StartsWith(Path.GetDirectoryName(pinned)!, StringComparison.Ordinal) ? Volatile.Read(ref stageFree) : 10000),
            TimeSpan.FromMilliseconds(10));
        var engine = new RcloneRemoteEngine(Path.Combine(_root, "private"), _ => Task.FromResult(Path.Combine(_root, "fake-rclone")),
            new BakabaseWebProxy(new TestBOptionsManager<NetworkOptions>(_network)), writes: guard);
        await File.WriteAllTextAsync(Path.Combine(_profile, "mode"), "normal");
        using var scope = StorageWriteScope.Enter(new("workflow", "17"));
        var failure = await Assert.ThrowsExactlyAsync<LocalStorageException>(() => engine.CopyAsync(profile, "folder", target,
            files, null, default, workspaceDirectory: preference));
        Assert.AreEqual(StorageWriteGuard.ReserveLowCode, failure.Code);
        Assert.AreEqual(1, File.ReadAllLines(Path.Combine(_profile, "copies.log")).Length, "No download may start on the full pinned volume.");
        Interlocked.Exchange(ref stageFree, 10000);
        var completed = await engine.CopyAsync(profile, "folder", target, files, null, default, workspaceDirectory: preference);
        Assert.AreEqual("abc", await File.ReadAllTextAsync(completed.Files.Single().Path));
        Interlocked.Exchange(ref stageFree, 1);
        Assert.IsTrue((await engine.CopyAsync(profile, "folder", target, files, null, default, workspaceDirectory: preference)).Reused);
        Assert.AreEqual(2, File.ReadAllLines(Path.Combine(_profile, "copies.log")).Length);
    }

    private sealed class StoragePolicy : IStorageWritePolicyProvider
    { public Task<long> GetMinimumFreeBytesAsync(StorageWriteContext? context, CancellationToken ct = default) => Task.FromResult(context == null ? 0L : 100L); }
}
