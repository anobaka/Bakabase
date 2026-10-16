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
using Bakabase.Service.Controllers;
using Bakabase.TestKit.Utils;
using Bakabase.Tests.RemoteAccess.Service;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Tests;

[TestClass]
public sealed class ThirdPartyContentTrackerDailyHistoryTests
{
    private static readonly DateTime CenterUtc = new(2026, 10, 16, 0, 0, 0, DateTimeKind.Utc);
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
        string? filter = null, string domain = "exhentai") => new()
    {
        DomainKey = domain, Filter = filter, ContentId = id, UpdatedAt = updatedAt,
        ViewedAt = CenterUtc.AddYears(1), CreatedAt = CenterUtc.AddYears(-1)
    };

    private async Task Seed(params ThirdPartyContentTrackerDbModel[] records)
    {
        _db.ThirdPartyContentTrackers.AddRange(records);
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
    }

    private static DateTime Utc(string iso) => DateTime.Parse(iso, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

    [TestMethod]
    public async Task EmptyHistoryStillHasExactly31ConsecutiveCalendarDaysAcrossYearBoundary()
    {
        var result = await _service.GetDailyHistory("exhentai", null, "2026-01-10", "UTC", []);
        Assert.AreEqual(31, result.Days.Count);
        Assert.AreEqual("2025-12-26", result.Days[0].Date);
        Assert.AreEqual("2026-01-10", result.Days[15].Date);
        Assert.AreEqual("2026-01-25", result.Days[^1].Date);
        Assert.IsTrue(result.Days.All(day => day.Count == 0));
        for (var i = 1; i < result.Days.Count; i++)
            Assert.AreEqual(DateOnly.Parse(result.Days[i - 1].Date, CultureInfo.InvariantCulture).AddDays(1),
                DateOnly.Parse(result.Days[i].Date, CultureInfo.InvariantCulture));
    }

    [TestMethod]
    public async Task UtcWindowIncludesItsFirstInstantAndExcludesTheNextMidnight()
    {
        var start = CenterUtc.AddDays(-15);
        var end = CenterUtc.AddDays(16);
        await Seed(Record("before", start.AddSeconds(-1)), Record("first", start),
            Record("same-day", start.AddHours(23)), Record("center", CenterUtc),
            Record("last", end.AddSeconds(-1)), Record("after", end), Record("without-time", null));
        var result = await _service.GetDailyHistory("exhentai", null, "2026-10-16", "UTC", []);
        Assert.AreEqual(2, result.Days[0].Count);
        Assert.AreEqual(1, result.Days[15].Count);
        Assert.AreEqual(1, result.Days[30].Count);
        Assert.AreEqual(4, result.Days.Sum(day => day.Count));
        Assert.AreEqual(28, result.Days.Count(day => day.Count == 0));
    }

    [TestMethod]
    public async Task ShanghaiGroupsUtcInstantsByTheirLocalPublicationDate()
    {
        await Seed(Record("before", Utc("2026-09-30T15:59:59Z")),
            Record("first", Utc("2026-09-30T16:00:00Z")),
            Record("first-later", Utc("2026-10-01T15:59:59Z")),
            Record("second", Utc("2026-10-01T16:00:00Z")),
            Record("last", Utc("2026-10-31T15:59:59Z")),
            Record("after", Utc("2026-10-31T16:00:00Z")));
        var result = await _service.GetDailyHistory("exhentai", null, "2026-10-16", "Asia/Shanghai", []);
        Assert.AreEqual("2026-10-01", result.Days[0].Date);
        Assert.AreEqual(2, result.Days[0].Count);
        Assert.AreEqual(1, result.Days[1].Count);
        Assert.AreEqual(1, result.Days[30].Count);
        Assert.AreEqual(4, result.Days.Sum(day => day.Count));
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("tag=a")]
    public async Task ExactDomainAndFilterScopeAreKeptIncludingNullAndEmpty(string? filter)
    {
        string?[] scopes = [null, "", "tag=a"];
        for (var i = 0; i < scopes.Length; i++)
            await Seed(Enumerable.Range(0, i + 1).Select(index =>
                Record($"scope-{i}-{index}", CenterUtc, scopes[i])).ToArray());
        await Seed(Record("other-domain", CenterUtc, filter, "soulplus"));
        var result = await _service.GetDailyHistory("exhentai", filter, "2026-10-16", "UTC", []);
        Assert.AreEqual(Array.IndexOf(scopes, filter) + 1, result.Days[15].Count);
        Assert.AreEqual(result.Days[15].Count, result.Days.Sum(day => day.Count));
    }

    [TestMethod]
    public async Task SparseHistoryDoesNotFillTheIntervalBetweenTwoViewedItems()
    {
        await Seed(Record("older", CenterUtc.AddDays(-12)), Record("newer", CenterUtc.AddDays(14)));
        var result = await _service.GetDailyHistory("exhentai", null, "2026-10-16", "UTC", []);
        Assert.AreEqual(2, result.Days.Sum(day => day.Count));
        Assert.AreEqual(29, result.Days.Count(day => day.Count == 0));
    }

    [TestMethod]
    public async Task OneVisitToDifferentPublicationMonthsDoesNotCollectTheirCountsOnTheVisitDay()
    {
        var publicationYear = DateTime.UtcNow.Year - 2;
        var publications = Enumerable.Range(1, 12).Select(month =>
            (contentId: $"month-{month}", updatedAt: (DateTime?)new DateTime(publicationYear, month, 5, 12, 0, 0, DateTimeKind.Utc)))
            .Append((contentId: "unknown-publication", updatedAt: (DateTime?)null)).ToList();
        await _service.MarkContentAsViewed("exhentai", null, publications);
        var stored = await _db.ThirdPartyContentTrackers.AsNoTracking().ToArrayAsync();
        Assert.AreEqual(1, stored.Select(record => record.ViewedAt).Distinct().Count(),
            "All items were viewed in the same call, while their publication dates are different.");
        var visitDay = DateOnly.FromDateTime(stored[0].ViewedAt).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        // A later visit with an unavailable site date must preserve known dates,
        // while a record that never had a publication date remains undated.
        await _service.MarkContentAsViewed("exhentai", null,
            publications.Select(item => (item.contentId, (DateTime?)null)).ToList());
        foreach (var publication in publications.Where(item => item.updatedAt.HasValue))
        {
            var actual = await _db.ThirdPartyContentTrackers.AsNoTracking()
                .SingleAsync(record => record.ContentId == publication.contentId);
            Assert.AreEqual(publication.updatedAt, actual.UpdatedAt);
            var day = publication.updatedAt!.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var history = await _service.GetDailyHistory("exhentai", null, day, "UTC", []);
            Assert.AreEqual(1, history.Days[15].Count, "The record belongs to its publication date.");
            Assert.AreEqual(1, history.Days.Sum(bucket => bucket.Count),
                "Neighboring months and undated records must not inflate this month's window.");
        }
        Assert.IsNull((await _db.ThirdPartyContentTrackers.AsNoTracking()
            .SingleAsync(record => record.ContentId == "unknown-publication")).UpdatedAt);
        var visitWindow = await _service.GetDailyHistory("exhentai", null, visitDay, "UTC", []);
        Assert.AreEqual(0, visitWindow.Days.Sum(bucket => bucket.Count),
            "A single visit today must not move twelve historical publications or an undated record into today's chart.");
    }

    [TestMethod]
    public async Task DuplicateNullFilterRowsCountOnceAtTheirLatestContentTimeBeforeWindowFiltering()
    {
        await Seed(Record("different-days", CenterUtc.AddDays(-1)), Record("different-days", CenterUtc.AddDays(1)),
            Record("same-date", CenterUtc), Record("same-date", CenterUtc),
            Record("latest-outside", CenterUtc), Record("latest-outside", CenterUtc.AddDays(40)),
            Record("latest-inside", CenterUtc.AddDays(-40)), Record("latest-inside", CenterUtc.AddDays(2)),
            Record("excluded", CenterUtc), Record("excluded", CenterUtc.AddDays(3)),
            Record("other-scopes", CenterUtc), Record("other-scopes", CenterUtc.AddDays(40), "tag=a"),
            Record("other-scopes", CenterUtc.AddDays(40), domain: "soulplus"));
        var result = await _service.GetDailyHistory("exhentai", null, "2026-10-16", "UTC", ["excluded"]);
        Assert.AreEqual(4, result.Days.Sum(day => day.Count));
        Assert.AreEqual(0, result.Days[14].Count, "A duplicate's earlier date must not receive another count.");
        Assert.AreEqual(2, result.Days[15].Count, "Same-date duplicates count once, and other scopes do not affect a content ID.");
        Assert.AreEqual(1, result.Days[16].Count);
        Assert.AreEqual(1, result.Days[17].Count);
        Assert.AreEqual(0, result.Days[18].Count, "Exclusion applies to all rows of the current page's ID.");
        Assert.AreEqual(13, await _db.ThirdPartyContentTrackers.CountAsync(), "The query must not repair or delete duplicates.");
    }

    [TestMethod]
    public async Task RepeatedAutoMarksOnExcludedPageItemsDoNotInflatePreviousDailyCounts()
    {
        await Seed(Record("previous", CenterUtc));
        string[] current = ["page-first", "page-last"];
        var before = await _service.GetDailyHistory("exhentai", null, "2026-10-16", "UTC", current);
        for (var i = 0; i < 2; i++)
        {
            await _service.MarkContentAsViewed("exhentai", null,
                [(current[0], CenterUtc.AddHours(1)), (current[1], CenterUtc.AddHours(2))]);
            var after = await _service.GetDailyHistory("exhentai", null, "2026-10-16", "UTC", current);
            CollectionAssert.AreEqual(before.Days.ToArray(), after.Days.ToArray());
            Assert.AreEqual(1, after.Days.Sum(day => day.Count));
        }
    }

    [TestMethod]
    [DataRow("2026-03-08", "2026-02-21T05:00:00Z", "2026-03-24T04:00:00Z", "2026-03-08T05:00:00Z", "2026-03-09T04:00:00Z", 743)]
    [DataRow("2026-11-01", "2026-10-17T04:00:00Z", "2026-11-17T05:00:00Z", "2026-11-01T04:00:00Z", "2026-11-02T05:00:00Z", 745)]
    public async Task NewYorkClockChangesUse31LocalDaysRatherThanAConstantUtcDuration(string center,
        string startIso, string endIso, string centerStartIso, string centerEndIso, int hours)
    {
        var start = Utc(startIso);
        var end = Utc(endIso);
        Assert.AreEqual(hours, (int)(end - start).TotalHours);
        await Seed(Record("before", start.AddSeconds(-1)), Record("first", start),
            Record("center-first", Utc(centerStartIso)), Record("center-last", Utc(centerEndIso).AddSeconds(-1)),
            Record("next-day", Utc(centerEndIso)), Record("last", end.AddSeconds(-1)), Record("after", end));
        var result = await _service.GetDailyHistory("exhentai", null, center, "America/New_York", []);
        Assert.AreEqual(31, result.Days.Count);
        Assert.AreEqual(center, result.Days[15].Date);
        Assert.AreEqual(1, result.Days[0].Count);
        Assert.AreEqual(2, result.Days[15].Count);
        Assert.AreEqual(1, result.Days[16].Count);
        Assert.AreEqual(1, result.Days[30].Count);
        Assert.AreEqual(5, result.Days.Sum(day => day.Count));
    }

    [TestMethod]
    public async Task BothOccurrencesOfAFallBackHourCountOnTheSameLocalDay()
    {
        await Seed(Record("first-hour", Utc("2026-11-01T05:30:00Z")),
            Record("repeated-hour", Utc("2026-11-01T06:30:00Z")));
        var result = await _service.GetDailyHistory("exhentai", null, "2026-11-01", "America/New_York", []);
        Assert.AreEqual(2, result.Days[15].Count);
        Assert.AreEqual(2, result.Days.Sum(day => day.Count));
    }

    [TestMethod]
    public async Task DailyQueryDoesNotTrackOrModifyStoredHistory()
    {
        await Seed(Record("viewed", CenterUtc));
        var before = JsonSerializer.Serialize(await _db.ThirdPartyContentTrackers.AsNoTracking().ToArrayAsync());
        await _service.GetDailyHistory("exhentai", null, "2026-10-16", "UTC", []);
        Assert.AreEqual(0, _db.ChangeTracker.Entries().Count());
        Assert.AreEqual(before, JsonSerializer.Serialize(await _db.ThirdPartyContentTrackers.AsNoTracking().ToArrayAsync()));
    }

    [TestMethod]
    public async Task SkippedOrRepeatedMidnightAtAWindowBoundaryIsRejectedWithoutReadingHistory()
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById("America/Havana");
        var midnight = Enumerable.Range(0, 366).Select(index => new DateOnly(2026, 1, 1).AddDays(index))
            .First(day => zone.IsInvalidTime(day.ToDateTime(TimeOnly.MinValue)) ||
                          zone.IsAmbiguousTime(day.ToDateTime(TimeOnly.MinValue)));
        var center = midnight.AddDays(15).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        await Assert.ThrowsExceptionAsync<ArgumentException>(() =>
            _service.GetDailyHistory("exhentai", null, center, zone.Id, []));
        Assert.AreEqual(0, _db.ChangeTracker.Entries().Count());
    }

    private Task<ServiceGateHost> StartHost() => ServiceGateHost.StartAsync([typeof(ThirdPartyContentTrackerController)],
        services => services.AddSingleton(_service));

    [TestMethod]
    public async Task HttpResponseContainsOnly31DayLabelsAndCounts()
    {
        await Seed(Record("previous", CenterUtc), Record("current", CenterUtc));
        await using var host = await StartHost();
        host.Remote.Mode = RemoteAccessMode.Unrestricted;
        using var request = host.Request(HttpMethod.Post, "/third-party-content-tracker/daily-history");
        request.Headers.Add(ServiceGateHost.RemoteIpHeader, "192.168.3.100");
        request.Content = new StringContent("""
            {"domainKey":"exhentai","filter":null,"centerDay":"2026-10-16","timeZoneId":"Asia/Shanghai","excludeContentIds":["current"]}
            """, Encoding.UTF8, "application/json");
        using var response = await host.SendAsync(request);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var data = json.RootElement.GetProperty("data");
        Assert.AreEqual(1, data.EnumerateObject().Count());
        var days = data.GetProperty("days");
        Assert.AreEqual(31, days.GetArrayLength());
        Assert.AreEqual("2026-10-16", days[15].GetProperty("date").GetString());
        Assert.AreEqual(1, days[15].GetProperty("count").GetInt32());
        Assert.IsTrue(days.EnumerateArray().All(day => day.EnumerateObject().Count() == 2));
    }

    [TestMethod]
    [DataRow("2026-2-1", "UTC")]
    [DataRow("2026-02-29", "UTC")]
    [DataRow("0001-01-01", "UTC")]
    [DataRow("9999-12-31", "UTC")]
    [DataRow("0001-01-16", "Asia/Shanghai")]
    [DataRow("2026-10-16", "Invalid/Zone")]
    public async Task HttpRejectsInvalidDaysOverflowAndUnknownTimeZones(string center, string zone)
    {
        await using var host = await StartHost();
        using var request = host.Request(HttpMethod.Post, "/third-party-content-tracker/daily-history");
        request.Content = new StringContent(JsonSerializer.Serialize(new
            {domainKey = "exhentai", centerDay = center, timeZoneId = zone, excludeContentIds = Array.Empty<string>()}),
            Encoding.UTF8, "application/json");
        using var response = await host.SendAsync(request);
        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.AreEqual(0, await _db.ThirdPartyContentTrackers.CountAsync());
    }

    [TestMethod]
    [DataRow("null")]
    [DataRow("{}")]
    [DataRow("{\"domainKey\":\"exhentai\",\"centerDay\":\"2026-10-16\",\"timeZoneId\":\"UTC\",\"excludeContentIds\":[\"\"]}")]
    public async Task HttpRejectsMissingFieldsAndInvalidExcludedIds(string body)
    {
        await using var host = await StartHost();
        using var request = host.Request(HttpMethod.Post, "/third-party-content-tracker/daily-history");
        request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        using var response = await host.SendAsync(request);
        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.AreEqual(0, await _db.ThirdPartyContentTrackers.CountAsync());
    }
}
