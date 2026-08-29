using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Modules.RemoteAccess.Abstractions.Components;
using Bakabase.Modules.RemoteAccess.Components;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Tests.RemoteAccess;

[TestClass]
public class MediaPathGuardTests
{
    private sealed class StubRootProvider(params string[] roots) : IServableRootProvider
    {
        public int CallCount { get; private set; }

        public Task<IReadOnlyCollection<string>> GetRootsAsync(CancellationToken ct = default)
        {
            CallCount++;
            return Task.FromResult<IReadOnlyCollection<string>>(roots);
        }
    }

    private sealed class ThrowingRootProvider : IServableRootProvider
    {
        public Task<IReadOnlyCollection<string>> GetRootsAsync(CancellationToken ct = default) =>
            throw new InvalidOperationException("database is unavailable");
    }

    private static string Rooted(params string[] segments) =>
        Path.Combine([Path.GetPathRoot(Path.GetTempPath())!, .. segments]);

    private static MediaPathGuard Build(params IServableRootProvider[] providers) =>
        new(providers, NullLogger<MediaPathGuard>.Instance);

    [TestMethod]
    public async Task Serves_FileInsideARoot()
    {
        var guard = Build(new StubRootProvider(Rooted("media", "library")));

        Assert.IsTrue(await guard.IsServableAsync(Rooted("media", "library", "anime", "ep1.mkv")));
    }

    [TestMethod]
    public async Task Refuses_FileOutsideEveryRoot()
    {
        var guard = Build(new StubRootProvider(Rooted("media", "library")));

        Assert.IsFalse(await guard.IsServableAsync(Rooted("home", "user", "passwords.txt")));
    }

    [TestMethod]
    public async Task Refuses_TraversalOutOfARoot()
    {
        // This is the shape an attacker actually sends: a path that starts inside a
        // root and climbs out with '..'.
        var guard = Build(new StubRootProvider(Rooted("media", "library")));

        Assert.IsFalse(await guard.IsServableAsync(
            Rooted("media", "library", "..", "..", "home", "user", "passwords.txt")));
    }

    [TestMethod]
    public async Task Refuses_SiblingDirectoryWithSharedPrefix()
    {
        var guard = Build(new StubRootProvider(Rooted("media", "library")));

        Assert.IsFalse(await guard.IsServableAsync(Rooted("media", "library-private", "tax.pdf")));
    }

    [TestMethod]
    public async Task Serves_ArchiveEntry_WhenTheArchiveIsInsideARoot()
    {
        var guard = Build(new StubRootProvider(Rooted("media", "library")));
        var entry = Rooted("media", "library", "book.zip") + "!chapter1/page01.jpg";

        Assert.IsTrue(await guard.IsServableAsync(entry));
    }

    [TestMethod]
    public async Task Refuses_ArchiveEntry_WhenTheArchiveIsOutsideEveryRoot()
    {
        var guard = Build(new StubRootProvider(Rooted("media", "library")));
        var entry = Rooted("home", "user", "backup.zip") + "!secrets/keys.txt";

        Assert.IsFalse(await guard.IsServableAsync(entry));
    }

    [TestMethod]
    public async Task Refuses_Everything_WhenThereAreNoRoots()
    {
        var guard = Build(new StubRootProvider());

        Assert.IsFalse(await guard.IsServableAsync(Rooted("media", "library", "ep1.mkv")));
    }

    [TestMethod]
    public async Task Refuses_UnusablePaths()
    {
        var guard = Build(new StubRootProvider(Rooted("media", "library")));

        Assert.IsFalse(await guard.IsServableAsync(null));
        Assert.IsFalse(await guard.IsServableAsync(""));
        Assert.IsFalse(await guard.IsServableAsync("relative/path.mkv"));
    }

    [TestMethod]
    public async Task Combines_RootsFromEveryProvider()
    {
        var guard = Build(
            new StubRootProvider(Rooted("media", "library")),
            new StubRootProvider(Rooted("srv", "appdata", "data")));

        Assert.IsTrue(await guard.IsServableAsync(Rooted("media", "library", "ep1.mkv")));
        Assert.IsTrue(await guard.IsServableAsync(Rooted("srv", "appdata", "data", "covers", "1.jpg")));
    }

    [TestMethod]
    public async Task A_FailingProvider_DoesNotWidenOrBreakTheRootSet()
    {
        var guard = Build(new ThrowingRootProvider(), new StubRootProvider(Rooted("media", "library")));

        Assert.IsTrue(await guard.IsServableAsync(Rooted("media", "library", "ep1.mkv")));
        Assert.IsFalse(await guard.IsServableAsync(Rooted("home", "user", "passwords.txt")));
    }

    [TestMethod]
    public async Task Caches_Roots_BetweenChecks()
    {
        var provider = new StubRootProvider(Rooted("media", "library"));
        var guard = Build(provider);

        await guard.IsServableAsync(Rooted("media", "library", "a.mkv"));
        await guard.IsServableAsync(Rooted("media", "library", "b.mkv"));

        Assert.AreEqual(1, provider.CallCount);
    }

    [TestMethod]
    public async Task Invalidate_ForcesAReload()
    {
        var provider = new StubRootProvider(Rooted("media", "library"));
        var guard = Build(provider);

        await guard.IsServableAsync(Rooted("media", "library", "a.mkv"));
        guard.Invalidate();
        await guard.IsServableAsync(Rooted("media", "library", "a.mkv"));

        Assert.AreEqual(2, provider.CallCount);
    }

    [TestMethod]
    public async Task Concurrent_FirstChecks_LoadRootsOnce()
    {
        var provider = new StubRootProvider(Rooted("media", "library"));
        var guard = Build(provider);

        var checks = new Task<bool>[16];
        for (var i = 0; i < checks.Length; i++)
        {
            checks[i] = Task.Run(() => guard.IsServableAsync(Rooted("media", "library", "a.mkv")));
        }

        await Task.WhenAll(checks);

        CollectionAssert.DoesNotContain(Array.ConvertAll(checks, t => t.Result), false);
        Assert.AreEqual(1, provider.CallCount);
    }
}
