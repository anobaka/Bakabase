using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Models.Db;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Services;
using Bakabase.InsideWorld.Business;
using Bakabase.InsideWorld.Business.Components.Configurations.Models.Domain;
using Bakabase.Modules.Federation.Contracts;
using Bakabase.Modules.Federation.Identity;
using Bakabase.Modules.Federation.Queries;
using Bakabase.Service.Components.Federation;
using Bakabase.TestKit.Implementations;
using Bootstrap.Components.Configuration.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using DbReservedValue = Bakabase.Abstractions.Models.Db.ReservedPropertyValue;

namespace Bakabase.Tests.Federation;

[TestClass]
public sealed class ServiceLocalLibraryReaderTests
{
    [TestMethod]
    public async Task CapturesSqliteNamesUsingExistingScopeRulesWithoutHydratingOrFetchingResources()
    {
        var directory = Path.Combine(Path.GetTempPath(), "bakabase-federation-reader-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var services = new ServiceCollection();
        services.AddDbContext<BakabaseDbContext>(o => o.UseSqlite("Data Source=" + Path.Combine(directory, "library.db")));
        services.AddSingleton<IBOptions<ResourceOptions>>(new TestBOptions<ResourceOptions>(new ResourceOptions
            { PropertyValueScopePriority = [PropertyValueScope.Synchronization, PropertyValueScope.Manual, PropertyValueScope.Av] }));
        services.AddSingleton<INodeIdentityProvider, Identity>();
        services.AddSingleton<IResourceProfileIndexService, ProfileMatches>();
        // Deliberately no ResourceService, remote, enhancer, source resolver, cache or filesystem service.
        services.AddSingleton<ServiceLocalLibraryReader>();
        await using var provider = services.BuildServiceProvider();
        try
        {
            await using (var scope = provider.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<BakabaseDbContext>();
                await db.Database.EnsureCreatedAsync();
                db.ResourcesV2.AddRange(new ResourceDbModel { Id = 1, Path = "/media/first.mkv" },
                    new ResourceDbModel { Id = 2, Path = null }, new ResourceDbModel { Id = 3, Path = "/media/fallback.mkv" });
                db.ReservedPropertyValues.AddRange(
                    new DbReservedValue { ResourceId = 1, Scope = (int)PropertyValueScope.Manual, Name = "Manual" },
                    new DbReservedValue { ResourceId = 1, Scope = (int)PropertyValueScope.Synchronization, Name = "Global" },
                    new DbReservedValue { ResourceId = 1, Scope = (int)PropertyValueScope.Av, Name = "Profile" },
                    new DbReservedValue { ResourceId = 2, Scope = (int)PropertyValueScope.Manual, Name = "Wanted placeholder" },
                    new DbReservedValue { ResourceId = 3, Scope = (int)PropertyValueScope.Manual, Name = "" },
                    new DbReservedValue { ResourceId = 3, Scope = (int)PropertyValueScope.Synchronization, Name = "Must not fall through" });
                db.Set<PropertyValueScopePreferenceDbModel>().Add(new()
                {
                    ResourceId = 3, PropertyPool = PropertyPool.Reserved, PropertyId = (int)ReservedProperty.Name,
                    Priorities = "0:0"
                });
                db.Set<ResourceProfileDbModel>().Add(new()
                {
                    Id = 11, Name = "Name scope", NameTemplate = "Template must not be used",
                    PropertiesJson = System.Text.Json.JsonSerializer.Serialize(new ResourceProfilePropertyOptions
                    {
                        Properties = [new() { Pool = PropertyPool.Reserved, Id = (int)ReservedProperty.Name,
                            ScopePriority = [PropertyValueScope.Av] }]
                    })
                });
                db.Set<ResourceSourceLinkDbModel>().Add(new() { ResourceId = 2, Source = ResourceSource.Steam, SourceKey = "123" });
                await db.SaveChangesAsync();
            }
            var reader = provider.GetRequiredService<ServiceLocalLibraryReader>();
            var capture = await reader.CaptureAsync(new(1024 * 1024, 16_384), default);
            Assert.AreEqual("node", capture.NodeId);
            Assert.AreEqual("epoch", capture.LibraryEpoch);
            Assert.AreEqual("Profile", capture.Resources.Single(r => r.ResourceId == 1).EffectiveName);
            var placeholder = capture.Resources.Single(r => r.ResourceId == 2);
            Assert.AreEqual("Wanted placeholder", placeholder.EffectiveName);
            Assert.IsFalse(placeholder.HasLocalPath);
            CollectionAssert.AreEqual(new[] { (int)ResourceSource.Steam }, placeholder.SourceKinds.ToArray());
            var cutoff = capture.Resources.Single(r => r.ResourceId == 3);
            Assert.IsNull(cutoff.EffectiveName);
            Assert.AreEqual("fallback.mkv", QueryProtocol.Project(capture, cutoff).Title);

            await using (var scope = provider.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<BakabaseDbContext>();
                var value = await db.ReservedPropertyValues.SingleAsync(v => v.ResourceId == 1 && v.Scope == (int)PropertyValueScope.Av);
                value.Name = "Updated";
                await db.SaveChangesAsync();
            }
            Assert.AreEqual("Profile", capture.Resources.Single(r => r.ResourceId == 1).EffectiveName);
            var fresh = await reader.CaptureAsync(new(1024 * 1024, 16_384), default);
            Assert.AreEqual("Updated", fresh.Resources.Single(r => r.ResourceId == 1).EffectiveName);
            var tooLarge = await Assert.ThrowsExceptionAsync<FederationQueryException>(() => reader.CaptureAsync(new(100, 16_384), default));
            Assert.AreEqual("ScanBudgetExceeded", tooLarge.Code);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(directory, true);
        }
    }

    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    [DataRow(10_000)]
    [DataRow(100_000)]
    [TestCategory("FederationPerformance")]
    public async Task MeasuresRealSqliteProjectionWithNamesSourcesPreferencesAndProfiles(int count)
    {
        var directory = Path.Combine(Path.GetTempPath(), "bakabase-federation-capture-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var services = new ServiceCollection();
        services.AddDbContext<BakabaseDbContext>(o => o.UseSqlite("Data Source=" + Path.Combine(directory, "library.db")));
        services.AddSingleton<IBOptions<ResourceOptions>>(new TestBOptions<ResourceOptions>(new ResourceOptions
            { PropertyValueScopePriority = [PropertyValueScope.Synchronization, PropertyValueScope.Manual] }));
        services.AddSingleton<INodeIdentityProvider, Identity>();
        services.AddSingleton<IResourceProfileIndexService, ProfileMatches>();
        services.AddSingleton<ServiceLocalLibraryReader>();
        await using var provider = services.BuildServiceProvider();
        try
        {
            await using (var scope = provider.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<BakabaseDbContext>();
                await db.Database.EnsureCreatedAsync();
                string Table<T>() => "\"" + db.Model.FindEntityType(typeof(T))!.GetTableName() + "\"";
                var resources = Table<ResourceDbModel>();
                // Test-owned constant SQL: set-based seeding avoids measuring EF tracking/insert overhead.
                await db.Database.ExecuteSqlRawAsync($"""
                    WITH RECURSIVE n(x) AS (SELECT 1 UNION ALL SELECT x+1 FROM n WHERE x < {count})
                    INSERT INTO {resources} (Id,Path,IsFile,CreateDt,UpdateDt,FileCreateDt,FileModifyDt,MediaLibraryId,CategoryId,Tags,Status)
                    SELECT x,printf('/media/%06d.mkv',x),1,'2026-09-20','2026-09-20','2026-09-20','2026-09-20',0,0,0,1 FROM n;
                    """);
                await db.Database.ExecuteSqlRawAsync($"""
                    INSERT INTO {Table<DbReservedValue>()} (ResourceId,Scope,Name)
                    SELECT Id,0,printf('Manual %06d',Id) FROM {resources};
                    INSERT INTO {Table<DbReservedValue>()} (ResourceId,Scope,Name)
                    SELECT Id,1,printf('Synced %06d',Id) FROM {resources};
                    INSERT INTO {Table<ResourceSourceLinkDbModel>()} (ResourceId,Source,SourceKey,CreateDt)
                    SELECT Id,1,CAST(Id AS TEXT),'2026-09-20' FROM {resources};
                    INSERT INTO {Table<PropertyValueScopePreferenceDbModel>()} (ResourceId,PropertyPool,PropertyId,Priorities)
                    SELECT Id,{(int)PropertyPool.Reserved},{(int)ReservedProperty.Name},'0:0' FROM {resources} WHERE Id % 10 = 0;
                    """);
                db.Set<ResourceProfileDbModel>().Add(new()
                {
                    Id = 11, Name = "Profile", PropertiesJson = JsonSerializer.Serialize(new ResourceProfilePropertyOptions
                    {
                        Properties = [new() { Pool = PropertyPool.Reserved, Id = (int)ReservedProperty.Name,
                            ScopePriority = [PropertyValueScope.Av] }]
                    })
                });
                await db.SaveChangesAsync();
            }
            var reader = provider.GetRequiredService<ServiceLocalLibraryReader>();
            var limits = new FederationQueryLimits();
            var budget = new CaptureBudget(limits.MaxCaptureBytes, limits.MaxStringLength);
            var allocations = GC.GetTotalAllocatedBytes(true);
            var watch = Stopwatch.StartNew();
            var capture = await reader.CaptureAsync(budget, default);
            watch.Stop();
            Assert.AreEqual(count, capture.Resources.Count);
            Assert.AreEqual("Manual 000010", capture.Resources.Single(r => r.ResourceId == 10).EffectiveName);
            Assert.AreEqual("Synced 000001", capture.Resources.Single(r => r.ResourceId == 1).EffectiveName);
            var allocatedBytes = GC.GetTotalAllocatedBytes(true) - allocations;
            var snapshotEstimate = capture.Resources.Sum(r => QueryProtocol.EstimateBytes(QueryProtocol.Project(capture, r)));
            var measured = new
            {
                kind = "real-sqlite-frozen-observation", resources = count, nameScopeRows = count * 2,
                sourceRows = count, preferenceRows = count / 10, profileCount = 1,
                captureMs = watch.Elapsed.TotalMilliseconds, allocatedBytes,
                captureBudgetChargedBytes = budget.Bytes, configuredCaptureBudgetBytes = limits.MaxCaptureBytes,
                estimatedSnapshotBytes = snapshotEstimate, defaultSnapshotBudgetBytes = limits.MaxSnapshotBytes,
                processWorkingSetBytes = Environment.WorkingSet, runtime = RuntimeInformation.FrameworkDescription,
                os = RuntimeInformation.OSDescription, processorCount = Environment.ProcessorCount
            };
            var json = JsonSerializer.Serialize(measured);
            TestContext.WriteLine(json);
            var output = Environment.GetEnvironmentVariable("BAKABASE_FEDERATION_PERF_OUTPUT");
            if (!string.IsNullOrWhiteSpace(output)) await File.AppendAllTextAsync(output, json + Environment.NewLine);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(directory, true);
        }
    }

    private sealed class Identity : INodeIdentityProvider
    {
        public Task<NodeIdentity> GetAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new NodeIdentity("node", "epoch", "This node"));
        }
    }

