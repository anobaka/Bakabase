using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.Abstractions.Models.Db;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Infrastructures.Components.Orm;
using Bakabase.InsideWorld.Business;
using Bakabase.InsideWorld.Business.Models.Db;
using Bakabase.Migrations.V230;
using Bakabase.Modules.Property.Abstractions.Models.Db;
using Bakabase.Modules.StandardValue.Extensions;
using Bootstrap.Components.Orm;
using Bootstrap.Components.Orm.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Tests;

/// <summary>
/// Locks down the V230 one-shot migrator that rewrites stored absolute AppData paths into
/// AppData-relative form. The migrator runs once per upgrading install, mutates the user DB,
/// and is not reversible — test coverage protects future maintenance, not current correctness.
/// </summary>
[TestClass]
public class PathsRelocationMigratorTests
{
    /// <summary>
    /// Mirrors the production <c>AppDataPathRelocator</c> contract: self-installs into
    /// <see cref="AppDataPaths"/> on construction, then performs deterministic prefix-based
    /// path rewrites. Anything under <c>/old/AppData</c> is treated as a stale absolute path;
    /// anything under <c>/new/AppData</c> is treated as the live root; URLs and unrelated
    /// absolute paths pass through.
    /// </summary>
    private sealed class StubRelocator : IAppDataPathRelocator
    {
        private const string OldRoot = "/old/AppData";
        private const string NewRoot = "/new/AppData";

        public StubRelocator()
        {
            AppDataPaths.Configure(this);
        }

        public string? Resolve(string? stored)
        {
            if (string.IsNullOrEmpty(stored)) return stored;
            if (stored.StartsWith(OldRoot + "/", StringComparison.Ordinal))
                return NewRoot + stored.Substring(OldRoot.Length);
            if (stored.StartsWith("/", StringComparison.Ordinal)) return stored;
            if (stored.StartsWith("http", StringComparison.Ordinal)) return stored;
            return NewRoot + "/" + stored;
        }

        public string? Relativize(string? path)
        {
            if (string.IsNullOrEmpty(path)) return path;
            if (path.StartsWith(NewRoot + "/", StringComparison.Ordinal))
                return path.Substring(NewRoot.Length + 1);
            return path;
        }
    }

    private string _testDir = null!;
    private ServiceProvider _sp = null!;

    [TestInitialize]
    public async Task Setup()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "bakabase-migrator-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDir);

