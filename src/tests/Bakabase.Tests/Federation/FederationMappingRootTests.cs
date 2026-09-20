using Bakabase.Abstractions.Models.Db;
using Bakabase.InsideWorld.Business;
using Bakabase.Modules.Federation.Contracts;
using Bakabase.Modules.Federation.Media;
using Bakabase.Service.Components.Federation;
using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Tests.Federation;

[TestClass]
public sealed class FederationMappingRootTests
{
    [TestMethod]
    public async Task ReadsOnlyLocalPathsAndDeduplicatesRootsWithoutResourceHydration()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Db.ResourcesV2.AddRange(
            new ResourceDbModel { Id = 1, Path = "/media/one/movie.mp4", IsFile = true },
            new ResourceDbModel { Id = 2, Path = "/media/one/another.mp4", IsFile = true },
            new ResourceDbModel { Id = 3, Path = "/media/two", IsFile = false },
            new ResourceDbModel { Id = 4 });
        await fixture.Db.SaveChangesAsync();
        fixture.Db.ChangeTracker.Clear();
        var roots = await fixture.Service.GetMappingRootsAsync(default);
        CollectionAssert.AreEqual(new[] { "one", "two" }, roots.Select(root => root.Name).ToArray());
        Assert.IsTrue(roots.All(root => root.SourceRootId.Length == 64 && !root.SourceRootId.Contains("media")));
        Assert.AreEqual(0, fixture.Db.ChangeTracker.Entries().Count());
    }

    [TestMethod]
    public async Task RootCountLimitFailsInsteadOfReturningATruncatedList()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.SeedAsync(FederationResourceService.MaximumMappingRoots + 1,
            "printf('/media/root-%06d',x)", []);
        var error = await Assert.ThrowsExactlyAsync<FederationQueryException>(() => fixture.Service.GetMappingRootsAsync(default));
        Assert.AreEqual("MappingRootsTooLarge", error.Code);
        Assert.AreEqual(503, error.StatusCode);
    }

    [TestMethod]
    public async Task EscapedResponseBytesAreBoundedEvenWhenRootCountIsSmall()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.SeedAsync(350, "{0} || x", ["/media/" + new string('界', 600)]);
        var error = await Assert.ThrowsExactlyAsync<FederationQueryException>(() => fixture.Service.GetMappingRootsAsync(default));
        Assert.AreEqual("MappingRootsTooLarge", error.Code);
    }

    [TestMethod]
    public async Task RepeatedRootsStillPayForScannedBytesAndMalformedLargePathsAreRejected()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.SeedAsync(5000, "{0}", ["/media/" + new string('x', 3990)]);
        Assert.AreEqual("ScanBudgetExceeded", (await Assert.ThrowsExactlyAsync<FederationQueryException>(() =>
            fixture.Service.GetMappingRootsAsync(default))).Code);
        await fixture.Db.ResourcesV2.ExecuteDeleteAsync();
        await fixture.SeedAsync(1, "{0}", ["/media/" + new string('x', 100_000)]);
        Assert.AreEqual("MappingRootMetadataTooLarge", (await Assert.ThrowsExactlyAsync<FederationQueryException>(() =>
            fixture.Service.GetMappingRootsAsync(default))).Code);
    }

    [TestMethod]
    public async Task CancelledMappingReadDoesNotReturnAnEmptyOrPartialSuccess()
    {
        await using var fixture = await Fixture.CreateAsync();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => fixture.Service.GetMappingRootsAsync(cancellation.Token));
    }

    [TestMethod]
    public async Task CancellationAfterDatabaseExecutionStopsTheStreamingRead()
    {
        using var cancellation = new CancellationTokenSource();
        var trigger = new CancelAfterQuery(cancellation);
        await using var fixture = await Fixture.CreateAsync(trigger);
        await fixture.SeedAsync(1000, "printf('/media/root-%06d',x)", []);
        trigger.Enabled = true;
        await Assert.ThrowsAsync<OperationCanceledException>(() => fixture.Service.GetMappingRootsAsync(cancellation.Token));
        Assert.IsTrue(trigger.SawCancellableQuery, "The cancellation token must reach the EF reader, not only the response boundary.");
    }

    private sealed class Fixture(SqliteConnection connection, BakabaseDbContext db) : IAsyncDisposable
    {
        public BakabaseDbContext Db { get; } = db;
        // Null collaborators prove the mapping endpoint never uses full resource hydration,
        // property resolution, remote access or filesystem operations.
        public FederationResourceService Service { get; } = new(null!, null!, null!, null!, new AssetLeaseStore(), db);

        public static async Task<Fixture> CreateAsync(DbCommandInterceptor? interceptor = null)
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<BakabaseDbContext>().UseSqlite(connection);
            if (interceptor != null) options.AddInterceptors(interceptor);
            var db = new BakabaseDbContext(options.Options);
            await db.Database.EnsureCreatedAsync();
            return new Fixture(connection, db);
        }

        public Task<int> SeedAsync(int count, string pathSql, object[] parameters)
        {
            var table = Db.Model.FindEntityType(typeof(ResourceDbModel))!.GetTableName();
            // SQL shape and row counts are test-owned; only path values use parameters.
#pragma warning disable EF1002 // Identifiers/expressions come only from this test and EF's model, never user input.
            return Db.Database.ExecuteSqlRawAsync($"""
                WITH RECURSIVE n(x) AS (SELECT 1 UNION ALL SELECT x+1 FROM n WHERE x < {count})
                INSERT INTO "{table}" (Id,Path,IsFile,CreateDt,UpdateDt,FileCreateDt,FileModifyDt,MediaLibraryId,CategoryId,Tags,Status)
                SELECT x,{pathSql},0,'2026-09-20','2026-09-20','2026-09-20','2026-09-20',0,0,0,1 FROM n;
                """, parameters);
#pragma warning restore EF1002
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await connection.DisposeAsync();
        }
    }

    private sealed class CancelAfterQuery(CancellationTokenSource cancellation) : DbCommandInterceptor
    {
        public bool Enabled { get; set; }
        public bool SawCancellableQuery { get; private set; }
        public override ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
            DbDataReader result, CancellationToken cancellationToken = default)
        {
            if (Enabled)
            {
                SawCancellableQuery = cancellationToken.CanBeCanceled;
                cancellation.Cancel();
            }
            return ValueTask.FromResult(result);
        }
    }
}
