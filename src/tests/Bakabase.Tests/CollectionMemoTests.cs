using Bakabase.InsideWorld.Business;
using Bakabase.InsideWorld.Business.Components.CollectionMemo;
using Bakabase.InsideWorld.Business.Components.CollectionMemo.Models.Db;
using Bakabase.InsideWorld.Business.Components.CollectionMemo.Models.Input;
using Bakabase.Service.Components.RemoteAccess;
using Bakabase.Service.Controllers;
using Bootstrap.Models.Constants;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Newtonsoft.Json;

namespace Bakabase.Tests;

[TestClass]
public class CollectionMemoTests
{
    private string _directory = null!;
    private DbContextOptions<BakabaseDbContext> _options = null!;
    private BakabaseDbContext _db = null!;
    private CollectionMemoService _service = null!;

    [TestInitialize]
    public async Task Initialize()
    {
        _directory = Path.Combine(Path.GetTempPath(), $"BakabaseCollectionMemo_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_directory);
        _options = new DbContextOptionsBuilder<BakabaseDbContext>()
            .UseSqlite($"Data Source={Path.Combine(_directory, "memo.db")};Pooling=False").Options;
        _db = new BakabaseDbContext(_options);
        await _db.Database.EnsureCreatedAsync();
        _service = new CollectionMemoService(_db);
    }

    [TestCleanup]
    public async Task Cleanup()
    {
        await _db.DisposeAsync();
        Directory.Delete(_directory, true);
    }

    [TestMethod]
    public async Task Targets_AreTrimmedUniqueAndSortedAfterRenaming()
    {
        Assert.AreEqual(0, (await _service.CreateTarget(new() {Name = "  Zebra  "})).Code);
        Assert.AreEqual(0, (await _service.CreateTarget(new() {Name = "alpha"})).Code);
        Assert.AreNotEqual(0, (await _service.CreateTarget(new() {Name = "zEBRA"})).Code);
        Assert.AreNotEqual(0, (await _service.CreateTarget(new() {Name = "  "})).Code);
        Assert.AreNotEqual(0, (await _service.CreateTarget(new() {Name = new string('x', 201)})).Code);

        var targets = await _service.GetTargets();
        CollectionAssert.AreEqual(new[] {"alpha", "Zebra"}, targets.Select(t => t.Name).ToArray());
        Assert.AreNotEqual(0, (await _service.UpdateTarget(targets[1].Id, new() {Name = " ALPHA "})).Code);
        Assert.AreEqual(0, (await _service.UpdateTarget(targets[1].Id, new() {Name = "Aardvark"})).Code);
        CollectionAssert.AreEqual(new[] {"Aardvark", "alpha"},
            (await _service.GetTargets()).Select(t => t.Name).ToArray());
    }

    [TestMethod]
    public async Task Ranges_PersistUtcOffsetsAndSecondsAcrossContextRestart()
    {
        var id = await CreateTarget("exhentai");
        Assert.AreEqual(0, (await _service.CreateRange(id, Range("2026-09-01T08:00:17+08:00",
            "2026-09-05T16:00:23+08:00"))).Code);

        await _db.DisposeAsync();
        _db = new BakabaseDbContext(_options);
        _service = new CollectionMemoService(_db);
        var target = (await _service.GetTargets()).Single();
        var range = target.Ranges.Single();
        Assert.AreEqual("exhentai", target.Name);
        Assert.AreEqual(new DateTime(2026, 9, 1, 0, 0, 17, DateTimeKind.Utc), range.StartAt);
        Assert.AreEqual(new DateTime(2026, 9, 5, 8, 0, 23, DateTimeKind.Utc), range.EndAt);
        Assert.AreEqual(DateTimeKind.Utc, range.StartAt.Kind);
        StringAssert.Contains(JsonConvert.SerializeObject(range, new JsonSerializerSettings
        {
            DateFormatString = "yyyy-MM-dd HH:mm:ss.fff"
        }), "2026-09-01T00:00:17Z");
    }

    [TestMethod]
    public async Task Ranges_AllowPointsAndOverlapsAndSortByStart()
    {
        var id = await CreateTarget("Target");
        await _service.CreateRange(id, Range("2026-09-05T00:00:00Z", "2026-09-05T00:00:00Z"));
        await _service.CreateRange(id, Range("2026-09-01T00:00:00Z", "2026-09-06T00:00:00Z"));
        await _service.CreateRange(id, Range("2026-09-02T00:00:00Z", "2026-09-04T00:00:00Z"));
        var ranges = (await _service.GetTargets()).Single().Ranges;
        Assert.AreEqual(3, ranges.Count, "Overlaps remain separate editable records.");
        CollectionAssert.AreEqual(new[] {1, 2, 5}, ranges.Select(r => r.StartAt.Day).ToArray());
        Assert.AreEqual(ranges[2].StartAt, ranges[2].EndAt);
    }

    [TestMethod]
    public async Task RangeChanges_EnforceOwnershipAndTargetDeletionCascades()
    {
        var first = await CreateTarget("First");
        var second = await CreateTarget("Second");
        var original = Range("2026-09-01T00:00:00Z", "2026-09-02T00:00:00Z");
        await _service.CreateRange(first, original);
        var rangeId = (await _service.GetTargets()).Single(t => t.Id == first).Ranges.Single().Id;
        var updated = Range("2026-09-03T00:00:00Z", "2026-09-04T00:00:00Z");
        Assert.AreEqual((int) ResponseCode.NotFound, (await _service.UpdateRange(second, rangeId, updated)).Code);
        Assert.AreEqual((int) ResponseCode.NotFound, (await _service.DeleteRange(second, rangeId)).Code);
        Assert.AreEqual(0, (await _service.UpdateRange(first, rangeId, updated)).Code);
        Assert.AreEqual(3, (await _service.GetTargets()).Single(t => t.Id == first).Ranges.Single().StartAt.Day);
        Assert.AreEqual(0, (await _service.DeleteRange(first, rangeId)).Code);
        Assert.AreEqual(0, await _db.CollectionMemoRanges.CountAsync());

        await _service.CreateRange(first, original);
        Assert.AreEqual(0, (await _service.DeleteTarget(first)).Code);
        Assert.AreEqual(0, await _db.CollectionMemoRanges.CountAsync());
        Assert.AreEqual(second, (await _service.GetTargets()).Single().Id);
        Assert.AreEqual((int) ResponseCode.NotFound, (await _service.CreateRange(first, original)).Code);
        Assert.AreEqual((int) ResponseCode.NotFound, (await _service.UpdateTarget(first, new() {Name = "Lost"})).Code);
        Assert.AreEqual((int) ResponseCode.NotFound, (await _service.DeleteTarget(first)).Code);
    }

    [TestMethod]
    public async Task InvalidRanges_AreRejectedWithoutChangingStoredData()
    {
        var id = await CreateTarget("Target");
        var invalid = new[]
        {
            Range("invalid", "2026-09-01T00:00:00Z"),
            Range("2026-09-01T00:00:00", "2026-09-02T00:00:00Z"),
            Range("2026-02-30T00:00:00Z", "2026-03-01T00:00:00Z"),
            Range("2026-09-02T00:00:00Z", "2026-09-01T00:00:00Z"),
            Range(DateTime.UtcNow.AddDays(1).ToString("O"), DateTime.UtcNow.AddDays(2).ToString("O"))
        };
        foreach (var input in invalid)
            Assert.AreEqual((int) ResponseCode.InvalidPayloadOrOperation, (await _service.CreateRange(id, input)).Code);
        Assert.AreEqual(0, await _db.CollectionMemoRanges.CountAsync());

        await _service.CreateRange(id, Range("2026-09-01T00:00:00Z", "2026-09-02T00:00:00Z"));
        var range = (await _service.GetTargets()).Single().Ranges.Single();
        Assert.AreNotEqual(0, (await _service.UpdateRange(id, range.Id, invalid[3])).Code);
        Assert.AreEqual(range.StartAt, (await _service.GetTargets()).Single().Ranges.Single().StartAt);
    }

    [TestMethod]
    public async Task DatabaseUniqueIndex_RejectsDuplicateNamesAcrossContexts()
    {
        await CreateTarget("Target");
        await using var otherDb = new BakabaseDbContext(_options);
        otherDb.CollectionMemoTargets.Add(new CollectionMemoTargetDbModel {Name = "TARGET", NormalizedName = "TARGET"});
        await Assert.ThrowsExceptionAsync<DbUpdateException>(() => otherDb.SaveChangesAsync());
    }

    [TestMethod]
    public async Task Controller_ExposesMemoDataAndValidationForRemoteClients()
    {
        var controller = new CollectionMemoController(_service);
        Assert.AreEqual(0, (await controller.CreateTarget(new() {Name = "Remote target"})).Code);
        var response = await controller.GetTargets();
        Assert.AreEqual(0, response.Code);
        var id = response.Data!.Single().Id;
        Assert.AreEqual(0, (await controller.CreateRange(id,
            Range("2026-09-01T00:00:00Z", "2026-09-01T00:00:00Z"))).Code);
        Assert.AreEqual(1, (await controller.GetTargets()).Data!.Single().Ranges.Count);
        Assert.AreNotEqual(0, (await controller.CreateTarget(new() {Name = "remote TARGET"})).Code);
        Assert.IsTrue(typeof(CollectionMemoController).GetCustomAttributes(typeof(RemoteAccessibleAttribute), true)
            .Cast<RemoteAccessibleAttribute>().Single().Allowed);
    }

    [TestMethod]
    public async Task Migration_UpgradesExistingDatabaseAndMatchesCurrentModel()
    {
        await _db.Database.EnsureDeletedAsync();
        await _db.DisposeAsync();
        _db = new BakabaseDbContext(_options);
        _service = new CollectionMemoService(_db);
        await _db.GetService<IMigrator>().MigrateAsync("20261001070000_AddDownloadTaskCompletedAt");
        await _db.Database.ExecuteSqlRawAsync(
            "INSERT INTO PostParserTasks (Source, Link, Revision, IsDeleted) VALUES (0, 'https://example.com/old', 1, 0)");

        await _db.Database.MigrateAsync();
        Assert.IsFalse(_db.Database.HasPendingModelChanges());
        Assert.AreEqual(0, await _db.CollectionMemoTargets.CountAsync());
        Assert.AreEqual(0, await _db.CollectionMemoRanges.CountAsync());
        var historicTask = await _db.PostParserTasks.SingleAsync();
        Assert.IsNull(historicTask.CreatedAt);
        Assert.IsNull(historicTask.CompletedAt);
        await CreateTarget("After migration");
        Assert.AreEqual(1, (await _service.GetTargets()).Count);
    }

    private async Task<int> CreateTarget(string name)
    {
        Assert.AreEqual(0, (await _service.CreateTarget(new() {Name = name})).Code);
        return (await _service.GetTargets()).Single(t => t.Name == name).Id;
    }

    private static CollectionMemoRangeInputModel Range(string startAt, string endAt) =>
        new() {StartAt = startAt, EndAt = endAt};
}
