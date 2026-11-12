using System.Text.Json;
using Bakabase.InsideWorld.Business;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Modules.Workflow.Services;
using Bakabase.Service.Controllers;
using Bakabase.TestKit.Utils;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Tests;

[TestClass]
public sealed class ArtifactDeliveryLayoutPreviewTests
{
    [TestMethod]
    public async Task PreviewIsBoundedReadOnlyAndRequiresRecordedRunOwnership()
    {
        var provider = await TestServiceBuilder.BuildServiceProvider();
        await using var providerLifetime = (IAsyncDisposable)provider;
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BakabaseDbContext>();
        var id = Guid.NewGuid().ToString("N"); var op = Guid.NewGuid().ToString("N");
        var run = new WorkflowRunDbModel {StartedAt = DateTime.UtcNow};
        run.NodeExecutions.Add(new() {NodeId = "publish", Kind = "test", Attempt = 1,
            StartedAt = DateTime.UtcNow, Status = "succeeded", OutputsJson = JsonSerializer.Serialize(new[]
                {new WorkflowOutputBlock {Kind = "artifact", Key = "result", ArtifactId = id}}, WorkflowJson.Options)});
        var unrelated = new WorkflowRunDbModel {StartedAt = DateTime.UtcNow};
        var entries = Enumerable.Range(0, 201).Select(i => new ArtifactManifestEntry($"author/{i:D4}.jpg", 1, new string('a', 64))).ToArray();
        var fingerprint = ArtifactManifestFiles.Fingerprint(entries);
        db.WorkflowRuns.AddRange(run, unrelated);
        db.ArtifactOperations.Add(new() {Id = op, IntentKey = op, Kind = "test"});
        var row = new ArtifactDbModel {Id = id, OperationId = op, RootPath = "/nonexistent/do-not-access-layout-preview",
            Fingerprint = fingerprint, ManifestJson = JsonSerializer.Serialize(new ArtifactManifest(entries, fingerprint), WorkflowJson.Options)};
        db.Artifacts.Add(row); await db.SaveChangesAsync();
        var controller = new ArtifactDeliveryLayoutController(
            scope.ServiceProvider.GetRequiredService<WorkflowOutputQueryService<BakabaseDbContext>>(),
            scope.ServiceProvider.GetRequiredService<IArtifactOperationStore>());
        var result = await controller.Preview(run.Id, id, new() {CollapseSingleRootDirectory = true}, default);
        Assert.AreEqual(0, result.Code); Assert.IsNotNull(result.Data);
        Assert.IsTrue(result.Data.Changed); Assert.IsTrue(result.Data.Truncated);
        Assert.AreEqual(201, result.Data.FileCount); Assert.AreEqual(201, result.Data.ChangedFileCount);
        Assert.AreEqual(100, result.Data.FileMappings.Count);
        Assert.AreEqual("0000.jpg", result.Data.FileMappings[0].TargetRelativePath);
        Assert.IsNull((await controller.Preview(unrelated.Id, id, new(), default)).Data);
        Assert.IsNull((await controller.Preview(run.Id, "unowned", new(), default)).Data);
        Assert.AreEqual(ArtifactLocationState.Available, row.State);
        Assert.AreEqual(fingerprint, row.Fingerprint);
        Assert.IsFalse(db.ChangeTracker.HasChanges(), "Preview cannot update task, operation or artifact state.");
    }
}
