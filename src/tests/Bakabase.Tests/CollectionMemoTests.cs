using Bakabase.InsideWorld.Business;
using Bakabase.InsideWorld.Business.Components.CollectionMemo;
using Bakabase.InsideWorld.Business.Components.CollectionMemo.Models.Db;
using Bakabase.InsideWorld.Business.Components.CollectionMemo.Models.Domain;
using Bakabase.InsideWorld.Business.Components.CollectionMemo.Models.Input;
using Bakabase.Service.Components.RemoteAccess;
using Bakabase.Service.Controllers;
using Bootstrap.Models.Constants;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Newtonsoft.Json;
using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

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
        Assert.AreEqual(DateTimeKind.Utc, range.StartAt!.Value.Kind);
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
        CollectionAssert.AreEqual(new[] {1, 2, 5}, ranges.Select(r => r.StartAt!.Value.Day).ToArray());
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
        Assert.AreEqual(3, (await _service.GetTargets()).Single(t => t.Id == first).Ranges.Single().StartAt!.Value.Day);
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

    [TestMethod]
    public async Task FillGap_MergesTransitiveTouchingCoverageAndPersistsOtherComponents()
    {
        var id = await CreateTarget("Fill");
        await _service.CreateRange(id, Range(Time(1), Time(5)));
        await _service.CreateRange(id, Range(Time(4), Time(15)));
        await _service.CreateRange(id, Range(Time(22), Time(25)));
        var original = await Ranges(id);
        var other = original.Single(r => r.StartAt!.Value.Day == 22);

        Assert.AreEqual(0, (await _service.FillGap(id, Range(Time(15), Time(20)))).Code);
        await RestartContext();
        var result = await Ranges(id);
        Assert.AreEqual(2, result.Count);
        Assert.AreEqual(original.Min(r => r.Id), result[0].Id);
        Assert.AreEqual(DateTime.Parse(Time(1)).ToUniversalTime(), result[0].StartAt);
        Assert.AreEqual(DateTime.Parse(Time(20)).ToUniversalTime(), result[0].EndAt);
        AssertRangeEqual(other, result[1]);
        Assert.AreEqual(DateTimeKind.Utc, result[0].StartAt!.Value.Kind);
    }

    [DataTestMethod]
    [DataRow("leading", 2, 5, 2, 6)]
    [DataRow("trailing", 6, 8, 5, 8)]
    public async Task FillGap_HandlesOneNeighbor(string name, int fillStart, int fillEnd, int resultStart, int resultEnd)
    {
        var id = await CreateTarget(name);
        await _service.CreateRange(id, Range(Time(5), Time(6)));
        Assert.AreEqual(0, (await _service.FillGap(id, Range(Time(fillStart), Time(fillEnd)))).Code);
        var result = (await Ranges(id)).Single();
        Assert.AreEqual(resultStart, result.StartAt!.Value.Day);
        Assert.AreEqual(resultEnd, result.EndAt.Day);
    }

    [TestMethod]
    public async Task FillGap_HandlesEmptyAndDisconnectedCoverageWithoutChangingExistingRecords()
    {
        var id = await CreateTarget("Empty");
        Assert.AreEqual(0, (await _service.FillGap(id, Range(Time(5), Time(6)))).Code);
        var existing = (await Ranges(id)).Single();
        Assert.AreEqual(0, (await _service.FillGap(id, Range(Time(1), Time(2)))).Code);
        var result = await Ranges(id);
        Assert.AreEqual(2, result.Count);
        AssertRangeEqual(existing, result[1]);
    }

    [DataTestMethod]
    [DataRow("start")]
    [DataRow("end")]
    public async Task ResizeCoverage_ConsolidatesOnlySelectedComponentAndPreservesExactOtherBoundary(string edge)
    {
        var id = await CreateTarget("Resize");
        await _service.CreateRange(id, Range(Time(1, "1234567"), Time(2, "1234567")));
        await _service.CreateRange(id, Range(Time(5, "1234567"), Time(6, "7654321")));
        await _service.CreateRange(id, Range(Time(6), Time(8, "7654321")));
        await _service.CreateRange(id, Range(Time(10, "1234567"), Time(11, "1234567")));
        var original = await Ranges(id);
        var selected = original.Where(r => r.StartAt!.Value.Day is 5 or 6).ToList();
        var at = edge == "start" ? Time(4, "9876543") : Time(9, "9876543");

        Assert.AreEqual(0, (await _service.ResizeCoverage(id, Resize(selected, edge, at))).Code);
        await RestartContext();
        var result = await Ranges(id);
        Assert.AreEqual(3, result.Count);
        AssertRangeEqual(original[0], result[0]);
        AssertRangeEqual(original[3], result[2]);
        Assert.AreEqual(selected.Min(r => r.Id), result[1].Id);
        Assert.AreEqual(edge == "start" ? DateTime.Parse(at).ToUniversalTime() : selected.Min(r => r.StartAt), result[1].StartAt);
        Assert.AreEqual(edge == "end" ? DateTime.Parse(at).ToUniversalTime() : selected.Max(r => r.EndAt), result[1].EndAt);
    }

    [TestMethod]
    public async Task ResizeCoverage_RejectsStaleIncompleteForeignDuplicateAndDisconnectedSnapshots()
    {
        var id = await CreateTarget("Snapshots");
        var foreignId = await CreateTarget("Other");
        await _service.CreateRange(id, Range(Time(1), Time(3)));
        await _service.CreateRange(id, Range(Time(3), Time(5)));
        await _service.CreateRange(id, Range(Time(10), Time(12)));
        await _service.CreateRange(foreignId, Range(Time(1), Time(2)));
        var original = await Ranges(id);
        var component = original.Take(2).ToList();

        Assert.AreEqual((int) ResponseCode.Conflict,
            (await _service.ResizeCoverage(id, Resize(component.Take(1), "end", Time(6)))).Code);
        Assert.AreEqual((int) ResponseCode.Conflict,
            (await _service.ResizeCoverage(id, Resize(original, "end", Time(13)))).Code);
        Assert.AreEqual((int) ResponseCode.Conflict,
            (await _service.ResizeCoverage(id, Resize(await Ranges(foreignId), "end", Time(6)))).Code);
        Assert.AreEqual((int) ResponseCode.InvalidPayloadOrOperation,
            (await _service.ResizeCoverage(id, Resize([component[0], component[0]], "end", Time(6)))).Code);
        var stale = Resize(component, "end", Time(6));
        await _service.UpdateRange(id, component[0].Id, Range(Time(1), Time(4)));
        var edited = await Ranges(id);
        Assert.AreEqual((int) ResponseCode.Conflict, (await _service.ResizeCoverage(id, stale)).Code);
        AssertRangesEqual(edited, await Ranges(id));
    }

    [TestMethod]
    public async Task ResizeCoverage_RejectsNewlyConnectedRowsButAcceptsUnrelatedNewRows()
    {
        var id = await CreateTarget("Connections");
        await _service.CreateRange(id, Range(Time(5), Time(8)));
        var selected = await Ranges(id);
        await _service.CreateRange(id, Range(Time(10), Time(12)));
        Assert.AreEqual(0, (await _service.ResizeCoverage(id, Resize(selected, "start", Time(4)))).Code);

        selected = (await Ranges(id)).Where(r => r.StartAt!.Value.Day == 4).ToList();
        await _service.CreateRange(id, Range(Time(7), Time(9)));
        var current = await Ranges(id);
        Assert.AreEqual((int) ResponseCode.Conflict,
            (await _service.ResizeCoverage(id, Resize(selected, "end", Time(9)))).Code);
        AssertRangesEqual(current, await Ranges(id));
    }

    [TestMethod]
    public async Task ResizeCoverage_RechecksCurrentNeighborAndAllowsTouchingWithoutMergingIt()
    {
        var id = await CreateTarget("Neighbors");
        await _service.CreateRange(id, Range(Time(10), Time(20)));
        await _service.CreateRange(id, Range(Time(28), Time(30)));
        var original = await Ranges(id);
        var input = Resize(original.Take(1), "end", Time(27));
        await _service.UpdateRange(id, original[1].Id, Range(Time(25), Time(30)));
        var current = await Ranges(id);
        Assert.AreEqual((int) ResponseCode.InvalidPayloadOrOperation, (await _service.ResizeCoverage(id, input)).Code);
        AssertRangesEqual(current, await Ranges(id));

        Assert.AreEqual(0, (await _service.ResizeCoverage(id, Resize(current.Take(1), "end", Time(25)))).Code);
        var touching = await Ranges(id);
        Assert.AreEqual(2, touching.Count);
        AssertRangeEqual(current[1], touching[1]);
        Assert.AreEqual(25, touching[0].EndAt.Day);
    }

    [TestMethod]
    public async Task CoverageOperations_PreserveOneTickGapsUntilExplicitlyFilled()
    {
        var id = await CreateTarget("Ticks");
        await _service.CreateRange(id, Range(Time(1, "1234560"), Time(1, "1234567")));
        await _service.CreateRange(id, Range(Time(1, "1234568"), Time(1, "1234570")));
        var original = await Ranges(id);
        Assert.AreEqual(1L, original[1].StartAt!.Value.Ticks - original[0].EndAt.Ticks);
        Assert.AreEqual(0, (await _service.ResizeCoverage(id,
            Resize(original.Take(1), "start", Time(1, "1234561")))).Code);
        var resized = await Ranges(id);
        Assert.AreEqual(2, resized.Count);
        AssertRangeEqual(original[1], resized[1]);
        Assert.AreEqual(original[0].EndAt.Ticks, resized[0].EndAt.Ticks);

        Assert.AreEqual(0, (await _service.FillGap(id, Range(Time(1, "1234567"), Time(1, "1234568")))).Code);
        var merged = (await Ranges(id)).Single();
        Assert.AreEqual(resized[0].StartAt!.Value.Ticks, merged.StartAt!.Value.Ticks);
        Assert.AreEqual(original[1].EndAt.Ticks, merged.EndAt.Ticks);
    }

    [DataTestMethod]
    [DataRow("fill")]
    [DataRow("resize")]
    public async Task CoverageOperations_RollBackBoundaryUpdateIfConsolidationDeletionFails(string operation)
    {
        var id = await CreateTarget("Atomic");
        await _service.CreateRange(id, Range(Time(1), Time(3)));
        await _service.CreateRange(id, Range(Time(3), Time(5)));
        var original = await Ranges(id);
        await using (var failingDb = new BakabaseDbContext(new DbContextOptionsBuilder<BakabaseDbContext>(_options)
                         .AddInterceptors(new FailConsolidationDelete()).Options))
        {
            var service = new CollectionMemoService(failingDb);
            await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => operation == "fill"
                ? service.FillGap(id, Range(Time(5), Time(8)))
                : service.ResizeCoverage(id, Resize(original, "end", Time(8))));
        }
        await RestartContext();
        AssertRangesEqual(original, await Ranges(id));
        Assert.AreEqual(0, (await _service.FillGap(id, Range(Time(5), Time(8)))).Code);
        Assert.AreEqual(8, (await Ranges(id)).Single().EndAt.Day);
    }

    [TestMethod]
    public async Task CoverageController_ValidatesBoundaryInputsAndMissingTargets()
    {
        var controller = new CollectionMemoController(_service);
        var id = await CreateTarget("API");
        Assert.AreEqual(0, (await controller.FillGap(id, Range(Time(5), Time(8)))).Code);
        var original = await Ranges(id);
        Assert.AreEqual(0, (await controller.ResizeCoverage(id, Resize(original, "start", Time(4)))).Code);
        var current = await Ranges(id);
        Assert.AreEqual((int) ResponseCode.InvalidPayloadOrOperation,
            (await controller.FillGap(id, Range(Time(9), Time(8)))).Code);
        Assert.AreEqual((int) ResponseCode.InvalidPayloadOrOperation,
            (await controller.ResizeCoverage(id, Resize(current, "middle", Time(5)))).Code);
        Assert.AreEqual((int) ResponseCode.InvalidPayloadOrOperation,
            (await controller.ResizeCoverage(id, Resize(current, "start", Time(9)))).Code);
        Assert.AreEqual((int) ResponseCode.InvalidPayloadOrOperation,
            (await controller.ResizeCoverage(id, Resize(current, "end", DateTime.UtcNow.AddDays(1).ToString("O")))).Code);
        Assert.AreEqual((int) ResponseCode.NotFound,
            (await controller.FillGap(int.MaxValue, Range(Time(1), Time(2)))).Code);
        Assert.AreEqual((int) ResponseCode.NotFound,
            (await controller.ResizeCoverage(int.MaxValue, Resize(current, "end", Time(9)))).Code);
        AssertRangesEqual(current, await Ranges(id));
    }

    private async Task<List<CollectionMemoRange>> Ranges(int targetId) =>
        (await _service.GetTargets()).Single(t => t.Id == targetId).Ranges;

    private async Task RestartContext()
    {
        await _db.DisposeAsync();
        _db = new BakabaseDbContext(_options);
        _service = new CollectionMemoService(_db);
    }

    private static CollectionMemoCoverageResizeInputModel Resize(IEnumerable<CollectionMemoRange> ranges,
        string edge, string at) => new()
    {
        Ranges = ranges.Select(r => new CollectionMemoRangeSnapshotInputModel
        {
            Id = r.Id, StartAt = r.StartAt?.ToString("O"), EndAt = r.EndAt.ToString("O")
        }).ToList(),
        Edge = edge,
        At = at
    };

    private static string Time(int day, string fraction = "0000000") => $"2026-09-{day:D2}T00:00:00.{fraction}Z";

    private static void AssertRangeEqual(CollectionMemoRange expected, CollectionMemoRange actual)
    {
        Assert.AreEqual(expected.Id, actual.Id);
        Assert.AreEqual(expected.StartAt, actual.StartAt);
        Assert.AreEqual(expected.EndAt.Ticks, actual.EndAt.Ticks);
    }

    private static void AssertRangesEqual(List<CollectionMemoRange> expected, List<CollectionMemoRange> actual)
    {
        Assert.AreEqual(expected.Count, actual.Count);
        foreach (var range in expected) AssertRangeEqual(range, actual.Single(r => r.Id == range.Id));
    }

    private sealed class FailConsolidationDelete : DbCommandInterceptor
    {
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.StartsWith("DELETE FROM \"CollectionMemoRanges\"", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Test consolidation deletion failed.");
            return base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
        }
    }

    private async Task<int> CreateTarget(string name)
    {
        Assert.AreEqual(0, (await _service.CreateTarget(new() {Name = name})).Code);
        return (await _service.GetTargets()).Single(t => t.Name == name).Id;
    }

    private static CollectionMemoRangeInputModel Range(string? startAt, string endAt) =>
        new() {StartAt = startAt, EndAt = endAt};
}
