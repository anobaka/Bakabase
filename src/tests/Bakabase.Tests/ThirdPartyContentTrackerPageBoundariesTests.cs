using System;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Bakabase.Abstractions.Models.Db;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Services;
using Bakabase.InsideWorld.Business;
using Bakabase.Modules.RemoteAccess.Abstractions.Models;
using Bakabase.Service.Controllers;
using Bakabase.TestKit.Utils;
using Bakabase.Tests.RemoteAccess.Service;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Tests;

[TestClass]
public sealed class ThirdPartyContentTrackerPageBoundariesTests
{
    private static readonly DateTime PageOldest = new(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime PageNewest = PageOldest.AddDays(1);
    private IServiceProvider _provider = null!;
    private BakabaseDbContext _db = null!;
    private IThirdPartyContentTrackerService _service = null!;

    [TestInitialize]
    public async Task Setup()
    {
        _provider = await TestServiceBuilder.BuildServiceProvider();
        _db = _provider.GetRequiredService<BakabaseDbContext>();
        _service = _provider.GetRequiredService<IThirdPartyContentTrackerService>();
        Assert.AreEqual("Microsoft.EntityFrameworkCore.Sqlite", _db.Database.ProviderName);
    }

    [TestCleanup]
    public async Task Cleanup()
    {
        if (_provider is IAsyncDisposable disposable) await disposable.DisposeAsync();
    }

    private static ThirdPartyContentTrackerDbModel Record(string id, DateTime? updatedAt,
        string? filter = null, string domain = "exhentai", DateTime? viewedAt = null) => new()
    {
        DomainKey = domain, Filter = filter, ContentId = id, UpdatedAt = updatedAt,
        ViewedAt = viewedAt ?? PageNewest.AddYears(1), CreatedAt = PageOldest.AddYears(-1)
    };

    private async Task Seed(params ThirdPartyContentTrackerDbModel[] records)
    {
        _db.ThirdPartyContentTrackers.AddRange(records);
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
    }

    private static string UtcIso(DateTime value) => value.ToString("O", CultureInfo.InvariantCulture);

    [TestMethod]
    public async Task BoundariesUseContentTimeRatherThanIdOrLastViewedTime()
    {
        await Seed(
            Record("999999", PageOldest.AddDays(-30), viewedAt: PageNewest.AddYears(2)),
            Record("1", PageOldest.AddHours(-2), viewedAt: PageOldest.AddYears(-2)),
            Record("2", PageNewest.AddHours(3), viewedAt: PageOldest.AddYears(-3)),
            Record("999998", PageNewest.AddDays(50), viewedAt: PageNewest.AddYears(3)),
            Record("within-page-times", PageOldest.AddHours(12)),
            Record("without-content-time", null));

        var result = await _service.FindPageBoundaries("exhentai", null, PageOldest, PageNewest, []);
        Assert.AreEqual("1", result.Older!.ContentId);
        Assert.AreEqual(UtcIso(PageOldest.AddHours(-2)), result.Older.UpdatedAt);
        Assert.AreEqual("2", result.Newer!.ContentId);
        Assert.AreEqual(UtcIso(PageNewest.AddHours(3)), result.Newer.UpdatedAt);
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("tag=a")]
    public async Task DomainAndExactFilterAreIsolatedIncludingNullAndEmpty(string? filter)
    {
        foreach (var scope in new string?[] {null, "", "tag=a"})
        {
            var label = scope == null ? "null" : scope == "" ? "empty" : "filtered";
            await Seed(Record(label + "-older", PageOldest.AddHours(-1), scope),
                Record(label + "-newer", PageNewest.AddHours(1), scope));
        }
        await Seed(Record("wrong-domain-older", PageOldest, filter, "soulplus"),
            Record("wrong-domain-newer", PageNewest, filter, "soulplus"));

        var result = await _service.FindPageBoundaries("exhentai", filter, PageOldest, PageNewest, []);
        var expected = filter == null ? "null" : filter == "" ? "empty" : "filtered";
        Assert.AreEqual(expected + "-older", result.Older!.ContentId);
        Assert.AreEqual(expected + "-newer", result.Newer!.ContentId);
    }

    [TestMethod]
    public async Task EqualBoundaryTimesAreIncludedAndTiesChooseADeterministicContentId()
    {
        await Seed(Record("b-old", PageOldest), Record("a-old", PageOldest),
            Record("b-new", PageNewest), Record("a-new", PageNewest),
            Record("current-old", PageOldest), Record("current-new", PageNewest));

        var result = await _service.FindPageBoundaries("exhentai", null, PageOldest, PageNewest,
            ["current-new", "current-old", "current-old"]);
        Assert.AreEqual("a-old", result.Older!.ContentId);
        Assert.AreEqual(UtcIso(PageOldest), result.Older.UpdatedAt);
        Assert.AreEqual("a-new", result.Newer!.ContentId);
        Assert.AreEqual(UtcIso(PageNewest), result.Newer.UpdatedAt);
    }

    [TestMethod]
    public async Task APageWithOneContentTimeCanFindBothDirectionsAtThatTime()
    {
        await Seed(Record("past", PageOldest.AddDays(-1)), Record("tie", PageOldest),
            Record("future", PageOldest.AddDays(1)), Record("current", PageOldest));
        var result = await _service.FindPageBoundaries("exhentai", null, PageOldest, PageOldest, ["current"]);
        Assert.AreEqual("tie", result.Older!.ContentId);
        Assert.AreEqual("tie", result.Newer!.ContentId);
    }

    [TestMethod]
    public async Task SparseHistoryReturnsNullForAMissingDirectionWithoutFallingBackToLastViewed()
    {
        var empty = await _service.FindPageBoundaries("exhentai", null, PageOldest, PageNewest, []);
        Assert.IsNull(empty.Older);
        Assert.IsNull(empty.Newer);

        await Seed(Record("unknown-time", null), Record("in-range", PageOldest.AddHours(1)),
            Record("older", PageOldest.AddDays(-1)));
        var olderOnly = await _service.FindPageBoundaries("exhentai", null, PageOldest, PageNewest, []);
        Assert.AreEqual("older", olderOnly.Older!.ContentId);
        Assert.IsNull(olderOnly.Newer);

        var excluded = await _service.FindPageBoundaries("exhentai", null, PageOldest, PageNewest, ["older"]);
        Assert.IsNull(excluded.Older);
        Assert.IsNull(excluded.Newer);
    }

    [TestMethod]
    public async Task MarkingEveryCurrentPageItemRepeatedlyCannotTurnOutsideDistancesIntoZero()
    {
        await Seed(Record("previous-older", PageOldest.AddDays(-5)),
            Record("previous-newer", PageNewest.AddDays(6)));
        string[] pageIds = ["current-oldest", "current-middle", "current-newest"];
        var before = await _service.FindPageBoundaries("exhentai", null, PageOldest, PageNewest, pageIds);
        for (var i = 0; i < 2; i++)
        {
            await _service.MarkContentAsViewed("exhentai", null,
                [(pageIds[0], PageOldest), (pageIds[1], PageOldest.AddHours(12)), (pageIds[2], PageNewest)]);
            var after = await _service.FindPageBoundaries("exhentai", null, PageOldest, PageNewest, pageIds);
            Assert.AreEqual(before.Older, after.Older);
            Assert.AreEqual(before.Newer, after.Newer);
        }
    }

    [TestMethod]
    public async Task QueryDoesNotTrackOrChangeExistingRecordsAndReturnsUtcAfterSqliteRoundtrip()
    {
        await Seed(Record("older", PageOldest.AddMinutes(-1)), Record("newer", PageNewest.AddMinutes(1)));
        var before = JsonSerializer.Serialize(await _db.ThirdPartyContentTrackers.AsNoTracking().OrderBy(t => t.Id).ToArrayAsync());
        Assert.AreEqual(DateTimeKind.Unspecified,
            (await _db.ThirdPartyContentTrackers.AsNoTracking().FirstAsync()).UpdatedAt!.Value.Kind);

        var result = await _service.FindPageBoundaries("exhentai", null, PageOldest, PageNewest, []);
        Assert.AreEqual(DateTimeKind.Utc, DateTime.Parse(result.Older!.UpdatedAt, CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind).Kind);
        Assert.AreEqual(DateTimeKind.Utc, DateTime.Parse(result.Newer!.UpdatedAt, CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind).Kind);
        Assert.AreEqual(0, _db.ChangeTracker.Entries().Count());
        Assert.AreEqual(before, JsonSerializer.Serialize(await _db.ThirdPartyContentTrackers.AsNoTracking().OrderBy(t => t.Id).ToArrayAsync()));
        var json = JsonSerializer.Serialize(result, JsonSerializerOptions.Web);
        StringAssert.Contains(json, "2026-09-30T23:59:00.0000000Z");
        StringAssert.Contains(json, "2026-10-02T00:01:00.0000000Z");
        Assert.IsFalse(json.Contains("viewedAt", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task ProductionDisplayDateFormatCannotStripTheBoundaryUtcMarker()
    {
        await Seed(Record("older", PageOldest.AddMinutes(-1)), Record("newer", PageNewest.AddMinutes(1)));
        var result = await _service.FindPageBoundaries("exhentai", null, PageOldest, PageNewest, []);
        var settings = new Newtonsoft.Json.JsonSerializerSettings
        {
            // The actual AppStartup HTTP serializer formats DateTime this way.
            DateFormatString = "yyyy-MM-dd HH:mm:ss.fff",
            ContractResolver = new Newtonsoft.Json.Serialization.CamelCasePropertyNamesContractResolver()
        };
        Assert.AreEqual("\"2026-10-01 00:00:00.000\"", Newtonsoft.Json.JsonConvert.SerializeObject(PageOldest, settings));
        using var json = JsonDocument.Parse(Newtonsoft.Json.JsonConvert.SerializeObject(result, settings));
        Assert.AreEqual("2026-09-30T23:59:00.0000000Z", json.RootElement.GetProperty("older").GetProperty("updatedAt").GetString());
        Assert.AreEqual("2026-10-02T00:01:00.0000000Z", json.RootElement.GetProperty("newer").GetProperty("updatedAt").GetString());
    }

    [TestMethod]
    public async Task ServiceRejectsNonUtcAndReversedBoundariesBeforeQuerying()
    {
        await Assert.ThrowsExceptionAsync<ArgumentException>(() => _service.FindPageBoundaries("exhentai", null,
            DateTime.SpecifyKind(PageOldest, DateTimeKind.Unspecified), PageNewest, []));
        await Assert.ThrowsExceptionAsync<ArgumentException>(() => _service.FindPageBoundaries("exhentai", null,
            PageOldest, DateTime.SpecifyKind(PageNewest, DateTimeKind.Local), []));
        await Assert.ThrowsExceptionAsync<ArgumentException>(() => _service.FindPageBoundaries("exhentai", null,
            PageNewest, PageOldest, []));
        Assert.AreEqual(0, _db.ChangeTracker.Entries().Count());
    }

    private Task<ServiceGateHost> StartHost() => ServiceGateHost.StartAsync([typeof(ThirdPartyContentTrackerController)],
        services => services.AddSingleton(_service));

    [TestMethod]
    public async Task HttpEndpointReturnsExplicitUtcContentTimesAndNoViewingTimes()
    {
        await Seed(Record("older", PageOldest.AddMinutes(-1)), Record("newer", PageNewest.AddMinutes(1)),
            Record("current", PageOldest));
        await using var host = await StartHost();
        host.Remote.Mode = RemoteAccessMode.Unrestricted;
        using var request = host.Request(HttpMethod.Post, "/third-party-content-tracker/page-boundaries");
        request.Headers.Add(ServiceGateHost.RemoteIpHeader, "192.168.3.100");
        request.Content = new StringContent("""
            {"domainKey":"exhentai","filter":null,"oldestUpdatedAt":"2026-10-01T00:00:00Z","newestUpdatedAt":"2026-10-02T00:00:00Z","excludeContentIds":["current"]}
            """, Encoding.UTF8, "application/json");
        using var response = await host.SendAsync(request);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var data = json.RootElement.GetProperty("data");
        Assert.AreEqual("older", data.GetProperty("older").GetProperty("contentId").GetString());
        Assert.AreEqual("2026-09-30T23:59:00.0000000Z", data.GetProperty("older").GetProperty("updatedAt").GetString());
        Assert.AreEqual("2026-10-02T00:01:00.0000000Z", data.GetProperty("newer").GetProperty("updatedAt").GetString());
        Assert.AreEqual(2, data.GetProperty("older").EnumerateObject().Count());
    }

    [TestMethod]
    [DataRow("2026-10-02T00:00:00Z", "2026-10-01T00:00:00Z")]
    [DataRow("2026-10-01T00:00:00", "2026-10-02T00:00:00Z")]
    [DataRow("2026-10-01T00:00:00Z", "2026-10-02T00:00:00+08:00")]
    [DataRow("invalid", "2026-10-02T00:00:00Z")]
    public async Task HttpEndpointRejectsReversedUnspecifiedNonUtcOrInvalidTimes(string oldest, string newest)
    {
        await using var host = await StartHost();
        using var request = host.Request(HttpMethod.Post, "/third-party-content-tracker/page-boundaries");
        request.Content = new StringContent(JsonSerializer.Serialize(new
            {domainKey = "exhentai", oldestUpdatedAt = oldest, newestUpdatedAt = newest, excludeContentIds = Array.Empty<string>()}),
            Encoding.UTF8, "application/json");
        using var response = await host.SendAsync(request);
        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.AreEqual(0, await _db.ThirdPartyContentTrackers.CountAsync());
    }

    [TestMethod]
    [DataRow("null")]
    [DataRow("{}")]
    [DataRow("{\"domainKey\":\"exhentai\",\"oldestUpdatedAt\":\"2026-10-01T00:00:00Z\",\"newestUpdatedAt\":\"2026-10-02T00:00:00Z\",\"excludeContentIds\":[\"\"]}")]
    public async Task HttpEndpointRejectsMissingRequiredValuesAndInvalidExcludedIds(string body)
    {
        await using var host = await StartHost();
        using var request = host.Request(HttpMethod.Post, "/third-party-content-tracker/page-boundaries");
        request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        using var response = await host.SendAsync(request);
        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.AreEqual(0, await _db.ThirdPartyContentTrackers.CountAsync());
    }
}
