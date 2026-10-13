using System.Diagnostics;
using System.Text.Json;
using System.Reflection;
using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.Abstractions.Models.Db;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Services;
using Bakabase.InsideWorld.Business;
using Bakabase.Modules.Federation;
using Bakabase.Modules.Federation.Contracts;
using Bakabase.Modules.Federation.Identity;
using Bakabase.Modules.Federation.Media;
using Bakabase.Modules.Federation.Queries;
using Bakabase.Modules.Federation.Security;
using Bakabase.Service.Components.Federation;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Tests.Federation;

[TestClass]
public sealed class FederationMappingTreeTests
{
    [TestMethod]
    public async Task CountsOwnedDirectoryMetadataWithoutScanningFilesOrExposingAbsolutePaths()
    {
        await using var f = await Fixture.CreateAsync([new("/Volumes/private-nas", "Media share", "mount", false)]);
        await f.AddAsync(("/Volumes/private-nas/Movies/A", false), ("/Volumes/private-nas/Movies/B", false),
            ("/Volumes/private-nas/Movies/A/cover.jpg", true), ("/Volumes/private-nas/image.jpg", true),
            (null, false));
        var roots = await f.ReadAsync();
        var root = roots.Nodes.Single();
        Assert.AreEqual(("Media share", 4L, true), (root.Name, root.ResourceCount, root.HasChildren));
        Assert.IsFalse(JsonSerializer.Serialize(roots).Contains("/Volumes"));
        Assert.IsFalse(System.Text.Encoding.UTF8.GetString(NodeRequestSignature.Decode(root.BrowseToken)).Contains("private-nas"));
        var children = await f.ReadAsync(root.BrowseToken);
        Assert.AreEqual(("Movies", 3L, true), (children.Nodes.Single().Name, children.Nodes.Single().ResourceCount, children.Nodes.Single().HasChildren));
        var works = await f.ReadAsync(children.Nodes.Single().BrowseToken);
        CollectionAssert.AreEqual(new[] { "A", "B" }, works.Nodes.Select(x => x.Name).ToArray());
        CollectionAssert.AreEqual(new long[] { 2, 1 }, works.Nodes.Select(x => x.ResourceCount).ToArray());
        Assert.IsFalse(works.Nodes.Any(x => x.HasChildren), "A contained file is not a directory metadata child.");
        Assert.AreEqual(0, (await f.ReadAsync(works.Nodes[0].BrowseToken)).Nodes.Length);
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
        var media = roots.Nodes.Single(x => x.Name == "Media");
        Assert.AreEqual(3L, media.ResourceCount);
        var children = await f.ReadAsync(media.BrowseToken);
        CollectionAssert.AreEquivalent(new[] { "A", "a", "😀_100%" }, children.Nodes.Select(x => x.Name).ToArray());
        var emoji = children.Nodes.Single(x => x.Name == "😀_100%");
        Assert.AreEqual("child", (await f.ReadAsync(emoji.BrowseToken)).Nodes.Single().Name);
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
        var movies = (await f.ReadAsync(drive.BrowseToken)).Nodes.Single();
        Assert.IsTrue(movies.Name.Equals("Movies", StringComparison.OrdinalIgnoreCase));
        var works = await f.ReadAsync(movies.BrowseToken);
        Assert.AreEqual(2, works.Nodes.Length);
        Assert.AreEqual(2L, works.Nodes.Single(x => x.Name.Equals("A", StringComparison.OrdinalIgnoreCase)).ResourceCount);
        var share = roots.Nodes.Single(x => x.Name.Equals("Share", StringComparison.OrdinalIgnoreCase));
        Assert.AreEqual(2L, share.ResourceCount);
        var series = (await f.ReadAsync(share.BrowseToken)).Nodes.Single();
        Assert.AreEqual(2L, (await f.ReadAsync(series.BrowseToken)).Nodes.Single().ResourceCount);
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
            var page = await f.ReadAsync(root.BrowseToken, cursor, 200);
            Assert.IsTrue(page.Nodes.Length <= 200);
            foreach (var node in page.Nodes) Assert.IsTrue(ids.Add(node.SourceRootId));
            cursor = page.NextCursor;
            pages++;
        } while (cursor != null);
        Assert.AreEqual(503, ids.Count);
        Assert.AreEqual(3, pages);
        var first = await f.ReadAsync(root.BrowseToken, limit: 1);
        Assert.IsNotNull(first.NextCursor);
        Assert.AreEqual("MappingTreeExpired", (await Assert.ThrowsExactlyAsync<FederationQueryException>(() =>
            f.ReadAsync(first.Nodes.Single().BrowseToken, first.NextCursor))).Code);
    }

    [TestMethod]
    public async Task TokensAreBoundToGrantRevisionNodeEpochAndProcessKey()
    {
        await using var f = await Fixture.CreateAsync([new("/media", "Media", "mount", false)]);
        await f.AddAsync(("/media/a", false));
        var root = (await f.ReadAsync()).Nodes.Single();
        Assert.AreEqual("MappingTreeExpired", (await Assert.ThrowsExactlyAsync<FederationQueryException>(() =>
            f.Service.ReadAsync("other-grant", root.BrowseToken, null, 200, default))).Code);
        f.Access.Version++;
        Assert.AreEqual("MappingTreeExpired", (await Assert.ThrowsExactlyAsync<FederationQueryException>(() => f.ReadAsync(root.BrowseToken))).Code);
        f.Access.Version--;
        f.Identity.Value = f.Identity.Value with { LibraryEpoch = "new-epoch" };
        Assert.AreEqual("MappingTreeExpired", (await Assert.ThrowsExactlyAsync<FederationQueryException>(() => f.ReadAsync(root.BrowseToken))).Code);
        f.Identity.Value = f.Identity.Value with { LibraryEpoch = "epoch", NodeId = "other-node" };
        Assert.AreEqual("MappingTreeExpired", (await Assert.ThrowsExactlyAsync<FederationQueryException>(() => f.ReadAsync(root.BrowseToken))).Code);
        f.Identity.Value = f.Identity.Value with { NodeId = "source" };
        using var otherKey = new FederationMappingTreeTokens();
        var restarted = new FederationMappingTreeService(f.Db, f.Storage, f.Identity, f.Access, otherKey);
        Assert.AreEqual("MappingTreeExpired", (await Assert.ThrowsExactlyAsync<FederationQueryException>(() =>
            restarted.ReadAsync("reader-grant", root.BrowseToken, null, 200, default))).Code);
        var fresh = (await f.ReadAsync()).Nodes.Single();
        Assert.AreEqual(root.SourceRootId, fresh.SourceRootId, "Saved mapping IDs survive browsing-session expiration.");
    }

    [TestMethod]
    public async Task EmptyAndUnrecordedFileDirectoriesDoNotInventFilesystemContents()
    {
        await using var f = await Fixture.CreateAsync([]);
        Assert.AreEqual(0, (await f.ReadAsync()).Nodes.Length);
        await f.AddAsync(("/logical/share/single.jpg", true));
        var root = (await f.ReadAsync()).Nodes.Single();
        Assert.AreEqual(("share", 1L, false), (root.Name, root.ResourceCount, root.HasChildren));
        Assert.AreEqual(0, (await f.ReadAsync(root.BrowseToken)).Nodes.Length);
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
        Assert.IsTrue(roots.Nodes.All(x => x.Name == "Media"));
        Assert.IsFalse(JsonSerializer.Serialize(roots).Contains("person"));
        var groups = await f.Service.GetLocationsAsync("/media/A/work", "/media/A/work/video.mp4", default);
        Assert.AreEqual(FederationMappingPaths.GroupId("/media"), groups.Last().SourceRootId);
        Assert.AreEqual("A/work/video.mp4", groups.Last().RelativePath);
        var native = await f.Service.GetLocationsAsync("/home/person/private/Media/Movie", "/home/person/private/Media/Movie", default, targetIsDirectory:true);
        Assert.AreEqual("Movie", native.Last().RelativePath);
        Assert.AreEqual(FederationMappingPaths.GroupId("/home/person/private/Media"), native.Last().SourceRootId);
    }

    [TestMethod]
    public async Task CancelledAndInvalidRequestsDoNotPublishPartialSuccess()
    {
        await using var f = await Fixture.CreateAsync([]);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => f.Service.ReadAsync("reader-grant", null, null, 200, cancellation.Token));
        foreach (var limit in new[] { 0, -1, 201, int.MaxValue })
            Assert.AreEqual("InvalidMappingTreeRequest", (await Assert.ThrowsExactlyAsync<FederationQueryException>(() => f.ReadAsync(limit: limit))).Code);
        Assert.AreEqual("InvalidMappingTreeRequest", (await Assert.ThrowsExactlyAsync<FederationQueryException>(() => f.ReadAsync("/etc/private"))).Code);
        Assert.AreEqual("InvalidMappingTreeRequest", (await Assert.ThrowsExactlyAsync<FederationQueryException>(() => f.ReadAsync(new string('a', FederationMappingTreeTokens.MaximumTokenChars + 1)))).Code);
    }

    [TestMethod]
    public async Task MillionOwnedMetadataRowsReturnABoundedPageOrAnExplicitDeadlineWithoutHydration()
    {
        await using var f = await Fixture.CreateAsync([new("/media", "Media", "mount", false)]);
        await f.SeedAsync(1_000_000, "printf('/media/group-%03d/work-%07d',x%500,x)");
        var before = GC.GetTotalAllocatedBytes(true);
        var watch = Stopwatch.StartNew();
        var roots = await f.ReadAsync();
        var rootElapsed = watch.Elapsed.TotalSeconds;
        var root = roots.Nodes.Single();
        Assert.AreEqual(1_000_000L, root.ResourceCount);
        var page = await f.ReadAsync(root.BrowseToken);
        Assert.AreEqual(200, page.Nodes.Length);
        Assert.IsNotNull(page.NextCursor);
        Assert.IsTrue(JsonSerializer.SerializeToUtf8Bytes(page).Length <= FederationMappingTreeService.MaximumResponseBytes);
        watch.Stop();
        Assert.AreEqual(0, f.Db.ChangeTracker.Entries().Count());
        Console.WriteLine($"Million metadata mapping tree: outcome=200 nodes + cursor, root={rootElapsed:F2}s, child={watch.Elapsed.TotalSeconds-rootElapsed:F2}s, elapsed={watch.Elapsed.TotalSeconds:F2}s, managed allocations={GC.GetTotalAllocatedBytes(true)-before:N0} bytes, tracked=0; SQLite aggregate/cache memory is not measured by this managed allocation counter.");

        // Fast-root ordering and its opaque cursor must agree with SQLite BINARY rather than
        // UTF-16 Ordinal: the astral name sorts after this private-use name in UTF-8.
        await using var unicode = await Fixture.CreateAsync([
            new("/native/\uE000", "private", "mount", false), new("/native/\U00010000", "astral", "mount", false)]);
        await unicode.AddAsync(("/native/\uE000/work",false),("/native/\U00010000/work",false));
        var first = await unicode.ReadAsync(limit:1);
        Assert.AreEqual("private",first.Nodes.Single().Name);
        Assert.IsNotNull(first.NextCursor);
        var second = await unicode.ReadAsync(cursor:first.NextCursor,limit:1);
        Assert.AreEqual("astral",second.Nodes.Single().Name);
        Assert.IsNull(second.NextCursor);
    }

    [TestMethod]
    public void DeepOrMalformedMetadataRetainsLegacyFallback()
    {
        var deep = "/media/" + string.Join('/', Enumerable.Range(0, 70).Select(x => "d" + x));
        Assert.AreEqual(0, FederationMappingPaths.Locations(deep, deep + "/image.jpg", ["/media"], []).Length);
        Assert.IsNull(FederationMappingPaths.Normalize("relative/file"));
        Assert.IsNull(FederationMappingPaths.Normalize("/media/../outside"));
        Assert.IsNull(FederationMappingPaths.Normalize("//server//share/work"));
        var longSegments = "/media/" + string.Join('/',Enumerable.Range(0,60).Select(x=>new string('a',55)+x));
        Assert.AreEqual(0,FederationMappingPaths.Locations(longSegments,longSegments+"/image.jpg",["/media"],[]).Length,
            "A shallow-enough but very verbose ancestor list also has a producer memory budget.");
    }

    [TestMethod]
    public async Task CumulativeResolveHintsStayInBudgetWhileLegacyFieldsAndAllAssetsSurvive()
    {
        using var directory = new FederationBrowsingControlTests.StateDirectory();
        var root=directory.Ensure();
        var work=root;
        for(var i=0;i<20;i++) work=Path.Combine(work,"folder-with-long-name-"+i.ToString("D3"));
        Directory.CreateDirectory(work);
        for(var i=0;i<16;i++) await File.WriteAllTextAsync(Path.Combine(work,$"image-{i:D3}.jpg"),"fixture");
        await using var f=await Fixture.CreateAsync([new(root,"Share","mount",false)]);
        var proxy=DispatchProxy.Create<IResourceService,Resources>();
        ((Resources)proxy).Path=work;
        var resources=new FederationResourceService(proxy,DispatchProxy.Create<IPropertyValueScopeResolver,EmptyScopes>(),
            f.Identity,f.Access,new AssetLeaseStore(),f.Db,f.Service);
        var response=await resources.ResolveAsync("reader-grant",new(Enumerable.Range(1,32).Select(x=>new ResourceRef("source","epoch",x)).ToArray()),default);
        var assets=response.Resources.SelectMany(x=>x.Assets).ToArray();
        Assert.AreEqual(512,assets.Length);
        Assert.IsTrue(assets.All(x=>x.SourceRootId is {Length:64} && x.RelativePath is {Length:>0}));
        Assert.IsTrue(assets.Any(x=>x.MappingLocations is {Length:>0}));
        Assert.IsTrue(assets.Any(x=>x.MappingLocations is {Length:0}),"Over-budget optional hints fall back to the legacy fields.");
        var bytes=assets.Sum(x=>x.MappingLocations is {Length:>0} list?JsonSerializer.SerializeToUtf8Bytes(list,FederationJson.Options).Length:0L)+
            response.Resources.Sum(x=>x.Location?.MappingLocations is {Length:>0} list?JsonSerializer.SerializeToUtf8Bytes(list,FederationJson.Options).Length:0L);
        Assert.IsTrue(bytes<=2*1024*1024,$"Optional hint wire bytes: {bytes}");
    }

    [TestMethod]
    public async Task NearEnvelopeBaseKeepsAllPropertiesAndLegacyAssetsByDroppingOptionalHints()
    {
        using var directory=new FederationBrowsingControlTests.StateDirectory();
        var root=directory.Ensure();
        var work=root;
        for(var i=0;i<20;i++) work=Path.Combine(work,"folder-with-long-name-"+i.ToString("D3"));
        Directory.CreateDirectory(work);
        for(var i=0;i<16;i++) await File.WriteAllTextAsync(Path.Combine(work,$"image-{i:D3}.jpg"),"fixture");
        await using var f=await Fixture.CreateAsync([new(root,"Share","mount",false)]);
        var proxy=DispatchProxy.Create<IResourceService,Resources>();
        var source=(Resources)proxy;
        source.Path=work;
        source.HugeProperty=new string('x',7*1024*1024);
        var scopes=DispatchProxy.Create<IPropertyValueScopeResolver,HugeScopes>();
        ((HugeScopes)scopes).Value=source.HugeProperty;
        var resources=new FederationResourceService(proxy,scopes,f.Identity,f.Access,new AssetLeaseStore(),f.Db,f.Service);
        var response=await resources.ResolveAsync("reader-grant",new(Enumerable.Range(1,32).Select(x=>new ResourceRef("source","epoch",x)).ToArray()),default);
        Assert.AreEqual(32,response.Resources.Length);
        var assets=response.Resources.SelectMany(x=>x.Assets).ToArray();
        Assert.AreEqual(512,assets.Length);
        Assert.IsTrue(assets.All(x=>x.SourceRootId is {Length:64} && x.RelativePath is {Length:>0} && x.MappingLocations==null));
        Assert.IsTrue(response.Resources.All(x=>x.Location?.MappingLocations==null));
        Assert.AreSame(source.HugeProperty,response.Resources[0].Properties.Single().Value);
        Assert.AreEqual("Large description",response.Resources[0].Properties.Single().Label);
        var bytes=JsonSerializer.SerializeToUtf8Bytes(response,FederationJson.Options).Length;
        Assert.IsTrue(bytes>7*1024*1024 && bytes<8*1024*1024,$"Legacy base wire bytes retained: {bytes}");
        var baseOnly=response.Resources[0].Assets[0];
        var serialized=JsonSerializer.Serialize(baseOnly,FederationJson.Options);
        Assert.IsFalse(serialized.Contains("mappingLocations"));
        Assert.IsFalse(serialized.Contains("canOpenLocally"));
    }

    [TestMethod]
    public async Task AnchorQuotaIsExplicitForTreeButCannotBreakLegacyResolveOrDirectoryLocation()
    {
        using var directory=new FederationBrowsingControlTests.StateDirectory();
        var root=directory.Ensure();
        var file=Path.Combine(root,"image.jpg");
        await File.WriteAllTextAsync(file,"fixture");
        await using var f=await Fixture.CreateAsync([new(root,"Share","mount",false)]);
        f.Db.PathMarks.AddRange(Enumerable.Range(1,FederationMappingTreeService.MaximumAnchors+1)
            .Select(x=>new PathMarkDbModel{Id=x,Path="/many/distinct/marks/"+x,ConfigJson="{}"}));
        await f.Db.SaveChangesAsync();
        await f.AddAsync((file,true));
        var proxy=DispatchProxy.Create<IResourceService,Resources>();
        ((Resources)proxy).Path=file;
        ((Resources)proxy).IsFile=true;
        var resources=new FederationResourceService(proxy,DispatchProxy.Create<IPropertyValueScopeResolver,EmptyScopes>(),
            f.Identity,f.Access,new AssetLeaseStore(),f.Db,f.Service);
        var reference=new ResourceRef("source","epoch",1);
        var location=await resources.GetLocationAsync("reader-grant",reference,default);
        Assert.IsNotNull(location.Response.Location);
        Assert.AreEqual(64,location.Response.Location.SourceRootId.Length);
        Assert.AreEqual("image.jpg",location.Response.Location.RelativePath);
        Assert.AreEqual(file,location.LocalPath);
        Assert.AreEqual(0,location.Response.Location.MappingLocations!.Length);
        var asset=(await resources.ResolveAsync("reader-grant",new([reference]),default)).Resources.Single().Assets.Single();
        Assert.AreEqual(64,asset.SourceRootId!.Length);
        Assert.AreEqual("image.jpg",asset.RelativePath);
        Assert.AreEqual(0,asset.MappingLocations!.Length);
        var error=await Assert.ThrowsExactlyAsync<FederationQueryException>(()=>f.ReadAsync());
        Assert.AreEqual("MappingTreeMetadataTooLarge",error.Code);
        using var cancellation=new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(()=>f.Service.GetLocationsAsync(root,file,cancellation.Token));
    }

    public class Resources:DispatchProxy
    {
        public string Path=null!;
        public bool IsFile;
        public string? HugeProperty;
        protected override object? Invoke(MethodInfo? method,object?[]? args)
        {
            if(method!.Name!=nameof(IResourceService.Get)) throw new AssertFailedException("The exporter must hydrate only requested resources.");
            var resource=new Resource{Id=(int)args![0]!,Path=Path,IsFile=IsFile};
            if(resource.Id==1 && HugeProperty!=null) resource.Properties=new()
            {[(int)PropertyPool.Custom]=new(){[1]=new("Large description",PropertyType.MultilineText,null)}};
            return Task.FromResult<Resource?>(resource);
        }
    }
    public class EmptyScopes:DispatchProxy
    {
        protected override object? Invoke(MethodInfo? method,object?[]? args)=>method!.Name==nameof(IPropertyValueScopeResolver.Resolve)
            ?null:throw new AssertFailedException();
    }
    public class HugeScopes:DispatchProxy
    {
        public string Value=null!;
        protected override object? Invoke(MethodInfo? method,object?[]? args)
        {
            if(method!.Name!=nameof(IPropertyValueScopeResolver.Resolve)) throw new AssertFailedException();
            return (PropertyPool)args![1]! == PropertyPool.Custom
                ?new Resource.Property.PropertyValue(0,Value,Value,Value):null;
        }
    }

    private sealed class Fixture(SqliteConnection connection, BakabaseDbContext db, StorageRoots storage) : IAsyncDisposable
    {
        public BakabaseDbContext Db { get; } = db;
        public StorageRoots Storage { get; } = storage;
        public Identity Identity { get; } = new();
        public Access Access { get; } = new();
        private readonly FederationMappingTreeTokens _tokens = new();
        public FederationMappingTreeService Service => _service ??= new(Db, Storage, Identity, Access, _tokens);
        private FederationMappingTreeService? _service;
        public Task<MappingTreePage> ReadAsync(string? parent = null, string? cursor = null, int limit = 200) =>
            Service.ReadAsync("reader-grant", parent, cursor, limit, default);
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
    private sealed class Access : IFederationQueryAccess
    {
        public long Version { get; set; } = 1;
        public Task<QueryAccess> ValidateAsync(string grantId, string expectedLibraryEpoch, CancellationToken cancellationToken)
        { cancellationToken.ThrowIfCancellationRequested(); return Task.FromResult(new QueryAccess("source", expectedLibraryEpoch, grantId, Version)); }
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
