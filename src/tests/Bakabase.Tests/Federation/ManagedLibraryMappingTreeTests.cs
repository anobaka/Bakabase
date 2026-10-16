using System.Diagnostics;
using System.Text.Json;
using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.Abstractions.Models.Db;
using Bakabase.InsideWorld.Business;
using Bakabase.Modules.Federation.Identity;
using Bakabase.Modules.Federation.Contracts;
using Bakabase.Service.Components.Federation;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Tests.Federation;

[TestClass]
public sealed class ManagedLibraryMappingTreeTests
{
    [TestMethod]
    public async Task CountsOwnedDirectoryMetadataAndReturnsActualPrefixWithoutScanningFiles()
    {
        await using var f = await Fixture.CreateAsync([new("/Volumes/private-nas", "Media share", "mount", false)]);
        await f.AddAsync(("/Volumes/private-nas/Movies/A", false), ("/Volumes/private-nas/Movies/B", false),
            ("/Volumes/private-nas/Movies/A/cover.jpg", true), ("/Volumes/private-nas/image.jpg", true),
            (null, false));
        var roots = await f.ReadAsync();
        var root = roots.Nodes.Single();
        Assert.AreEqual(("private-nas", 4L, true), (root.Name, root.ResourceCount, root.HasChildren));
        Assert.AreEqual("/Volumes/private-nas", root.ServerPath);
        var children = await f.ReadAsync(root.ServerPath);
        Assert.AreEqual(("Movies", 3L, true), (children.Nodes.Single().Name, children.Nodes.Single().ResourceCount, children.Nodes.Single().HasChildren));
        var works = await f.ReadAsync(children.Nodes.Single().ServerPath);
        CollectionAssert.AreEqual(new[] { "A", "B" }, works.Nodes.Select(x => x.Name).ToArray());
        CollectionAssert.AreEqual(new long[] { 2, 1 }, works.Nodes.Select(x => x.ResourceCount).ToArray());
        Assert.IsFalse(works.Nodes.Any(x => x.HasChildren), "A contained file is not a directory metadata child.");
        Assert.AreEqual(0, (await f.ReadAsync(works.Nodes[0].ServerPath)).Nodes.Length);
        Assert.AreEqual(0, f.Db.ChangeTracker.Entries().Count());
        Assert.AreEqual(0, f.Storage.Probes, "The tree only asks for storage roots, never permissions or NAS entries.");
    }

    [TestMethod]
    public async Task SeparatorBoundariesCaseAndUnicodeArePreserved()
    {
        await using var f = await Fixture.CreateAsync([new("/media", "Media", "mount", false)]);
        await f.AddAsync(("/media/A", false), ("/media/a", false), ("/media/😀_100%/child", false),
            ("/media-backup/unrelated", false));
        var roots = await f.ReadAsync();
        Assert.AreEqual(2, roots.Nodes.Length);
        var media = roots.Nodes.Single(x => x.Name == "media");
        Assert.AreEqual(3L, media.ResourceCount);
        var children = await f.ReadAsync(media.ServerPath);
        CollectionAssert.AreEquivalent(new[] { "A", "a", "😀_100%" }, children.Nodes.Select(x => x.Name).ToArray());
        var emoji = children.Nodes.Single(x => x.Name == "😀_100%");
        Assert.AreEqual("child", (await f.ReadAsync(emoji.ServerPath)).Nodes.Single().Name);
    }

    [TestMethod]
    public async Task WindowsDriveAndUncPathsUseTheirOwnCaseAndSlashSemantics()
    {
        await using var f = await Fixture.CreateAsync([]);
        await f.AddAsync((@"Y:\Movies\A\one.jpg", true), ("y:/movies/a/two.jpg", true),
            (@"Y:\Movies\B", false), (@"\\NAS\Share\Series\Work", false),
            ("//nas/share/series/work/file.mp4", true), ("Y:/root-file.jpg", true));
        var roots = await f.ReadAsync();
        Assert.AreEqual(2, roots.Nodes.Length);
        var drive = roots.Nodes.Single(x => x.Name.Equals("Y:", StringComparison.OrdinalIgnoreCase));
        Assert.AreEqual(4L, drive.ResourceCount);
        var movies = (await f.ReadAsync(drive.ServerPath)).Nodes.Single();
        Assert.IsTrue(movies.Name.Equals("Movies", StringComparison.OrdinalIgnoreCase));
        var works = await f.ReadAsync(movies.ServerPath);
        Assert.AreEqual(2, works.Nodes.Length);
        Assert.AreEqual(2L, works.Nodes.Single(x => x.Name.Equals("A", StringComparison.OrdinalIgnoreCase)).ResourceCount);
        var share = roots.Nodes.Single(x => x.Name.Equals("Share", StringComparison.OrdinalIgnoreCase));
        Assert.AreEqual(2L, share.ResourceCount);
        var series = (await f.ReadAsync(share.ServerPath)).Nodes.Single();
        Assert.AreEqual(2L, (await f.ReadAsync(series.ServerPath)).Nodes.Single().ResourceCount);
    }

