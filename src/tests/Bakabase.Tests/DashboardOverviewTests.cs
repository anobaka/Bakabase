using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Models.Db;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.InsideWorld.Business;
using Bakabase.Modules.Collection.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Bakabase.Service.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Bakabase.Tests;

[TestClass]
public sealed class DashboardOverviewTests
{
    private static readonly DateTime Now = new(2026, 9, 15, 12, 0, 0);
    private static readonly DateTime Monday = new(2026, 9, 14);
    private BakabaseDbContext _db = null!;
    private SqlCapture _sql = null!;
    private DashboardOverviewService Service => new(_db, new FixedTimeProvider());

    [TestInitialize]
    public async Task Setup()
    {
        _sql = new SqlCapture();
        _db = new BakabaseDbContext(new DbContextOptionsBuilder<BakabaseDbContext>()
            .UseSqlite("Data Source=:memory:").AddInterceptors(_sql).Options);
        await _db.Database.EnsureCreatedAsync();
        _sql.Commands.Clear();
    }

    [TestCleanup]
    public async Task Cleanup() => await _db.DisposeAsync();

    [TestMethod]
    public async Task EmptyDatabaseHasZeroCountsAndEmptyLists()
    {
        var result = await Service.GetAsync();

        Assert.AreEqual(0, result.TotalResourceCount);
        Assert.AreEqual(0, result.LocalResourceCount);
        Assert.AreEqual(0, result.PendingResourceCount);
        Assert.AreEqual(0, result.CollectionCount);
        Assert.AreEqual(0, result.MediaLibraryCount);
        Assert.AreEqual(0, result.ThisWeekAddedCount);
        Assert.AreEqual(0, result.MediaLibraries.Count);
        Assert.AreEqual(0, result.Workflows.RunningCount);
        Assert.AreEqual(0, result.Workflows.WaitingCount);
        Assert.AreEqual(0, result.Workflows.FailedRecentlyCount);
    }

    [TestMethod]
    public async Task CountsResourcesIndependentlyOfLibraryMembershipAndUsesLocalPathSemantics()
    {
        _db.ResourcesV2.AddRange(
            Resource(1, "/not-required-to-exist/first", ResourceStatus.Active),
            Resource(2, null),
            Resource(3, ""),
            Resource(4, "/not-required-to-exist/absent", ResourceStatus.Absent),
            Resource(5, null, ResourceStatus.Unavailable));
        _db.MediaLibrariesV2.AddRange(Library(1, "First"), Library(2, "Second"), Library(3, "Empty"));
        _db.MediaLibraryResourceMappings.AddRange(
            Mapping(1, 1), Mapping(1, 2), Mapping(2, 1), Mapping(2, 4), Mapping(1, 999));
        _db.Collections.AddRange(
            new CollectionDbModel {Name = "Manual"},
            // A dashboard count must never evaluate rule membership.
            new CollectionDbModel {Name = "Rule", RuleSearchJson = "not valid JSON"});
        await SaveFixture();

        var result = await Service.GetAsync();

        Assert.AreEqual(5, result.TotalResourceCount);
        Assert.AreEqual(2, result.LocalResourceCount);
        Assert.AreEqual(3, result.PendingResourceCount);
        Assert.AreEqual(2, result.CollectionCount);
        Assert.AreEqual(3, result.MediaLibraryCount);
        CollectionAssert.AreEqual(new[] {1, 2, 3}, result.MediaLibraries.Select(library => library.Id).ToArray());
        CollectionAssert.AreEqual(new[] {2, 2, 0}, result.MediaLibraries.Select(library => library.ResourceCount).ToArray());
        Assert.AreEqual("Empty", result.MediaLibraries[2].Name);
    }

    [TestMethod]
    public async Task AddedCountsUseServerLocalMondayBoundariesAndExcludeFutureDates()
    {
        var dates = new[]
        {
            Monday.AddDays(-7), Monday.AddTicks(-1), Monday, Now, Now.AddSeconds(1)
        };
        _db.ResourcesV2.AddRange(dates.Select((date, index) => Resource(index + 1, null) with {CreateDt = date}));
        await SaveFixture();

        var result = await Service.GetAsync();

        Assert.AreEqual(5, result.TotalResourceCount);
        Assert.AreEqual(2, result.ThisWeekAddedCount);
    }

