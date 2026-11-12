using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business;
using Bakabase.InsideWorld.Business.Components.PostParser.Models.Db;
using Bakabase.Modules.Acquisition.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Bakabase.Service.Components.Tasks;
using Bakabase.TestKit.Utils;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Tests;

[TestClass]
public sealed class WorkflowRunCleanupOwnershipTests
{
    private string _root = null!;
    private IServiceProvider _services = null!;
    private AsyncServiceScope _scope;
    private BakabaseDbContext _db = null!;

    [TestInitialize]
    public async Task Setup()
    {
        _root = Path.Combine(Path.GetTempPath(), "workflow-owner-retention-" + Guid.NewGuid().ToString("N"));
        _services = await TestServiceBuilder.BuildServiceProvider(Path.Combine(_root, "database"));
        _scope = _services.CreateAsyncScope();
        _db = _scope.ServiceProvider.GetRequiredService<BakabaseDbContext>();
    }

    [TestCleanup]
    public async Task Cleanup()
    {
        await _scope.DisposeAsync();
        if (_services is IAsyncDisposable disposable) await disposable.DisposeAsync();
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    [DataTestMethod]
    [DataRow("postParser", WorkflowRunStatus.Failed)]
    [DataRow("postParser", WorkflowRunStatus.Success)]
    [DataRow("acquisition", WorkflowRunStatus.Interrupted)]
    [DataRow("acquisition", WorkflowRunStatus.Cancelled)]
    public async Task CurrentOwnerRetainsLegacyRunWithoutSourceIndexAndReleasesItWhenRemoved(
        string module, WorkflowRunStatus status)
    {
        var owned = await AddRun(status);
        var ownerId = await AddOwner(module, owned.Id);
        var oldestUnowned = await AddRun(WorkflowRunStatus.Success);
        await AddRun(WorkflowRunStatus.Success);

        var prunable = await WorkflowRunCleanupTask.GetPrunableRunIdsAsync(_db, 0, [], 1);

        CollectionAssert.AreEqual(new[] {oldestUnowned.Id}, prunable.ToArray(),
            "A live business task's current run must not consume the unowned history budget.");
        await RemoveOwner(module, ownerId);
        prunable = await WorkflowRunCleanupTask.GetPrunableRunIdsAsync(_db, 0, [], 1);
        CollectionAssert.AreEquivalent(new[] {owned.Id, oldestUnowned.Id}, prunable.ToArray(),
            "Deleting the owner must release even a legacy run to normal retention.");
    }

    [DataTestMethod]
    [DataRow("postParser")]
    [DataRow("acquisition")]
    public async Task LiveSourcePreservesPriorRevisionsAndDynamicPlansWithoutPinningOrphans(string module)
    {
        var ownerId = await AddOwner(module, null);
        var previous = await AddRun(WorkflowRunStatus.Failed);
        var latest = await AddRun(WorkflowRunStatus.Success);
        _db.Set<WorkflowRunSourceDbModel>().AddRange(
            Source(previous.Id, module, ownerId, "1", "0"),
            Source(latest.Id, module, ownerId, "2", "1"));
        await _db.SaveChangesAsync();
        var oldestUnowned = await AddRun(WorkflowRunStatus.Success);
        await AddRun(WorkflowRunStatus.Success);

        var prunable = await WorkflowRunCleanupTask.GetPrunableRunIdsAsync(_db, 0, [], 1);

        CollectionAssert.AreEqual(new[] {oldestUnowned.Id}, prunable.ToArray(),
            "A source owns all its revisions/result keys even when its current execution pointer is absent.");
        await RemoveOwner(module, ownerId);
        prunable = await WorkflowRunCleanupTask.GetPrunableRunIdsAsync(_db, 0, [], 1);
        CollectionAssert.AreEquivalent(new[] {previous.Id, latest.Id, oldestUnowned.Id}, prunable.ToArray(),
            "Source-index rows must not retain history indefinitely after the business owner disappears.");
    }

    [TestMethod]
    public async Task MissingUnknownAndMismatchedSourceIdentitiesDoNotClaimLiveOwnerHistory()
    {
        var ownerId = await AddOwner("postParser", null);
        var missing = await AddRun(WorkflowRunStatus.Success);
        var unknownModule = await AddRun(WorkflowRunStatus.Failed);
        var wrongKind = await AddRun(WorkflowRunStatus.Cancelled);
        _db.Set<WorkflowRunSourceDbModel>().AddRange(
            Source(missing.Id, "postParser", ownerId + 1000, "1", ""),
            Source(unknownModule.Id, "unknown", ownerId, "1", ""),
            Source(wrongKind.Id, "postParser", ownerId, "1", "") with {EntityKind = "other"});
        await _db.SaveChangesAsync();

        var prunable = await WorkflowRunCleanupTask.GetPrunableRunIdsAsync(_db, 0, [], 0);

        CollectionAssert.AreEquivalent(new[] {missing.Id, unknownModule.Id, wrongKind.Id}, prunable.ToArray());
    }

    [TestMethod]
    public async Task ARunWithSeveralSourcesIsReleasedOnlyAfterItsLastKnownOwnerIsRemoved()
    {
        var postId = await AddOwner("postParser", null);
        var acquisitionId = await AddOwner("acquisition", null);
        var run = await AddRun(WorkflowRunStatus.Success);
        _db.Set<WorkflowRunSourceDbModel>().AddRange(
            Source(run.Id, "postParser", postId, "3", ""),
            Source(run.Id, "acquisition", acquisitionId, "2", ""));
        await _db.SaveChangesAsync();
        await RemoveOwner("postParser", postId);

        Assert.AreEqual(0, (await WorkflowRunCleanupTask.GetPrunableRunIdsAsync(_db, 0, [], 0)).Count);
        await RemoveOwner("acquisition", acquisitionId);
        CollectionAssert.AreEqual(new[] {run.Id},
            (await WorkflowRunCleanupTask.GetPrunableRunIdsAsync(_db, 0, [], 0)).ToArray());
    }

    private async Task<WorkflowRunDbModel> AddRun(WorkflowRunStatus status)
    {
        // Dynamic execution uses definition ID 0 and does not require a surviving template.
        var run = new WorkflowRunDbModel
        {
            WorkflowDefinitionId = 0, Status = status,
            StartedAt = new DateTime(2026, 10, 23, 0, 0, 0, DateTimeKind.Utc)
        };
        _db.Set<WorkflowRunDbModel>().Add(run);
        await _db.SaveChangesAsync();
        return run;
    }

    private async Task<int> AddOwner(string module, int? runId)
    {
        if (module == "postParser")
        {
            var owner = new PostParserTaskDbModel {Link = "https://example.test/post", WorkflowRunId = runId};
            _db.Set<PostParserTaskDbModel>().Add(owner);
            await _db.SaveChangesAsync();
            return owner.Id;
        }
        var acquisition = new AcquisitionTaskDbModel {WorkflowRunId = runId};
        _db.Set<AcquisitionTaskDbModel>().Add(acquisition);
        await _db.SaveChangesAsync();
        return acquisition.Id;
    }

    private Task<int> RemoveOwner(string module, int id) => module == "postParser"
        ? _db.Set<PostParserTaskDbModel>().Where(owner => owner.Id == id)
            .ExecuteUpdateAsync(update => update.SetProperty(owner => owner.IsDeleted, true))
        : _db.Set<AcquisitionTaskDbModel>().Where(owner => owner.Id == id).ExecuteDeleteAsync();

    private static WorkflowRunSourceDbModel Source(int runId, string module, int entityId,
        string revision, string resultKey) => new()
    {
        WorkflowRunId = runId, Module = module, EntityKind = "task", EntityId = entityId.ToString(),
        Revision = revision, ResultKey = resultKey
    };
}
