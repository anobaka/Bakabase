using System.Text.Json;
using Bakabase.InsideWorld.Business;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Service.Components.Acquisition.Planning;
using Bakabase.Service.Components.FileProcessing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Tests;

public sealed partial class AcquisitionStoragePolicyTests
{
    [TestMethod]
    [DataRow("success")]
    [DataRow("missingPart")]
    [DataRow("changedPart")]
    [DataRow("shared")]
    [DataRow("active")]
    [DataRow("unknownSource")]
    [DataRow("changedPlan")]
    public async Task ExplicitCleanupOverridesOnlyRetentionAndRequiresEveryCompleteFinalPart(string scenario)
    {
        var fixture = Directory.CreateTempSubdirectory("explicit-cleanup-");
        try
        {
            var services = await Services(); await using var lifetime = (IAsyncDisposable)services;
            var plans = services.GetRequiredService<AcquisitionPlanService>();
            var db = services.GetRequiredService<BakabaseDbContext>();
            var store = services.GetRequiredService<IArtifactOperationStore>();
            var maintenance = services.GetRequiredService<ArtifactWorkspaceMaintenance>();
            var ancestors = new PublishedArtifactAncestorCleanup(db, store, services.GetRequiredService<ArchiveQuarantineService>(), maintenance: maintenance);
            var plan = Plan();
            plan = plan with {OutputDirectory = fixture.FullName, Parts = [plan.Parts[0], plan.Parts[0] with {Id = "second", SelectedSourceId = "second-source", Sources = [plan.Parts[0].Sources[0] with {Id = "second-source"}]}]};
            var task = await plans.CreatePreparedPlanAsync(plan, []);
            db.WorkflowRuns.AddRange(new WorkflowRunDbModel {Id = 500, Status = WorkflowRunStatus.Success},
                new WorkflowRunDbModel {Id = 501, Status = WorkflowRunStatus.Success});
            var row = (await db.AcquisitionTasks.FindAsync(task.Id))!;
            row.WorkflowRunId = 500; row.Status = AcquisitionStatus.Completed;
            await db.SaveChangesAsync(); db.ChangeTracker.Clear();
            var raws = new List<ArtifactRecord>(); var finals = new List<ArtifactRecord>();
            foreach (var part in plan.Parts)
            {
                var sourceRoot = Path.Combine(fixture.FullName, part.Id, "raw"); Directory.CreateDirectory(sourceRoot);
                var sourceFile = Path.Combine(sourceRoot, "content.bin"); await File.WriteAllTextAsync(sourceFile, "original " + part.Id);
                var sourceFiles = new List<string> {sourceFile};
                if (scenario == "unknownSource" && part.Id == "main")
                {var key = Path.Combine(sourceRoot, "needed.key"); await File.WriteAllTextAsync(key, "required key"); sourceFiles.Add(key);}
                var raw = await store.AdoptAsync(new(part.Id + "raw", part.Id + "raw", "download", "fixture"),
                    new(sourceRoot, sourceFiles, "download", true), "fixture");
                var finalRoot = Path.Combine(fixture.FullName, part.Id, "final"); Directory.CreateDirectory(finalRoot);
                var finalFile = Path.Combine(finalRoot, "content.bin"); File.Copy(sourceFile, finalFile);
                var final = await store.AdoptAsync(new(part.Id + "final", part.Id + "final", "files.publish", raw.Fingerprint),
                    new(finalRoot, [finalFile], "publishedFiles", false, [raw.Id]), "fixture");
                raws.Add(raw); finals.Add(final);
                var bindings = new ArtifactOperationBindingService(db);
                Assert.IsTrue(await bindings.BindAsync("workflow", "500", part.Id + ":download", raw.OperationId));
                Assert.IsTrue(await bindings.BindAsync("workflow", "500", part.Id + ":artifact.publish", final.OperationId));
            }
            var before = await db.AcquisitionTasks.AsNoTracking().SingleAsync(t => t.Id == task.Id);
            var previews = new AcquisitionStoragePolicyPreviewStore();
            var normal = await plans.PreviewWorkspaceCleanupAsync(new([task.Id]), previews, maintenance, Guard, default, ancestors);
            Assert.AreEqual(0, normal.EligibleCount, "Missing legacy delete policy remains retained without new authorization.");
            var preview = await plans.PreviewWorkspaceCleanupAsync(new([task.Id], OverrideRetention: true), previews, maintenance, Guard, default, ancestors);
            Assert.AreEqual(1, preview.EligibleCount);
            if (scenario == "unknownSource")
            {
                Assert.IsFalse(preview.Rows.Single().ArtifactIds.Contains(raws[0].Id));
                Assert.IsTrue(raws[0].Files.All(File.Exists));
                return;
            }
            if (scenario == "missingPart") File.Delete(finals[1].Files.Single());
            if (scenario == "changedPart")
            {
                var file = finals[1].Files.Single(); var stamp = File.GetLastWriteTimeUtc(file);
                await File.WriteAllTextAsync(file, new string('x', (int)new FileInfo(file).Length)); File.SetLastWriteTimeUtc(file, stamp);
            }
            if (scenario == "shared") Assert.IsTrue(await new ArtifactOperationBindingService(db).BindAsync("workflow", "501", "shared", raws[0].OperationId));
            if (scenario == "active") await store.AttachConsumerAsync(raws[0].OperationId, "active", TimeSpan.FromMinutes(1));
            if (scenario == "changedPlan") await db.AcquisitionTasks.Where(t => t.Id == task.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.PlanRevision, t => t.PlanRevision + 1));
            var outcome = await plans.ReclaimWorkspacesAsync(new(preview.PreviewId, [task.Id]), previews, maintenance, Guard, default, ancestors);
            var after = await db.AcquisitionTasks.AsNoTracking().SingleAsync(t => t.Id == task.Id);
            Assert.IsNull(after.DeletedAt); Assert.AreEqual(AcquisitionStatus.Completed, after.Status);
            Assert.AreEqual(before.PlanJson, after.PlanJson); Assert.AreEqual(before.PlanHistoryJson, after.PlanHistoryJson);
            Assert.AreEqual(before.BuilderStateJson, after.BuilderStateJson);
            if (scenario == "success")
            {
                Assert.AreEqual(1, outcome.CompletedTaskIds.Count);
                Assert.IsTrue(raws.All(r => r.Files.All(f => !File.Exists(f))));
                Assert.IsTrue(finals.All(r => r.Files.All(File.Exists)));
                Assert.AreEqual(outcome, await plans.ReclaimWorkspacesAsync(new(preview.PreviewId, [task.Id]), previews, maintenance, Guard, default, ancestors));
            }
            else
            {
                Assert.AreEqual(0, outcome.CompletedTaskIds.Count);
                Assert.IsTrue(raws[0].Files.All(File.Exists), "Source is retained when final proof or ownership changes.");
                if (scenario is "missingPart" or "changedPart") Assert.IsTrue(raws[1].Files.All(File.Exists), "All parts are verified before deleting any source.");
            }
        }
        finally {fixture.Delete(true);}
    }
}
