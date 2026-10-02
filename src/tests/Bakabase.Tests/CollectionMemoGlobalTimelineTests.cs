using System.Data.Common;
using Bakabase.InsideWorld.Business;
using Bakabase.InsideWorld.Business.Components.CollectionMemo;
using Bakabase.InsideWorld.Business.Components.CollectionMemo.Models.Db;
using Bakabase.InsideWorld.Business.Components.CollectionMemo.Models.Domain;
using Bakabase.InsideWorld.Business.Components.CollectionMemo.Models.Input;
using Bakabase.Service.Controllers;
using Bootstrap.Models.Constants;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;

namespace Bakabase.Tests;

[TestClass]
public class CollectionMemoGlobalTimelineTests
{
    private string _directory = null!;
    private DbContextOptions<BakabaseDbContext> _options = null!;
    private BakabaseDbContext _db = null!;
    private CollectionMemoService _service = null!;

    [TestInitialize]
    public async Task Initialize()
    {
        _directory = Path.Combine(Path.GetTempPath(), $"BakabaseMemoGlobal_{Guid.NewGuid():N}");
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
    public async Task Settings_InitializeFromEarliestExplicitStartAndRemainFixedAcrossRestart()
    {
        var id = await Target();
        await _service.CreateRange(id, Range(Time(5), Time(6)));
        await _service.CreateRange(id, Range("2026-09-01T08:00:00.1234567+08:00", Time(2)));
        Assert.AreEqual(0, await _db.CollectionMemoSettings.CountAsync(), "Range reads and explicit CRUD do not initialize settings.");

        var settings = await _service.GetSettings();
        Assert.AreEqual(Parse(Time(1, "1234567")), settings.StartAt);
        Assert.AreEqual(DateTimeKind.Utc, settings.StartAt.Kind);
        Assert.IsTrue(settings.Reverse);
        Assert.AreEqual(1, await _db.CollectionMemoSettings.CountAsync());
        StringAssert.Contains(JsonConvert.SerializeObject(settings, new JsonSerializerSettings
        {
            DateFormatString = "yyyy-MM-dd HH:mm:ss.fff"
        }), "2026-09-01T00:00:00.1234567Z");

        await _service.CreateRange(id, Range("2026-08-01T00:00:00Z", Time(2)));
        await _service.DeleteRange(id, (await Ranges(id)).Single(r => r.StartAt == settings.StartAt).Id);
        await Restart();
        Assert.AreEqual(settings.StartAt, (await _service.GetSettings()).StartAt);
        Assert.AreEqual(1, (await _db.CollectionMemoSettings.SingleAsync()).Id);
    }

    [TestMethod]
    public async Task Settings_EmptyDatabasePersistsInitialNowAndDefaultDirection()
    {
        var before = DateTime.UtcNow;
        var initial = await _service.GetSettings();
        Assert.IsTrue(initial.StartAt >= before && initial.StartAt <= DateTime.UtcNow);
        Assert.IsTrue(initial.Reverse);
        await Restart();
        Assert.AreEqual(initial.StartAt, (await _service.GetSettings()).StartAt);
        _db.CollectionMemoSettings.Add(new CollectionMemoSettingsDbModel {Id = 2, StartAt = initial.StartAt});
        await Assert.ThrowsExceptionAsync<DbUpdateException>(() => _db.SaveChangesAsync());
    }

    [TestMethod]
    public async Task SettingsController_PersistsDirectionUtcPrecisionAndRejectsInvalidInputs()
    {
        var controller = new CollectionMemoController(_service);
        Assert.AreEqual(0, (await controller.UpdateSettings(new()
        {
            StartAt = "2026-09-01T08:00:00.7654321+08:00", Reverse = false
        })).Code);
        var response = await controller.GetSettings();
        Assert.AreEqual(0, response.Code);
        Assert.AreEqual(Parse(Time(1, "7654321")), response.Data!.StartAt);
        Assert.IsFalse(response.Data.Reverse);

        foreach (var invalid in new[] {null, "", "invalid", "2026-09-01T00:00:00", "2026-02-30T00:00:00Z", DateTime.UtcNow.AddDays(1).ToString("O")})
            Assert.AreEqual((int) ResponseCode.InvalidPayloadOrOperation,
                (await controller.UpdateSettings(new() {StartAt = invalid!, Reverse = true})).Code);
        await Restart();
        var stored = await _service.GetSettings();
        Assert.AreEqual(response.Data.StartAt, stored.StartAt);
        Assert.IsFalse(stored.Reverse);
    }

    [TestMethod]
    public async Task InheritedRanges_RetainNullAndUseChangedGlobalWithoutBackfillingExplicitHistory()
    {
        await SetStart(Time(3));
        var id = await Target();
        Assert.AreEqual(0, (await _service.CreateRange(id, Range(null, Time(8)))).Code);
        Assert.AreEqual(0, (await _service.CreateRange(id, Range(Time(1, "1234567"), Time(2)))).Code);
        var inherited = (await Ranges(id)).Single(r => r.StartAt == null);
        Assert.AreEqual(0, (await _service.UpdateRange(id, inherited.Id, Range(null, Time(9)))).Code);
        await SetStart(Time(5, "1234567"), false);
        await Restart();

        var ranges = await Ranges(id);
        Assert.IsNull(ranges.Single(r => r.Id == inherited.Id).StartAt);
        Assert.AreEqual(Parse(Time(9)), ranges.Single(r => r.Id == inherited.Id).EndAt);
        Assert.AreEqual(Parse(Time(1, "1234567")), ranges.Single(r => r.Id != inherited.Id).StartAt);
        Assert.AreEqual(Parse(Time(5, "1234567")), (await _service.GetSettings()).StartAt);
        // Create (rather than CreateDefault) ignores process-wide JsonConvert defaults
        // that other suites may configure with camel casing and omitted null values.
        var serializer = JsonSerializer.Create(new JsonSerializerSettings
        {
            ContractResolver = new DefaultContractResolver
            {
                NamingStrategy = new CamelCaseNamingStrategy {ProcessDictionaryKeys = false}
            },
            NullValueHandling = NullValueHandling.Include,
            DateFormatString = "yyyy-MM-dd HH:mm:ss.fff",
            ReferenceLoopHandling = ReferenceLoopHandling.Ignore
        });
        var payload = JArray.FromObject(ranges, serializer);
        var inheritedPayload = payload.Single(r => r["id"]!.Value<int>() == inherited.Id);
        Assert.AreEqual(JTokenType.Null, inheritedPayload["startAt"]?.Type);
        Assert.AreEqual("2026-09-09T00:00:00Z", inheritedPayload["endAt"]!.Value<string>());
        var explicitPayload = payload.Single(r => r["id"]!.Value<int>() != inherited.Id);
        Assert.AreEqual("2026-09-01T00:00:00.1234567Z", explicitPayload["startAt"]!.Value<string>());
    }

    [TestMethod]
    public async Task InheritedValidation_UsesGlobalAndRejectsSettingsPastAnyInheritedEnd()
    {
        await SetStart(Time(3));
        var id = await Target();
        Assert.AreNotEqual(0, (await _service.CreateRange(id, Range(null, Time(2)))).Code);
        Assert.AreNotEqual(0, (await _service.CreateRange(id, Range("", Time(4)))).Code);
        Assert.AreNotEqual(0, (await _service.CreateRange(id, Range(null, DateTime.UtcNow.AddDays(1).ToString("O")))).Code);
        Assert.AreEqual(0, (await _service.CreateRange(id, Range(null, Time(5)))).Code);
        var inherited = (await Ranges(id)).Single();
        Assert.AreNotEqual(0, (await _service.UpdateRange(id, inherited.Id, Range(null, Time(2)))).Code);
        Assert.AreNotEqual(0, (await _service.UpdateSettings(new() {StartAt = Time(6), Reverse = false})).Code);
        Assert.AreEqual(Parse(Time(3)), (await _service.GetSettings()).StartAt);
        Assert.IsTrue((await _service.GetSettings()).Reverse, "Rejected updates leave direction unchanged too.");
        await SetStart(Time(5), false);
        Assert.IsNull((await Ranges(id)).Single().StartAt, "An inherited point still stores null.");
        Assert.AreEqual(0, (await _service.UpdateRange(id, inherited.Id, Range(Time(1), Time(2)))).Code);
        await SetStart(Time(8));
        Assert.AreEqual(Parse(Time(1)), (await Ranges(id)).Single().StartAt);
    }

    [TestMethod]
    public async Task EndResize_ConsolidatesConnectedCoverageAndPreservesDynamicInheritance()
    {
        await SetStart(Time(3, "1234567"));
        var id = await Target();
        await _service.CreateRange(id, Range(null, Time(5)));
        await _service.CreateRange(id, Range(Time(4), Time(7, "7654321")));
        await _service.CreateRange(id, Range(Time(10), Time(12)));
        var original = await Ranges(id);
        var request = Resize(original.Take(2), "end", Time(8), Time(3, "1234567"));
        Assert.AreEqual(0, (await _service.ResizeCoverage(id, request)).Code);
        await Restart();
        var result = await Ranges(id);
        Assert.AreEqual(2, result.Count);
        Assert.IsNull(result[0].StartAt);
        Assert.AreEqual(Parse(Time(8)), result[0].EndAt);
        Assert.AreEqual(original[2].StartAt, result[1].StartAt);
        Assert.AreEqual(original[2].EndAt, result[1].EndAt);
        await SetStart(Time(2));
        Assert.IsNull((await Ranges(id))[0].StartAt);
    }

    [TestMethod]
    public async Task StartResize_MakesTheMovedBoundaryExplicitAtFullPrecision()
    {
        await SetStart(Time(3));
        var id = await Target();
        await _service.CreateRange(id, Range(null, Time(8, "7654321")));
        var selected = await Ranges(id);
        Assert.AreEqual(0, (await _service.ResizeCoverage(id,
            Resize(selected, "start", Time(4, "1234567"), Time(3)))).Code);
        await SetStart(Time(2));
        await Restart();
        Assert.AreEqual(Parse(Time(4, "1234567")), (await Ranges(id)).Single().StartAt);
        Assert.AreEqual(Parse(Time(8, "7654321")), (await Ranges(id)).Single().EndAt);
    }

    [TestMethod]
    public async Task Resize_RejectsMissingStaleOrIncorrectGlobalAndNullableRawSnapshots()
    {
        await SetStart(Time(3, "1234567"));
        var id = await Target();
        await _service.CreateRange(id, Range(null, Time(8)));
        var selected = await Ranges(id);
        Assert.AreEqual((int) ResponseCode.InvalidPayloadOrOperation,
            (await _service.ResizeCoverage(id, Resize(selected, "end", Time(9)))).Code);
        var incorrectRaw = Resize(selected, "end", Time(9), Time(3, "1234567"));
        incorrectRaw.Ranges[0].StartAt = Time(3, "1234567");
        Assert.AreEqual((int) ResponseCode.Conflict, (await _service.ResizeCoverage(id, incorrectRaw)).Code);

        var stale = Resize(selected, "end", Time(9), Time(3, "1234567"));
        await SetStart(Time(3, "1234568"));
        Assert.AreEqual((int) ResponseCode.Conflict, (await _service.ResizeCoverage(id, stale)).Code);
        Assert.IsNull((await Ranges(id)).Single().StartAt);
        Assert.AreEqual(selected[0].EndAt, (await Ranges(id)).Single().EndAt);
        Assert.AreEqual(0, (await _service.ResizeCoverage(id,
            Resize(selected, "end", Time(9), "2026-09-03T08:00:00.1234568+08:00"))).Code);
    }

    [TestMethod]
    public async Task Fill_PreservesInheritedStartAndLeavesDisconnectedCoverageUnchanged()
    {
        await SetStart(Time(3, "1234567"));
        var id = await Target();
        await _service.CreateRange(id, Range(null, Time(5)));
        await _service.CreateRange(id, Range(Time(4), Time(7)));
        await _service.CreateRange(id, Range(Time(10, "1234567"), Time(12, "7654321")));
        var unrelated = (await Ranges(id))[2];
        Assert.AreEqual(0, (await _service.FillGap(id, Range(Time(7), Time(9)))).Code);
        await Restart();
        var result = await Ranges(id);
        Assert.AreEqual(2, result.Count);
        Assert.IsNull(result[0].StartAt);
        Assert.AreEqual(Parse(Time(9)), result[0].EndAt);
        Assert.AreEqual(unrelated.StartAt, result[1].StartAt);
        Assert.AreEqual(unrelated.EndAt, result[1].EndAt);
        await SetStart(Time(2));
        Assert.IsNull((await Ranges(id))[0].StartAt);
    }

    [TestMethod]
    public async Task Fill_NullStartCreatesInheritedCoverageButEarlierExplicitHistoryRemainsExplicit()
    {
        await SetStart(Time(3));
        var id = await Target();
        Assert.AreEqual(0, (await _service.FillGap(id, Range(null, Time(5)))).Code);
        Assert.IsNull((await Ranges(id)).Single().StartAt);
        Assert.AreNotEqual(0, (await _service.FillGap(id, Range(null, Time(2)))).Code);
        await _service.CreateRange(id, Range(Time(1, "1234567"), Time(4)));
        Assert.AreEqual(0, (await _service.FillGap(id, Range(Time(5), Time(6)))).Code);
        Assert.AreEqual(Parse(Time(1, "1234567")), (await Ranges(id)).Single().StartAt);
    }

    [TestMethod]
    public async Task SettingsTransaction_SerializesConcurrentInheritedRangeUpdate()
    {
        await SetStart(Time(1));
        var id = await Target();
        await _service.CreateRange(id, Range(null, Time(10)));
        var rangeId = (await Ranges(id)).Single().Id;
        var pause = new PauseInheritedValidation();
        await using var settingsDb = new BakabaseDbContext(new DbContextOptionsBuilder<BakabaseDbContext>(_options)
            .AddInterceptors(pause).Options);
        await using var otherDb = new BakabaseDbContext(_options);
        var settingsChange = new CollectionMemoService(settingsDb).UpdateSettings(new() {StartAt = Time(5)});
        await pause.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var updateStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var rangeChange = Task.Run(async () =>
        {
            updateStarted.SetResult();
            return await new CollectionMemoService(otherDb).UpdateRange(id, rangeId, Range(null, Time(3)));
        });
        await updateStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
        try
        {
            await Task.Delay(100);
            Assert.IsFalse(rangeChange.IsCompleted, "The competing writer must wait for the settings transaction.");
        }
        finally
        {
            pause.Resume.TrySetResult();
        }
        Assert.AreEqual(0, (await settingsChange.WaitAsync(TimeSpan.FromSeconds(10))).Code);
        Assert.AreEqual((int) ResponseCode.InvalidPayloadOrOperation,
            (await rangeChange.WaitAsync(TimeSpan.FromSeconds(10))).Code);
        Assert.AreEqual(Parse(Time(5)), (await _service.GetSettings()).StartAt);
        Assert.IsNull((await Ranges(id)).Single().StartAt);
        Assert.AreEqual(Parse(Time(10)), (await Ranges(id)).Single().EndAt);
    }

    [TestMethod]
    public async Task Migration_UpgradesExistingExplicitRangesWithoutBackfillOrPrecisionLoss()
    {
        await _db.Database.EnsureDeletedAsync();
        await Restart();
        await _db.GetService<IMigrator>().MigrateAsync("20261001151353_AddCollectionMemoAndPostParserTaskTimestamps");
        await _db.Database.ExecuteSqlRawAsync("INSERT INTO CollectionMemoTargets (Id, Name, NormalizedName) VALUES (1, 'Legacy', 'LEGACY')");
        var start = Parse(Time(1, "1234567"));
        var end = Parse(Time(2, "7654321"));
        await _db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO CollectionMemoRanges (Id, TargetId, StartAt, EndAt) VALUES (1, 1, {start}, {end})");
        await _db.Database.MigrateAsync();
        Assert.IsFalse(_db.Database.HasPendingModelChanges());
        Assert.AreEqual(0, await _db.CollectionMemoSettings.CountAsync(), "The generated migration contains schema only.");
        var legacy = (await Ranges(1)).Single();
        Assert.AreEqual(start, legacy.StartAt);
        Assert.AreEqual(end, legacy.EndAt);
        Assert.AreEqual(DateTimeKind.Utc, legacy.StartAt!.Value.Kind);
        Assert.AreEqual(start, (await _service.GetSettings()).StartAt);
        Assert.IsTrue((await _service.GetSettings()).Reverse);
        Assert.AreEqual(0, (await _service.CreateRange(1, Range(null, end.ToString("O")))).Code);
        await Restart();
        Assert.AreEqual(1, (await Ranges(1)).Count(r => r.StartAt == null));
    }

    private sealed class PauseInheritedValidation : DbCommandInterceptor
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Resume { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("CollectionMemoRanges") && command.CommandText.Contains("IS NULL"))
            {
                Entered.TrySetResult();
                await Resume.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
            }
            return await base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }

