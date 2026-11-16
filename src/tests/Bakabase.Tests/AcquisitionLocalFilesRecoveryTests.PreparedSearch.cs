using System.Text.Json;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain;
using Bakabase.Modules.Acquisition.Abstractions.Services;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Service.Components.Acquisition.Planning;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Tests;

public sealed partial class AcquisitionRecoveryTests
{
    [DataTestMethod]
    [DataRow("missingLocalFile")]
    [DataRow("unregisteredPart")]
    [DataRow("wrongOwner")]
    [DataRow("wrongPart")]
    [DataRow("retired")]
    [DataRow("fingerprint")]
    [DataRow("runningRevision")]
    public async Task Prepared_download_fact_uses_current_revision_and_all_registered_inputs_without_touching_files(string variant)
    {
        var fixture = await LocalFilesFixture();
        var preview = await fixture.Recovery.PreviewAsync(new([fixture.TaskId], "localFiles"));
        var result = await fixture.Recovery.ApplyAsync(new(preview.PreviewToken, [fixture.TaskId], PrepareOnly: true));
        Assert.AreEqual("prepared", result.LocalFiles!.Single().Status);
        await _db.AcquisitionTasks.Where(t => t.Id == fixture.TaskId)
            .ExecuteUpdateAsync(u => u.SetProperty(t => t.TargetDirectory, _root));
        var search = new AcquisitionTaskSearchService(_db, _services.GetRequiredService<IAcquisitionService>(),
            artifacts: _services.GetRequiredService<IArtifactOperationStore>());
        var filter = new AcquisitionTaskSearchInput(Stage: "ready", PageSize: 1, Facts: ["downloadedPending"]);
        var page = await search.SearchAsync(filter);
        Assert.AreEqual(1, page.FactCounts["downloadedPending"]);
        CollectionAssert.AreEqual(new[] {fixture.TaskId}, page.Items.Select(t => t.Id).ToArray());
        Assert.AreEqual(0, (await search.SearchAsync(filter with {Facts = ["downloadedPending", "spaceInsufficient"]})).TotalCount);

        var task = await _db.AcquisitionTasks.AsNoTracking().SingleAsync(t => t.Id == fixture.TaskId);
        var plan = JsonSerializer.Deserialize<AcquisitionPlan>(task.PlanJson!, WorkflowJson.Options)!;
        var part = plan.Parts.Single();
        var input = part.ContinueFrom!;
        switch (variant)
        {
            case "missingLocalFile":
                var record = await _services.GetRequiredService<IArtifactOperationStore>().GetArtifactAsync(input.ArtifactId);
                File.Move(record!.Files.Single(), record.Files.Single() + ".moved");
                Assert.IsFalse(await fixture.Plans.CanAnalyzePreparedFilesAsync(task, default));
                break;
            case "unregisteredPart": plan = plan with {Parts = [part, part with {Id = "additional", ContinueFrom = null}]}; break;
            case "wrongOwner": plan = plan with {Parts = [part with {ContinueFrom = input with {TaskId = task.Id + 200}}]}; break;
            case "wrongPart": plan = plan with {Parts = [part with {Id = "foreign"}]}; break;
            case "fingerprint": plan = plan with {Parts = [part with {ContinueFrom = input with {Fingerprint = new string('d', 64)}}]}; break;
            case "retired":
                await _db.Artifacts.Where(a => a.Id == input.ArtifactId)
                    .ExecuteUpdateAsync(u => u.SetProperty(a => a.State, ArtifactLocationState.Retired));
                break;
            case "runningRevision":
                _db.Set<WorkflowRunSourceDbModel>().Add(new() {WorkflowRunId = fixture.RunId, Module = "acquisition",
                    EntityKind = "task", EntityId = task.Id.ToString(), Revision = task.PlanRevision.ToString()});
                await _db.SaveChangesAsync();
                break;
        }
        var json = JsonSerializer.Serialize(plan, WorkflowJson.Options);
        await _db.AcquisitionTasks.Where(t => t.Id == task.Id).ExecuteUpdateAsync(u => u.SetProperty(t => t.PlanJson, json));
        page = await search.SearchAsync(filter);
        var expected = variant == "missingLocalFile" ? 1 : 0;
        Assert.AreEqual(expected, page.FactCounts["downloadedPending"]);
        Assert.AreEqual(expected, page.TotalCount);
    }
}
