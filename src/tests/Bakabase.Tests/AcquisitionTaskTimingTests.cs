using System.Text.Json;
using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.InsideWorld.Business;
using Bakabase.Modules.Acquisition.Abstractions.Models.Db;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Acquisition.Abstractions.Services;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Workflow.Abstractions.Services;
using Bakabase.Service.Components.Acquisition.Planning;
using Bakabase.Service.Components.DownloadIntents;
using Bakabase.TestKit.Utils;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using JsonSerializer = System.Text.Json.JsonSerializer;

namespace Bakabase.Tests;

[TestClass]
public sealed class AcquisitionTaskTimingTests
{
    private IServiceProvider _services = null!;
    private BakabaseDbContext _db = null!;
    private IAcquisitionService _acquisitions = null!;
    private AcquisitionPlanService _plans = null!;
    private static readonly DateTime Created = new(2025, 1, 1, 1, 0, 0);
    private static readonly DateTime Submitted = Created.AddDays(2);
    private static readonly DateTime Finished = Submitted.AddHours(3);

    [TestInitialize]
    public async Task Setup()
    {
        _services = await TestServiceBuilder.BuildServiceProvider(services => services.AddScoped(_ =>
            new DownloadMethodRegistry([], [], null!, null!)));
        _db = _services.GetRequiredService<BakabaseDbContext>();
        _acquisitions = _services.GetRequiredService<IAcquisitionService>();
        _plans = new(_db, [], _acquisitions, _services.GetRequiredService<IWorkflowDefinitionService>(),
            _services.GetRequiredService<DownloadIntentService>(), _services.GetRequiredService<IUserStoragePolicy>());
    }

    [DataTestMethod]
    [DataRow(false, WorkflowRunStatus.Success)]
    [DataRow(true, WorkflowRunStatus.Success)]
    [DataRow(false, WorkflowRunStatus.Waiting)]
    [DataRow(true, WorkflowRunStatus.Pending)]
    public async Task ListAndDetailsUseAssociatedRunTimesForLegacyAndPlanTasks(bool dynamicPlan, WorkflowRunStatus status)
    {
        var run = new WorkflowRunDbModel
        {
            WorkflowDefinitionId = dynamicPlan ? 0 : 999,
            Status = status, StartedAt = Submitted,
            CompletedAt = status == WorkflowRunStatus.Success ? Finished : null
        };
        _db.WorkflowRuns.Add(run);
        await _db.SaveChangesAsync();
        var row = TaskRow(dynamicPlan, run.Id);
        _db.AcquisitionTasks.Add(row);
        // A newer, unrelated run must never supply this task's execution dates.
        _db.WorkflowRuns.Add(new() {Status = WorkflowRunStatus.Success, StartedAt = Finished.AddDays(1), CompletedAt = Finished.AddDays(2)});
        await _db.SaveChangesAsync();

        AssertTimes((await _acquisitions.SearchAsync()).Single(), row, run);
        AssertTimes((await _acquisitions.GetAsync(row.Id))!, row, run);
        if (dynamicPlan) AssertTimes((await _plans.GetAsync(row.Id, default))!.Task, row, run);
    }

    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task MissingRunDoesNotSubstituteTaskIndexDates(bool associatedRunMissing)
    {
        var row = TaskRow(true, associatedRunMissing ? 987654 : null);
        _db.AcquisitionTasks.Add(row);
        await _db.SaveChangesAsync();
        foreach (var task in new[] {(await _acquisitions.SearchAsync()).Single(), (await _acquisitions.GetAsync(row.Id))!,
                     (await _plans.GetAsync(row.Id, default))!.Task})
        {
            AssertTimes(task, row, null);
            Assert.AreEqual(associatedRunMissing, task.WorkflowRunId != null,
                "The retained run reference distinguishes missing history from a task not yet submitted.");
        }
    }