    private async Task SetStart(string start, bool reverse = true) =>
        Assert.AreEqual(0, (await _service.UpdateSettings(new() {StartAt = start, Reverse = reverse})).Code);

    private async Task<int> Target()
    {
        Assert.AreEqual(0, (await _service.CreateTarget(new() {Name = "Target"})).Code);
        return (await _service.GetTargets()).Single().Id;
    }

    private async Task<List<CollectionMemoRange>> Ranges(int id) =>
        (await _service.GetTargets()).Single(t => t.Id == id).Ranges;

    private async Task Restart()
    {
        await _db.DisposeAsync();
        _db = new BakabaseDbContext(_options);
        _service = new CollectionMemoService(_db);
    }

    private static CollectionMemoCoverageResizeInputModel Resize(IEnumerable<CollectionMemoRange> ranges,
        string edge, string at, string? globalStart = null) => new()
    {
        Ranges = ranges.Select(r => new CollectionMemoRangeSnapshotInputModel
        {
            Id = r.Id, StartAt = r.StartAt?.ToString("O"), EndAt = r.EndAt.ToString("O")
        }).ToList(),
        Edge = edge,
        At = at,
        ExpectedGlobalStartAt = globalStart
    };

    private static CollectionMemoRangeInputModel Range(string? start, string end) => new() {StartAt = start, EndAt = end};
    private static string Time(int day, string fraction = "0000000") => $"2026-09-{day:D2}T00:00:00.{fraction}Z";
    private static DateTime Parse(string value) => DateTimeOffset.Parse(value).UtcDateTime;
}