    [TestMethod]
    public async Task KeysetPagesStayWithinTheirParentAndDoNotRepeatNodes()
    {
        await using var f = await Fixture.CreateAsync([new("/media", "Media", "mount", false)]);
        await f.SeedAsync(503, "printf('/media/work-%06d',x)");
        var root = (await f.ReadAsync()).Nodes.Single();
        var ids = new HashSet<string>();
        string? cursor = null;
        var pages = 0;
        do
        {
            var page = await f.ReadAsync(root.ServerPath, cursor, 200);
            Assert.IsTrue(page.Nodes.Length <= 200);
            foreach (var node in page.Nodes) Assert.IsTrue(ids.Add(node.ServerPath));
            cursor = page.NextCursor;
            pages++;
        } while (cursor != null);
        Assert.AreEqual(503, ids.Count);
        Assert.AreEqual(3, pages);
        var first = await f.ReadAsync(root.ServerPath, limit: 1);
        Assert.IsNotNull(first.NextCursor);
        Assert.AreEqual("MappingTreeExpired", (await Assert.ThrowsExactlyAsync<FederationQueryException>(() =>
            f.ReadAsync(first.Nodes.Single().ServerPath, first.NextCursor))).Code);
    }

    [TestMethod]
    public async Task CursorsAreBoundToOwnerEpochParentAndProcessKey()
    {
        await using var f = await Fixture.CreateAsync([new("/media", "Media", "mount", false)]);
        await f.AddAsync(("/media/a", false), ("/media/b", false));
        var first = await f.ReadAsync("/media", limit: 1);
        Assert.IsNotNull(first.NextCursor);
        f.Identity.Value = f.Identity.Value with { LibraryEpoch = "new-epoch" };
        Assert.AreEqual("MappingTreeExpired", (await Assert.ThrowsExactlyAsync<FederationQueryException>(() =>
            f.ReadAsync("/media", first.NextCursor))).Code);
        f.Identity.Value = f.Identity.Value with { LibraryEpoch = "epoch", NodeId = "other-node" };
        Assert.AreEqual("MappingTreeExpired", (await Assert.ThrowsExactlyAsync<FederationQueryException>(() =>
            f.ReadAsync("/media", first.NextCursor))).Code);
        f.Identity.Value = f.Identity.Value with { NodeId = "source" };
        using var otherKey = new ManagedLibraryMappingTreeTokens();
        var restarted = new ManagedLibraryMappingTreeService(f.Db, f.Storage, f.Identity, otherKey);
        Assert.AreEqual("MappingTreeExpired", (await Assert.ThrowsExactlyAsync<FederationQueryException>(() =>
            restarted.ReadAsync("/media", first.NextCursor, 200, default))).Code);
        Assert.AreEqual(2, (await f.ReadAsync("/media")).Nodes.Length);
    }

    [TestMethod]
    public async Task EmptyAndUnrecordedFileDirectoriesDoNotInventFilesystemContents()
    {
        await using var f = await Fixture.CreateAsync([]);
        Assert.AreEqual(0, (await f.ReadAsync()).Nodes.Length);
        await f.AddAsync(("/logical/share/single.jpg", true));
        var root = (await f.ReadAsync()).Nodes.Single();
        Assert.AreEqual(("share", 1L, false), (root.Name, root.ResourceCount, root.HasChildren));
        Assert.AreEqual(0, (await f.ReadAsync(root.ServerPath)).Nodes.Length);
    }

    [TestMethod]
    public async Task PathMarksAnchorUnMountedNativePathsAndMountsTakePrecedence()
    {
        await using var f = await Fixture.CreateAsync([new("/media", "Media", "mount", false)]);
        f.Db.PathMarks.AddRange(new PathMarkDbModel { Id = 1, Path = "/media/A", ConfigJson = "{}" },
            new PathMarkDbModel { Id = 2, Path = "/home/person/private/Media", ConfigJson = "{}" });
        await f.Db.SaveChangesAsync();
        await f.AddAsync(("/media/A/work", false), ("/home/person/private/Media/Movie", false));
        var roots = await f.ReadAsync();
        Assert.AreEqual(2, roots.Nodes.Length);
        Assert.AreEqual(2, roots.Nodes.Select(x => x.ServerPath).Distinct().Count());
        Assert.IsTrue(JsonSerializer.Serialize(roots).Contains("person"));
    }