    [DataTestMethod]
    [DataRow(WorkflowRunStatus.Pending, AcquisitionStatus.Running)]
    [DataRow(WorkflowRunStatus.Waiting, AcquisitionStatus.Waiting)]
    [DataRow(WorkflowRunStatus.Success, AcquisitionStatus.Completed)]
    [DataRow(WorkflowRunStatus.Failed, AcquisitionStatus.Failed)]
    public async Task ReconcileTracksOriginalRunRetriedAfterRecordedFailure(
        WorkflowRunStatus current, AcquisitionStatus expected)
    {
        var ended = current is WorkflowRunStatus.Success or WorkflowRunStatus.Failed;
        var run = new WorkflowRunDbModel {Status = current, StartedAt = Submitted,
            CompletedAt = ended ? Finished : null, ErrorMessage = current == WorkflowRunStatus.Failed ? "new cause" : null};
        _db.WorkflowRuns.Add(run);
        await _db.SaveChangesAsync();
        var task = TaskRow(true, run.Id);
        task.Status = AcquisitionStatus.Failed;
        task.Error = "first failure";
        var deleted = TaskRow(true, run.Id);
        deleted.DeletedAt = Finished;
        deleted.Status = AcquisitionStatus.Failed;
        _db.AcquisitionTasks.AddRange(task, deleted);
        await _db.SaveChangesAsync();

        var queue = (Bakabase.Modules.Acquisition.Components.IAcquisitionQueue)_acquisitions;
        await queue.ReconcileAsync();
        await _db.Entry(task).ReloadAsync();
        Assert.AreEqual(expected, task.Status);
        Assert.AreEqual(run.ErrorMessage, task.Error);
        Assert.AreEqual(run.CompletedAt, task.CompletedAt);
        var updated = task.UpdatedAt;
        await queue.ReconcileAsync();
        await _db.Entry(task).ReloadAsync();
        Assert.AreEqual(updated, task.UpdatedAt);
        await _db.Entry(deleted).ReloadAsync();
        Assert.AreEqual(AcquisitionStatus.Failed, deleted.Status);
        Assert.AreEqual(Finished.AddDays(-1), deleted.CompletedAt);
    }

