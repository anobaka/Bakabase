using System.Data.Common;
using System.Reflection;
using Bakabase.InsideWorld.Business;
using Bakabase.Modules.Acquisition.Abstractions.Models.Db;
using Bakabase.Modules.Acquisition.Abstractions.Services;
using Bakabase.Service.Components.Acquisition.Planning;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Bakabase.Tests;

[TestClass]
public sealed class AcquisitionTaskNamesTests
{
    private string _root = null!;
    private BakabaseDbContext _db = null!;
    private AcquisitionTaskSearchService _service = null!;
    private NamesProxy _names = null!;
    private QueryRecorder _queries = null!;

    [TestInitialize]
    public async Task Setup()
    {
        _root = Directory.CreateTempSubdirectory("acquisition-task-names-").FullName;
        _queries = new();
        _db = new(new DbContextOptionsBuilder<BakabaseDbContext>()
            .UseSqlite("Data Source=" + Path.Combine(_root, "fixture.db")).AddInterceptors(_queries).Options);
        await _db.Database.EnsureCreatedAsync();
        var service = DispatchProxy.Create<IAcquisitionService, NamesProxy>();
        _names = (NamesProxy)(object)service;
        _service = new(_db, service);
        _db.AcquisitionTasks.AddRange(
            new() {Id = 1, Title = "Task title", ResourceId = 10, PlanJson = "invalid plan is irrelevant"},
            new() {Id = 2, ResourceId = 20, PlanHistoryJson = "invalid history is irrelevant"},
            new() {Id = 3, Title = "Deleted task title", DeletedAt = DateTime.UtcNow},
            new() {Id = 4, Title = " ", ResourceId = 20},
            new() {Id = 5});
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
        _queries.Reads.Clear();
    }

    [TestCleanup]
    public async Task Cleanup()
    {
        await _db.DisposeAsync();
        Directory.Delete(_root, true);
    }

    [TestMethod]
    public async Task Fixed_batch_names_include_deleted_tasks_and_batch_resource_fallback_with_only_three_columns()
    {
        var names = await _service.GetNamesAsync(new([1, 2, 3, 4, 5, 2, 999]));
        Assert.AreEqual(4, names.Count);
        Assert.AreEqual("Task title", names[1]);
        Assert.AreEqual("Resource title", names[2]);
        Assert.AreEqual("Deleted task title", names[3]);
        Assert.AreEqual("Resource title", names[4]);
        CollectionAssert.AreEqual(new[] {20}, _names.Requests.Single());
        Assert.AreEqual(1, _queries.Reads.Count);
        var query = _queries.Reads.Single();
        StringAssert.Contains(query, "\"Id\", \"a\".\"Title\", \"a\".\"ResourceId\"");
        Assert.IsFalse(query.Contains("PlanJson", StringComparison.Ordinal));
        Assert.IsFalse(query.Contains("WorkflowRuns", StringComparison.Ordinal));
        Assert.AreEqual(0, _db.ChangeTracker.Entries().Count());
    }

    [TestMethod]
    public async Task Titled_tasks_need_no_resource_or_execution_lookup()
    {
        var names = await _service.GetNamesAsync(new([3, 1]));
        Assert.AreEqual(2, names.Count);
        Assert.AreEqual(0, _names.Requests.Count);
        Assert.AreEqual(1, _queries.Reads.Count);
    }

    [TestMethod]
    public async Task Invalid_batch_is_rejected_before_database_or_resource_queries()
    {
        foreach (var ids in new IReadOnlyList<int>[] {null!, [], [0], [-1], Enumerable.Range(1, 10001).ToArray()})
            await Assert.ThrowsExactlyAsync<ArgumentException>(() => _service.GetNamesAsync(new(ids)));
        Assert.AreEqual(0, _queries.Reads.Count);
        Assert.AreEqual(0, _names.Requests.Count);
    }

    public class NamesProxy : DispatchProxy
    {
        public List<int[]> Requests { get; } = [];
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            Assert.AreEqual(nameof(IAcquisitionService.GetDisplayNamesAsync), targetMethod!.Name);
            Requests.Add(((IReadOnlyCollection<int>)args![0]!).ToArray());
            return Task.FromResult(new Dictionary<int, string?> {[20] = "Resource title"});
        }
    }

    private sealed class QueryRecorder : DbCommandInterceptor
    {
        public List<string> Reads { get; } = [];
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            Reads.Add(command.CommandText);
            return ValueTask.FromResult(result);
        }
    }
}