    [TestMethod]
    public async Task CancelledAndInvalidRequestsDoNotPublishPartialSuccess()
    {
        await using var f = await Fixture.CreateAsync([]);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => f.Service.ReadAsync(null, null, 200, cancellation.Token));
        foreach (var limit in new[] { 0, -1, 201, int.MaxValue })
            Assert.AreEqual("InvalidMappingTreeRequest", (await Assert.ThrowsExactlyAsync<FederationQueryException>(() => f.ReadAsync(limit: limit))).Code);
        Assert.AreEqual("InvalidMappingTreeRequest", (await Assert.ThrowsExactlyAsync<FederationQueryException>(() => f.ReadAsync("/etc/../private"))).Code);
        Assert.AreEqual("InvalidMappingTreeRequest", (await Assert.ThrowsExactlyAsync<FederationQueryException>(() => f.ReadAsync(new string('a', ManagedLibraryMappingTreeTokens.MaximumTokenChars + 1)))).Code);
    }

    [TestMethod]
    public async Task MillionOwnedMetadataRowsReturnABoundedPageOrAnExplicitDeadlineWithoutHydration()
    {
        await using var f = await Fixture.CreateAsync([new("/media", "Media", "mount", false)]);
        await f.SeedAsync(1_000_000, "printf('/media/group-%03d/work-%07d',x%500,x)");
        var before = GC.GetTotalAllocatedBytes(true);
        var watch = Stopwatch.StartNew();
        var roots = await ReadWithinBudgetAsync(null);
        var rootElapsed = watch.Elapsed.TotalSeconds;
        if (roots != null)
        {
            var root = roots.Nodes.Single();
            Assert.AreEqual("/media", root.ServerPath);
            Assert.AreEqual(1_000_000L, root.ResourceCount);
        }
        var page = await ReadWithinBudgetAsync("/media");
        if (page != null)
        {
            Assert.AreEqual(200, page.Nodes.Length);
            Assert.IsNotNull(page.NextCursor);
        }
        watch.Stop();
        Assert.AreEqual(0, f.Db.ChangeTracker.Entries().Count());
        Assert.AreEqual(0, f.Storage.Probes);
        Console.WriteLine($"Million metadata mapping tree: roots={(roots == null ? "deadline" : "success")}, children={(page == null ? "deadline" : "200 nodes + cursor")}, root={rootElapsed:F2}s, child={watch.Elapsed.TotalSeconds-rootElapsed:F2}s, elapsed={watch.Elapsed.TotalSeconds:F2}s, managed allocations={GC.GetTotalAllocatedBytes(true)-before:N0} bytes, tracked=0; SQLite aggregate/cache memory is not measured by this managed allocation counter.");

        // A shared runner may legitimately reach the production four-second deadline.
        // Its explicit retryable failure must leave SQLite usable for a smaller branch.
        var smaller = await f.ReadAsync("/media/group-001");
        Assert.AreEqual(200, smaller.Nodes.Length);
        Assert.IsNotNull(smaller.NextCursor);
        Assert.IsTrue(smaller.Nodes.All(x => x.ResourceCount == 1 && !x.HasChildren));
        Assert.AreEqual(0, f.Db.ChangeTracker.Entries().Count());

        async Task<ManagedLibraryMappingTreePage?> ReadWithinBudgetAsync(string? parent)
        {
            try
            {
                var result = await f.ReadAsync(parent);
                Assert.IsTrue(result.Nodes.Length <= ManagedLibraryMappingTreeService.MaximumPageSize);
                Assert.IsTrue(JsonSerializer.SerializeToUtf8Bytes(result).Length <= ManagedLibraryMappingTreeService.MaximumResponseBytes);
                return result;
            }
            catch (FederationQueryException exception) when (exception.Code == "MappingTreeBusy")
            {
                Assert.AreEqual(503, exception.StatusCode);
                Assert.IsTrue(exception.Retryable);
                return null;
            }
        }
    }

    [TestMethod]
    public async Task FastRootCursorsUseSqliteBinaryOrderingForUnicodeNames()
    {
        // Fast-root ordering and its opaque cursor must agree with SQLite BINARY rather than
        // UTF-16 Ordinal: the astral name sorts after this private-use name in UTF-8.
        await using var unicode = await Fixture.CreateAsync([
            new("/native/\uE000", "private", "mount", false), new("/native/\U00010000", "astral", "mount", false)]);
        await unicode.AddAsync(("/native/\uE000/work",false),("/native/\U00010000/work",false));
        var first = await unicode.ReadAsync(limit:1);
        Assert.AreEqual("/native/\uE000",first.Nodes.Single().ServerPath);
        Assert.IsNotNull(first.NextCursor);
        var second = await unicode.ReadAsync(cursor:first.NextCursor,limit:1);
        Assert.AreEqual("/native/\U00010000",second.Nodes.Single().ServerPath);
        Assert.IsNull(second.NextCursor);
    }

    private sealed class Fixture(SqliteConnection connection, BakabaseDbContext db, StorageRoots storage) : IAsyncDisposable
    {
        public BakabaseDbContext Db { get; } = db;
        public StorageRoots Storage { get; } = storage;
        public Identity Identity { get; } = new();
        private readonly ManagedLibraryMappingTreeTokens _tokens = new();
        public ManagedLibraryMappingTreeService Service => _service ??= new(Db, Storage, Identity, _tokens);
        private ManagedLibraryMappingTreeService? _service;
        public Task<ManagedLibraryMappingTreePage> ReadAsync(string? parent = null, string? cursor = null, int limit = 200) =>
            Service.ReadAsync(parent, cursor, limit, default);
        public static async Task<Fixture> CreateAsync(UserStorageRoot[] roots)
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var db = new BakabaseDbContext(new DbContextOptionsBuilder<BakabaseDbContext>().UseSqlite(connection).Options);
            await db.Database.EnsureCreatedAsync();
            return new(connection, db, new(roots));
        }
        public async Task AddAsync(params (string? Path, bool IsFile)[] resources)
        {
            var id = await Db.ResourcesV2.Select(x => (int?)x.Id).MaxAsync() ?? 0;
            Db.ResourcesV2.AddRange(resources.Select(x => new ResourceDbModel { Id = ++id, Path = x.Path, IsFile = x.IsFile }));
            await Db.SaveChangesAsync();
            Db.ChangeTracker.Clear();
        }
        public Task<int> SeedAsync(int count, string pathSql)
        {
            var table = Db.Model.FindEntityType(typeof(ResourceDbModel))!.GetTableName();
#pragma warning disable EF1002 // All SQL identifiers, expressions and row counts are test-owned.
            return Db.Database.ExecuteSqlRawAsync($"""
                WITH RECURSIVE n(x) AS (SELECT 1 UNION ALL SELECT x+1 FROM n WHERE x < {count})
                INSERT INTO "{table}" (Id,Path,IsFile,CreateDt,UpdateDt,FileCreateDt,FileModifyDt,MediaLibraryId,CategoryId,Tags,Status)
                SELECT x,{pathSql},0,'2026-10-13','2026-10-13','2026-10-13','2026-10-13',0,0,0,1 FROM n;
                """);
#pragma warning restore EF1002
        }
        public async ValueTask DisposeAsync() { _tokens.Dispose(); await Db.DisposeAsync(); await connection.DisposeAsync(); }
    }
    private sealed class Identity : INodeIdentityProvider
    {
        public NodeIdentity Value { get; set; } = new("source", "epoch", "Source");
        public Task<NodeIdentity> GetAsync(CancellationToken cancellationToken = default)
        { cancellationToken.ThrowIfCancellationRequested(); return Task.FromResult(Value); }
    }
    private sealed class StorageRoots(UserStorageRoot[] roots) : IUserStoragePolicy
    {
        public int Probes;
        public bool IsRestricted => true;
        public IReadOnlyList<UserStorageRoot> GetRoots(UserStoragePurpose purpose = UserStoragePurpose.UserFiles) => roots;
        public bool IsPathAllowed(string path, UserStoragePurpose purpose = UserStoragePurpose.UserFiles) { Probes++; throw new AssertFailedException("No source filesystem probing."); }
        public void EnsurePathAllowed(string path, UserStoragePurpose purpose = UserStoragePurpose.UserFiles) { Probes++; throw new AssertFailedException(); }
        public void EnsureTreeMutationAllowed(string path) { Probes++; throw new AssertFailedException(); }
    }
}
