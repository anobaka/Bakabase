using System.Text.Json.Nodes;
using Bakabase.InsideWorld.Business;
using Bakabase.InsideWorld.Business.Components.DataSync;
using Bakabase.InsideWorld.Business.Components.DataSync.Apply;
using Bakabase.InsideWorld.Business.Components.DataSync.Persistence;
using Bakabase.Modules.DataSync.Abstractions;
using Bakabase.Modules.DataSync.Merging;
using Bakabase.Modules.DataSync.Planning;
using Bakabase.Modules.DataSync.Runtime;
using Bakabase.Modules.DataSync.Wire;
using Bakabase.TestKit.DataSync;
using Bakabase.TestKit.Utils;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Tests.DataSync;

/// <summary>
/// State outside the database (spec §4.7) and registration (§1, §2.11): per-provider folders, the actor watermark
/// file, the in-memory review store, and an <c>AddDataSync()</c> that resolves nothing and touches no file.
/// </summary>
[TestClass]
public class DataSyncStateFilesTests
{
    private string _root = null!;

    [TestInitialize]
    public void Setup()
    {
        _root = Path.Combine(Path.GetTempPath(), $"DataSyncStateFilesTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    #region Registration

    [TestMethod]
    public void AddDataSync_resolves_nothing_and_touches_no_file()
    {
        var services = new ServiceCollection();

        // Nothing is registered it could need (no AppService, no identity, no database): registration still works.
        services.AddDataSync();

        Assert.IsTrue(services.Any(d => d.ServiceType == typeof(IDataSyncStore)));
        Assert.IsTrue(services.Any(d => d.ServiceType == typeof(IDataSyncDataDirectory)));
        Assert.IsFalse(services.Any(d => d.ServiceType == typeof(IDataSyncDeviceIdentity)),
            "the identity is the host's (§2.11)");
        Assert.IsFalse(services.Any(d => d.ServiceType == typeof(IDataSyncHostKind)));
    }

    [TestMethod]
    public async Task Every_TestKit_provider_has_its_own_folders_and_a_fake_identity()
    {
        var a = await TestServiceBuilder.BuildServiceProvider();
        var b = await TestServiceBuilder.BuildServiceProvider();
        var dirA = a.GetRequiredService<IDataSyncDataDirectory>();
        var dirB = b.GetRequiredService<IDataSyncDataDirectory>();

        Assert.IsInstanceOfType<FixedDataSyncDataDirectory>(dirA, "the TestKit's folders win over the default");
        Assert.AreNotEqual(dirA.Path, dirB.Path);
        Assert.AreNotEqual(dirA.BackupsPath, dirB.BackupsPath);
        StringAssert.StartsWith(dirA.Path, Path.GetFullPath(Path.GetTempPath()));
        Assert.AreEqual(Path.GetDirectoryName(dirA.Path), Path.GetDirectoryName(dirA.BackupsPath));
        Assert.AreEqual("data-sync", Path.GetFileName(dirA.Path));
        Assert.AreEqual("backups", Path.GetFileName(dirA.BackupsPath));

        var deviceA = await a.GetRequiredService<IDataSyncDeviceIdentity>().GetAsync(default);
        var deviceB = await b.GetRequiredService<IDataSyncDeviceIdentity>().GetAsync(default);
        Assert.AreNotEqual(deviceA.NodeId, deviceB.NodeId);
        StringAssert.StartsWith(deviceA.Name, "test-device-");
        Assert.IsFalse(a.GetRequiredService<IDataSyncHostKind>().IsHeadless);
    }

    [TestMethod]
    public async Task A_test_can_bring_its_own_identity()
    {
        var device = new DataSyncDevice("node-x", "epoch-x", "NAS");
        var sp = await TestServiceBuilder.BuildServiceProvider(s =>
            s.AddSingleton<IDataSyncDeviceIdentity>(new TestDataSyncDeviceIdentity(device)));
        Assert.AreEqual(device, await sp.GetRequiredService<IDataSyncDeviceIdentity>().GetAsync(default));
    }

    [TestMethod]
    public void The_default_folders_are_a_TryAdd_over_AppService()
    {
        // Resolving the default would read the real app data location, so only its registration is checked here;
        // AppDataSyncDataDirectory resolves {AppData}/data-sync and {AppData}/backups on first use.
        var services = new ServiceCollection();
        services.AddDataSync();
        var descriptor = services.Single(d => d.ServiceType == typeof(IDataSyncDataDirectory));
        Assert.AreEqual(typeof(AppDataSyncDataDirectory), descriptor.ImplementationType);
        Assert.AreEqual(ServiceLifetime.Singleton, descriptor.Lifetime);

        var fixedFirst = new ServiceCollection();
        fixedFirst.AddSingleton<IDataSyncDataDirectory>(FixedDataSyncDataDirectory.Under(_root));
        fixedFirst.AddDataSync();
        Assert.AreEqual(1, fixedFirst.Count(d => d.ServiceType == typeof(IDataSyncDataDirectory)));
    }

    #endregion

    #region actor.json

    [TestMethod]
    public async Task The_watermark_file_is_written_atomically_and_read_back()
    {
        var file = new DataSyncActorWatermarkFile(FixedDataSyncDataDirectory.Under(_root));
        Assert.IsTrue(file.Read().Missing, "a missing file is not an error");

        var watermark = new DataSyncActorWatermark(3, "0123456789abcdef", 42, Guid.NewGuid().ToString("N"));
        await file.WriteAsync(watermark, default);
        await file.WriteAsync(watermark with {Counter = 43}, default);

        var read = file.Read();
        Assert.AreEqual(watermark with {Counter = 43}, read.Watermark);
        Assert.IsNull(read.Problem);
        CollectionAssert.AreEqual(new[] {DataSyncActorWatermarkFile.FileName},
            Directory.GetFiles(Path.GetDirectoryName(file.FilePath)!).Select(Path.GetFileName).ToArray(),
            "no temporary file is left behind");
        StringAssert.Contains(await File.ReadAllTextAsync(file.FilePath), "\"generation\":3");
    }

    [TestMethod]
    public async Task A_corrupted_watermark_is_reported_not_trusted()
    {
        var file = new DataSyncActorWatermarkFile(FixedDataSyncDataDirectory.Under(_root));
        Directory.CreateDirectory(Path.GetDirectoryName(file.FilePath)!);

        foreach (var content in new[]
                 {
                     "not json",
                     """{"generation":0,"actorId":"0123456789abcdef","counter":1,"dbInstanceId":"00000000000000000000000000000000"}""",
                     """{"generation":1,"actorId":"XYZ","counter":1,"dbInstanceId":"00000000000000000000000000000000"}""",
                     """{"generation":1,"actorId":"0123456789abcdef","counter":-1,"dbInstanceId":"00000000000000000000000000000000"}""",
                     """{"generation":1,"actorId":"0123456789abcdef","counter":1,"dbInstanceId":"short"}""",
                 })
        {
            await File.WriteAllTextAsync(file.FilePath, content);
            var read = file.Read();
            Assert.IsNull(read.Watermark, content);
            Assert.IsNotNull(read.Problem, content);
            Assert.IsFalse(read.Missing, content);
        }
    }

    #endregion

    #region Review store

    [TestMethod]
    public void At_most_three_reviews_are_staged_and_the_least_recently_used_goes_first()
    {
        var clock = new ManualTimeProvider(DateTimeOffset.UtcNow);
        var store = new DataSyncReviewStore(clock);
        var r1 = store.Stage(1, false, Pull());
        clock.Advance(TimeSpan.FromMinutes(1));
        var r2 = store.Stage(2, false, Pull());
        clock.Advance(TimeSpan.FromMinutes(1));
        var r3 = store.Stage(null, true, Pull());
        clock.Advance(TimeSpan.FromMinutes(1));
        store.Get(r1.ReviewId); // r1 was read: r2 is now the oldest

        var r4 = store.Stage(4, false, Pull());

        Assert.IsNotNull(store.Get(r1.ReviewId));
        Assert.IsNull(store.Get(r2.ReviewId));
        Assert.IsNotNull(store.Get(r3.ReviewId));
        Assert.IsNotNull(store.Get(r4.ReviewId));
        Assert.IsTrue(store.Get(r3.ReviewId)!.CopyOnce);
    }

    [TestMethod]
    public void A_review_expires_after_an_idle_hour_unless_it_is_applying()
    {
        var clock = new ManualTimeProvider(DateTimeOffset.UtcNow);
        var store = new DataSyncReviewStore(clock);
        var idle = store.Stage(1, false, Pull());
        var read = store.Stage(2, false, Pull());
        var applying = store.Stage(3, false, Pull());
        store.MarkApplying(applying.ReviewId, "DataSyncApply");

        clock.Advance(TimeSpan.FromMinutes(40));
        Assert.IsNotNull(store.GetForLink(2), "sliding: reading keeps it");
        clock.Advance(TimeSpan.FromMinutes(40));

        Assert.IsNull(store.GetForLink(1), "idle for 80 minutes");
        Assert.AreEqual(read.ReviewId, store.GetForLink(2)!.ReviewId);
        Assert.AreEqual("DataSyncApply", store.Get(applying.ReviewId)!.TaskId);

        clock.Advance(TimeSpan.FromHours(5));
        Assert.IsNotNull(store.Get(applying.ReviewId), "an applying review never expires");
        store.MarkApplied(applying.ReviewId, 17);
        clock.Advance(TimeSpan.FromMinutes(61));
        Assert.IsNull(store.Get(applying.ReviewId), "an applied one idles out again");
        Assert.IsNull(store.Get(idle.ReviewId));
    }

    [TestMethod]
    public void Staging_again_for_a_link_replaces_its_review_and_plans_are_kept()
    {
        var store = new DataSyncReviewStore(TimeProvider.System);
        var first = store.Stage(1, false, Pull());
        var plan = new DataSyncPlan("0123456789abcdef", "sha256:x", [], new DataSyncPlanSummary([], 0, 0, 0), []);
        store.SetLastPlan(first.ReviewId, plan);
        Assert.AreSame(plan, store.Get(first.ReviewId)!.LastPlan);

        var second = store.Stage(1, false, Pull());
        Assert.IsNull(store.Get(first.ReviewId));
        Assert.AreEqual(second.ReviewId, store.GetForLink(1)!.ReviewId);

        store.Discard(second.ReviewId);
        Assert.IsNull(store.GetForLink(1));
        Assert.ThrowsException<KeyNotFoundException>(() => store.MarkApplying(second.ReviewId, "t"));
    }

    private static DataSyncStagedPull Pull() =>
        new("peer", "Peer", new DataSyncFeedManifest("snap", 120_000, "peer", "epoch", "0123456789abcdef", 1, 1,
            "2.0.0", [], null, new DataSyncSourceAttention(false, 0, 0, false, 0)), [], DateTime.UtcNow);

    #endregion
}
