using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.Abstractions.Models.Db;
using Bakabase.InsideWorld.Business;
using Bakabase.Modules.Federation.Contracts;
using Bakabase.Modules.Federation.Identity;
using Bakabase.Modules.Federation.Media;
using Bakabase.Modules.Federation.Queries;
using Bakabase.Service.Components.Federation;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Tests.Federation;

[TestClass]
public sealed class FederationMappingPathCaseTests
{
    [TestMethod]
    [DataRow(@"Y:\σ\Album", "y:/ς/album")]
    [DataRow(@"Y:\Movies\Album", "y:/movies/album")]
    [DataRow(@"\\NAS\Share\σ\Album", "//nas/share/ς/album")]
    [DataRow(@"\\NAS\Share\Movies", "//nas/share/movies")]
    public void WindowsAliasesShareGroupIdentityAndRespectSeparatorBoundaries(string first, string second)
    {
        var firstRoot = FederationMappingPaths.Normalize(first)!;
        var secondRoot = FederationMappingPaths.Normalize(second)!;
        Assert.IsTrue(StringComparer.OrdinalIgnoreCase.Equals(firstRoot, secondRoot));
        Assert.AreEqual(FederationMappingPaths.GroupId(firstRoot), FederationMappingPaths.GroupId(secondRoot));
        Assert.IsTrue(FederationMappingPaths.Within(secondRoot, firstRoot));
        Assert.IsTrue(FederationMappingPaths.Within(secondRoot + "/child/image.jpg", firstRoot));
        Assert.IsFalse(FederationMappingPaths.Within(secondRoot + "-other/image.jpg", firstRoot));
        var locations = FederationMappingPaths.Locations(firstRoot, secondRoot + "/Image.JPG", [], [],
            "unchanged-legacy-id", "Image.JPG");
        Assert.AreEqual(new FederatedMappingLocation("unchanged-legacy-id", "Image.JPG"), locations[0],
            "Case normalization is only for new group IDs; the original legacy mapping stays first at its own level.");
        Assert.AreEqual(new FederatedMappingLocation(FederationMappingPaths.GroupId(firstRoot), "Image.JPG"), locations[1]);
    }

    [TestMethod]
    [DataRow("/media/σ/Album", "/media/ς/Album")]
    [DataRow("/media/Movies", "/media/movies")]
    public void PosixCaseVariantsKeepDistinctGroupsAndBoundaries(string first, string second)
    {
        Assert.AreNotEqual(FederationMappingPaths.GroupId(first), FederationMappingPaths.GroupId(second));
        Assert.IsFalse(FederationMappingPaths.Within(second, first));
        Assert.IsFalse(FederationMappingPaths.Within(second + "/image.jpg", first));
    }

    [TestMethod]
    [DataRow(@"Y:\σ\Album", "y:/ς/album")]
    [DataRow(@"Y:\Movies\Album", "y:/movies/album")]
    [DataRow(@"\\NAS\Share\σ\Album", "//nas/share/ς/album")]
    [DataRow(@"\\NAS\Share\Movies", "//nas/share/movies")]
    public async Task SqliteMergedWindowsDirectoryMatchesBothResourceMappingCandidates(string first, string second)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new BakabaseDbContext(new DbContextOptionsBuilder<BakabaseDbContext>()
            .UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        var firstFile = first + @"\one.jpg";
        var secondFile = second + "/two.jpg";
        db.ResourcesV2.AddRange(
            new ResourceDbModel { Id = 1, Path = firstFile, IsFile = true },
            new ResourceDbModel { Id = 2, Path = secondFile, IsFile = true });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        using var tokens = new FederationMappingTreeTokens();
        var tree = new FederationMappingTreeService(db, new MetadataOnlyStorage(), new Identity(), new Access(), tokens);
        var page = await tree.ReadAsync("reader-grant", null, null, 200, default);
        var node = page.Nodes.Single();
        var levels = 0;
        while (node.HasChildren)
        {
            Assert.AreEqual(2L, node.ResourceCount, "The two case aliases must share every tree level.");
            Assert.IsTrue(levels++ < 8, "The small metadata fixture did not reach its directory.");
            page = await tree.ReadAsync("reader-grant", node.BrowseToken, null, 200, default);
            node = page.Nodes.Single();
        }
        Assert.AreEqual(2L, node.ResourceCount);
        Assert.IsTrue(StringComparer.OrdinalIgnoreCase.Equals(
            node.Name, FederationMappingPaths.Name(FederationMappingPaths.Normalize(first)!)));
        Assert.AreEqual(FederationMappingPaths.GroupId(FederationMappingPaths.Normalize(first)!), node.SourceRootId);
        Assert.AreEqual(FederationMappingPaths.GroupId(FederationMappingPaths.Normalize(second)!), node.SourceRootId);
        // The source may retain one spelling for the resource root and another for its asset.
        foreach (var (file, legacyId, relative) in new[]
                 { (firstFile, "legacy-one", "one.jpg"), (secondFile, "legacy-two", "two.jpg") })
        {
            var locations = await tree.GetLocationsAsync(first, file, default, legacyId, relative);
            Assert.AreEqual(new FederatedMappingLocation(legacyId, relative), locations[0]);
            Assert.AreEqual(new FederatedMappingLocation(node.SourceRootId, relative), locations[1],
                "A saved rule selected from the SQL tree must resolve either resource's original path spelling.");
        }
        Assert.AreEqual(0, db.ChangeTracker.Entries().Count());
    }

    private sealed class Identity : INodeIdentityProvider
    {
        public Task<NodeIdentity> GetAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new NodeIdentity("source", "epoch", "Source"));
    }

    private sealed class Access : IFederationQueryAccess
    {
        public Task<QueryAccess> ValidateAsync(string grantId, string expectedLibraryEpoch, CancellationToken cancellationToken) =>
            Task.FromResult(new QueryAccess("source", expectedLibraryEpoch, grantId, 1));
    }

    private sealed class MetadataOnlyStorage : IUserStoragePolicy
    {
        public bool IsRestricted => true;
        public IReadOnlyList<UserStorageRoot> GetRoots(UserStoragePurpose purpose = UserStoragePurpose.UserFiles) => [];
        public bool IsPathAllowed(string path, UserStoragePurpose purpose = UserStoragePurpose.UserFiles) =>
            throw new AssertFailedException("The mapping tree must not probe source files.");
        public void EnsurePathAllowed(string path, UserStoragePurpose purpose = UserStoragePurpose.UserFiles) =>
            throw new AssertFailedException("The mapping tree must not probe source files.");
        public void EnsureTreeMutationAllowed(string path) =>
            throw new AssertFailedException("Read-only directory metadata must not mutate files.");
    }
}