    private sealed class ProfileMatches : IResourceProfileIndexService
    {
        public bool IsReady => true;
        public Task WaitUntilReady(CancellationToken ct = default) => Task.CompletedTask;
        public Task<IReadOnlyList<int>> GetMatchingProfileIds(int resourceId) => Task.FromResult<IReadOnlyList<int>>(resourceId == 1 ? [11] : []);
        public Task<Dictionary<int, IReadOnlyList<int>>> GetMatchingProfileIdsForResources(IEnumerable<int> resourceIds) =>
            Task.FromResult(resourceIds.ToDictionary(i => i, i => (IReadOnlyList<int>)(i == 1 ? new[] { 11 } : [])));
        public Task<IReadOnlySet<int>> GetMatchingResourceIds(int profileId) => Task.FromResult<IReadOnlySet<int>>(new HashSet<int> { 1 });
        public void InvalidateResource(int resourceId) => throw new AssertFailedException("Reader must not mutate profiles.");
        public void InvalidateResources(IEnumerable<int> resourceIds) => throw new AssertFailedException("Reader must not mutate profiles.");
        public void InvalidateProfile(int profileId) => throw new AssertFailedException("Reader must not mutate profiles.");
        public void InvalidateAllProfiles() => throw new AssertFailedException("Reader must not mutate profiles.");
        public void TriggerFullRebuild() => throw new AssertFailedException("Reader must not mutate profiles.");
        public Task RebuildAsync(Func<int, string?, Task>? onProgress, CancellationToken ct) => throw new AssertFailedException("Reader must not mutate profiles.");
    }
}