        var services = new ServiceCollection();
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.Warning));
        services.AddBootstrapServices<BakabaseDbContext>(c => c.UseBootstrapSqLite(_testDir, "test"));
        services.AddSingleton<IAppDataPathRelocator, StubRelocator>();

        _sp = services.BuildServiceProvider();

        await using var scope = _sp.CreateAsyncScope();
        var ctx = scope.ServiceProvider.GetRequiredService<BakabaseDbContext>();
        await ctx.Database.MigrateAsync();
    }

    [TestCleanup]
    public void Cleanup()
    {
        AppDataPaths.Reset();
        _sp?.Dispose();
        try { Directory.Delete(_testDir, recursive: true); } catch { /* best-effort */ }
    }

    private static string Serialize(params string[] entries) =>
        ((object)entries.ToList()).SerializeAsStandardValue(StandardValueType.ListString)!;

    private static List<string>? Deserialize(string? serialized) =>
        serialized?.DeserializeAsStandardValue<List<string>>(StandardValueType.ListString);

    [TestMethod]
    public async Task RewritesAbsoluteOldPaths_LeavesRelativeAndUrlsUntouched_AcrossAllFiveTables()
    {
        const string oldAbs = "/old/AppData/data/covers/cover.jpg";
        const string url = "https://example.com/x.jpg";
        const string rel = "data/covers/already-relative.jpg";
        var serialized = Serialize(oldAbs, url, rel);

        await using (var seed = _sp.CreateAsyncScope())
        {
            var ctx = seed.ServiceProvider.GetRequiredService<BakabaseDbContext>();
            ctx.ReservedPropertyValues.Add(new ReservedPropertyValue
                { ResourceId = 1, Scope = 1, CoverPaths = serialized });
            ctx.ResourceCaches.Add(new ResourceCacheDbModel
                { ResourceId = 2, CoverPaths = serialized });
            ctx.ResourceSourceLinks.Add(new ResourceSourceLinkDbModel
                { ResourceId = 3, Source = ResourceSource.PathMark, SourceKey = "k", LocalCoverPaths = serialized });
            ctx.CustomPropertyValues.Add(new CustomPropertyValueDbModel
                { ResourceId = 4, PropertyId = 100, Scope = 1, Value = serialized });
            ctx.Enhancements.Add(new EnhancementDbModel
            {
                ResourceId = 5, EnhancerId = 1, Target = 0, Key = "k",
                ValueType = StandardValueType.ListString, Value = serialized,
            });
            await ctx.SaveChangesAsync();
        }

        var migrator = new PathsRelocationMigrator(_sp);
        await migrator.MigrateAfterDbMigration();

        await using var verify = _sp.CreateAsyncScope();
        var v = verify.ServiceProvider.GetRequiredService<BakabaseDbContext>();
        var expected = new List<string> { "data/covers/cover.jpg", url, rel };

        CollectionAssert.AreEqual(expected, Deserialize((await v.ReservedPropertyValues.AsNoTracking().FirstAsync()).CoverPaths));
        CollectionAssert.AreEqual(expected, Deserialize((await v.ResourceCaches.AsNoTracking().FirstAsync()).CoverPaths));
        CollectionAssert.AreEqual(expected, Deserialize((await v.ResourceSourceLinks.AsNoTracking().FirstAsync()).LocalCoverPaths));
        CollectionAssert.AreEqual(expected, Deserialize((await v.CustomPropertyValues.AsNoTracking().FirstAsync()).Value));
        CollectionAssert.AreEqual(expected, Deserialize((await v.Enhancements.AsNoTracking().FirstAsync()).Value));
    }

    [TestMethod]
    public async Task LeavesNonPathScalarValuesUntouched_InEnhancementValueColumn()
    {
        // A Decimal-typed enhancement stores "123.45" in Value. The migrator unconditionally tries
        // to deserialize every Value as ListString — for "123.45" that yields a 1-element list whose
        // sole entry round-trips through resolve→relativize as a non-path token (unchanged). The row
        // must therefore not be marked dirty and not be rewritten.
        const string scalar = "123.45";

        await using (var seed = _sp.CreateAsyncScope())
        {
            var ctx = seed.ServiceProvider.GetRequiredService<BakabaseDbContext>();
            ctx.Enhancements.Add(new EnhancementDbModel
            {
                ResourceId = 1, EnhancerId = 1, Target = 0, Key = "k",
                ValueType = StandardValueType.Decimal, Value = scalar,
            });
            await ctx.SaveChangesAsync();
        }

        var migrator = new PathsRelocationMigrator(_sp);
        await migrator.MigrateAfterDbMigration();

        await using var verify = _sp.CreateAsyncScope();
        var ctx2 = verify.ServiceProvider.GetRequiredService<BakabaseDbContext>();
        Assert.AreEqual(scalar, (await ctx2.Enhancements.AsNoTracking().FirstAsync()).Value);
    }

    [TestMethod]
    public async Task EvictsCacheVault_ForAllFiveTypeKeys_EvenWhenNoRowsChanged()
    {
        // Eviction is unconditional in the migrator (see class-level docstring). Pre-populate the
        // vault with sentinel entries under the exact type-FullName keys the production caches use,
        // run the migrator over an empty DB, and assert all five are gone.
        var vault = _sp.GetRequiredService<GlobalCacheVault>();
        var keys = new[]
        {
            typeof(ReservedPropertyValue).FullName!,
            typeof(ResourceCacheDbModel).FullName!,
            typeof(ResourceSourceLinkDbModel).FullName!,
            typeof(CustomPropertyValueDbModel).FullName!,
            typeof(EnhancementDbModel).FullName!,
        };
        foreach (var k in keys) vault[k] = "sentinel";

        var migrator = new PathsRelocationMigrator(_sp);
        await migrator.MigrateAfterDbMigration();

        foreach (var k in keys)
        {
            Assert.IsFalse(vault.ContainsKey(k), $"vault still has entry for {k}");
        }
    }
}