    [TestMethod]
    public async Task SourceSummariesKeepEachTasksExactRunDatesAndPreserveUnstartedAndMissingHistory()
    {
        var completedRun = new WorkflowRunDbModel {Status = WorkflowRunStatus.Success, StartedAt = Submitted, CompletedAt = Finished};
        var waitingRun = new WorkflowRunDbModel {Status = WorkflowRunStatus.Waiting, StartedAt = Submitted.AddHours(1)};
        _db.WorkflowRuns.AddRange(completedRun, waitingRun);
        await _db.SaveChangesAsync();
        var source = new WorkflowSourceRef("test", "entry", "one", "2");
        var rows = new[] {TaskRow(true, completedRun.Id), TaskRow(true, waitingRun.Id), TaskRow(true, null), TaskRow(true, 987654)};
        foreach (var row in rows) row.SourceRefsJson = JsonSerializer.Serialize(new[] {source}, WorkflowJson.Options);
        _db.AcquisitionTasks.AddRange(rows);
        await _db.SaveChangesAsync();

        var summaries = await _plans.BySourcesAsync([source, source with {Revision = "3"}], default);
        Assert.AreEqual(4, summaries[0].Tasks.Count);
        Assert.AreEqual(0, summaries[1].Tasks.Count);
        var runs = new Dictionary<int, WorkflowRunDbModel> {[completedRun.Id] = completedRun, [waitingRun.Id] = waitingRun};
        foreach (var row in rows)
        {
            var summary = summaries[0].Tasks.Single(t => t.Id == row.Id);
            AssertTimes(summary, row, row.WorkflowRunId is { } id ? runs.GetValueOrDefault(id) : null);
            var detail = (await _plans.GetAsync(row.Id, default))!.Task;
            Assert.AreEqual(detail.ExecutionStartedAt, summary.ExecutionStartedAt);
            Assert.AreEqual(detail.ExecutionCompletedAt, summary.ExecutionCompletedAt);
        }
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(summaries[0].Tasks.Single(t => t.Id == rows[0].Id), WorkflowJson.Options));
        Assert.AreEqual(Submitted.ToUniversalTime(), json.RootElement.GetProperty("executionStartedAt").GetDateTime());
        Assert.AreEqual(Finished.ToUniversalTime(), json.RootElement.GetProperty("executionCompletedAt").GetDateTime());
    }

    [TestMethod]
    public async Task SqliteTimesSerializeAsUtcInstantsDespiteTheHostsTimezoneFreeDateFormat()
    {
        var run = new WorkflowRunDbModel {Status = WorkflowRunStatus.Success, StartedAt = Submitted,
            CompletedAt = Finished, WaitingSince = Submitted.AddMinutes(1)};
        _db.WorkflowRuns.Add(run);
        await _db.SaveChangesAsync();
        var source = new WorkflowSourceRef("test", "entry", "timezone");
        var row = TaskRow(true, run.Id);
        row.SourceRefsJson = JsonSerializer.Serialize(new[] {source}, WorkflowJson.Options);
        _db.AcquisitionTasks.Add(row);
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear(); // Read the SQLite values whose DateTime Kind is unspecified.

        var task = (await _acquisitions.GetAsync(row.Id))!;
        var summary = (await _plans.BySourcesAsync([source], default)).Single().Tasks.Single();
        var settings = new JsonSerializerSettings
        {
            ContractResolver = new CamelCasePropertyNamesContractResolver(),
            DateFormatString = "yyyy-MM-dd HH:mm:ss.fff"
        };
        foreach (var dto in new[] {task, summary})
        {
            using var json = JsonDocument.Parse(JsonConvert.SerializeObject(dto, settings));
            foreach (var (name, expected) in new[]
                     {
                         ("createdAt", Created), ("updatedAt", row.UpdatedAt), ("completedAt", row.CompletedAt!.Value),
                         ("executionStartedAt", Submitted), ("executionCompletedAt", Finished)
                     })
            {
                var text = json.RootElement.GetProperty(name).GetString()!;
                Assert.IsTrue(text.EndsWith('Z'), $"{name} must include an explicit UTC zone.");
                Assert.AreEqual(DateTime.SpecifyKind(expected, DateTimeKind.Local).ToUniversalTime(),
                    json.RootElement.GetProperty(name).GetDateTime(), name);
            }
        }
        Assert.AreEqual(DateTimeKind.Utc, task.WaitingSince!.Value.Kind);
        Assert.AreEqual(Submitted.AddMinutes(1).ToUniversalTime(), task.WaitingSince);
        var alreadyUtc = new AcquisitionTask {CreatedAt = DateTime.SpecifyKind(Created, DateTimeKind.Utc)};
        Assert.AreEqual(alreadyUtc.CreatedAt, alreadyUtc.NormalizeTimestampsToUtc().CreatedAt,
            "UTC values must not be converted as though they were server-local a second time.");
    }

    private static AcquisitionTaskDbModel TaskRow(bool dynamicPlan, int? runId) => new()
    {
        Title = "Timing fixture", BuilderId = dynamicPlan ? "prepared" : null,
        RecipeDefinitionId = dynamicPlan ? null : 999, WorkflowRunId = runId,
        Status = AcquisitionStatus.Waiting, CreatedAt = Created,
        UpdatedAt = Finished.AddDays(1), CompletedAt = Finished.AddDays(-1)
    };

    private static void AssertTimes(AcquisitionTask task, AcquisitionTaskDbModel row, WorkflowRunDbModel? run)
    {
        Assert.AreEqual(row.CreatedAt.ToUniversalTime(), task.CreatedAt);
        Assert.AreEqual(row.UpdatedAt.ToUniversalTime(), task.UpdatedAt);
        Assert.AreEqual(row.CompletedAt?.ToUniversalTime(), task.CompletedAt, "Existing task-index fields retain their own instants.");
        Assert.AreEqual(run?.StartedAt.ToUniversalTime(), task.ExecutionStartedAt);
        Assert.AreEqual(run?.CompletedAt?.ToUniversalTime(), task.ExecutionCompletedAt);
    }
}