    [TestMethod]
    public async Task WorkflowCountsUseCurrentStatesAndRecentFailureTime()
    {
        _db.WorkflowDefinitions.Add(new WorkflowDefinitionDbModel
        {
            Id = 1, Name = "Direct download", TriggerKind = "acquisition.requested", IsBuiltin = true
        });
        _db.WorkflowRuns.AddRange(
            Run(1, WorkflowRunStatus.Pending, Now.AddDays(-20)),
            Run(2, WorkflowRunStatus.Running, Now),
            Run(3, WorkflowRunStatus.Waiting, Now.AddDays(-60)) with {WaitingSince = Now.AddDays(-30)},
            Run(4, WorkflowRunStatus.Waiting, Now.AddDays(-1)) with {WaitingSince = Now.AddHours(-12)},
            // A long-running job that failed yesterday is recent even if its start is old.
            Run(5, WorkflowRunStatus.Failed, Now.AddDays(-30), Now.AddDays(-1)),
            Run(6, WorkflowRunStatus.Interrupted, Now.AddDays(-2)),
            Run(7, WorkflowRunStatus.Failed, Now.AddDays(-8), Now.AddDays(-7)),
            Run(8, WorkflowRunStatus.Failed, Now.AddDays(-9), Now.AddDays(-7).AddTicks(-1)),
            Run(9, WorkflowRunStatus.Success, Now, Now),
            Run(10, WorkflowRunStatus.Cancelled, Now, Now),
            Run(11, WorkflowRunStatus.Failed, Now, Now.AddHours(1)));
        await SaveFixture();

        var result = await Service.GetAsync();

        Assert.AreEqual(2, result.Workflows.RunningCount);
        Assert.AreEqual(2, result.Workflows.WaitingCount);
        Assert.AreEqual(3, result.Workflows.FailedRecentlyCount);
    }

    [TestMethod]
    public async Task LargerLibraryStillUsesBoundedSqlProjectionsWithoutHydratingPropertiesOrRunPayloads()
    {
        _db.ResourcesV2.AddRange(Enumerable.Range(1, 200).Select(id => Resource(id, null)));
        _db.MediaLibrariesV2.AddRange(Enumerable.Range(1, 10).Select(id => Library(id, $"Library {id}")));
        _db.MediaLibraryResourceMappings.AddRange(Enumerable.Range(1, 10).SelectMany(libraryId =>
            Enumerable.Range(1, libraryId).Select(resourceId => Mapping(libraryId, resourceId))));
        _db.WorkflowDefinitions.Add(new WorkflowDefinitionDbModel {Id = 1, Name = "Waiting", TriggerKind = "test"});
        _db.WorkflowRuns.AddRange(Enumerable.Range(1, 8).Select(id =>
            Run(id, WorkflowRunStatus.Waiting, Now.AddDays(-id)) with
            {
                PayloadJson = "not valid JSON", CurrentItemJson = "not valid JSON",
                StepStatsJson = "not valid JSON", OutputItemsJson = "not valid JSON"
            }));
        _db.WorkflowRuns.Add(Run(9, WorkflowRunStatus.Failed, Now, Now));
        await SaveFixture();

        var result = await Service.GetAsync();

        Assert.AreEqual(200, result.TotalResourceCount);
        Assert.AreEqual(10, result.MediaLibraryCount);
        CollectionAssert.AreEqual(new[] {10, 9, 8, 7, 6, 5}, result.MediaLibraries.Select(library => library.Id).ToArray());
        Assert.AreEqual(8, result.Workflows.WaitingCount);
        Assert.AreEqual(1, result.Workflows.FailedRecentlyCount);
        Assert.IsFalse(_db.ChangeTracker.Entries().Any(), "Overview queries must not materialize tracked entities.");
        Assert.IsTrue(_sql.Commands.Count is > 0 and <= 5, "Query count must be bounded independently of library size.");
        var sql = string.Join("\n", _sql.Commands);
        foreach (var forbidden in new[]
                 {
                     "CustomPropertyValues", "ReservedPropertyValues", "ResourceCaches", "PayloadJson",
                     "CurrentItemJson", "StepStatsJson", "OutputItemsJson", "WaitPromptJson"
                 })
        {
            Assert.IsFalse(sql.Contains(forbidden, StringComparison.Ordinal), $"Overview unnecessarily read {forbidden}.");
        }
        Assert.IsTrue(_sql.Commands.All(command => command.Contains("COUNT(", StringComparison.OrdinalIgnoreCase) ||
                                                   command.Contains("LIMIT", StringComparison.OrdinalIgnoreCase)),
            "Each query must aggregate in SQL or return a bounded projection.");
    }

    private async Task SaveFixture()
    {
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
        _sql.Commands.Clear();
    }

    private static ResourceDbModel Resource(int id, string? path, ResourceStatus status = ResourceStatus.Active) =>
        new() {Id = id, Path = path, Status = status, CreateDt = Now};

    private static MediaLibraryV2DbModel Library(int id, string name) =>
        new() {Id = id, Name = name, Paths = "[]", ResourceCount = 999};

    private static MediaLibraryResourceMappingDbModel Mapping(int libraryId, int resourceId) =>
        new() {MediaLibraryId = libraryId, ResourceId = resourceId, CreateDt = Now};

    private static WorkflowRunDbModel Run(int id, WorkflowRunStatus status, DateTime startedAt,
        DateTime? completedAt = null) =>
        new() {Id = id, WorkflowDefinitionId = 1, Status = status, StartedAt = startedAt, CompletedAt = completedAt};

    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(Now, TimeSpan.Zero);
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }

    private sealed class SqlCapture : DbCommandInterceptor
    {
        public List<string> Commands { get; } = [];

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            Commands.Add(command.CommandText);
            return ValueTask.FromResult(result);
        }
    }
}
