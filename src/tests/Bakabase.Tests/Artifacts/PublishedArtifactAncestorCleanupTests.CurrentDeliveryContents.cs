using System.Text.Json;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain.Constants;
using Bakabase.Service.Components.Acquisition.Planning;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Tests.Artifacts;

public sealed partial class PublishedArtifactAncestorCleanupTests
{
    [DataTestMethod, DataRow("renamed"), DataRow("moved-up"), DataRow("new-file"), DataRow("empty-directory")]
    public async Task Accepted_skipped_owned_delivery_deletes_current_contents_after_manual_reorganization(string scenario)
    {
        var f = await OwnedDeliveryFixture(emptyDirectories: true);
        var extra = Path.Combine(f.Final.RootDirectory, "manually organized", "current.bin");
        Directory.CreateDirectory(Path.GetDirectoryName(extra)!);
        if (scenario == "renamed") File.Move(f.Final.Files.Single(), extra);
        if (scenario == "moved-up") File.Move(f.Final.Files.Single(), Path.Combine(f.Final.RootDirectory, "result.bin"));
        if (scenario == "new-file") await File.WriteAllTextAsync(extra, "manually extracted contents");
        var preview = await f.Service.PreviewAsync(new([f.Task.Id], false, true, true, DeleteDeliveredFiles: true));
        var selected = preview.Rows.Single().Cleanup!.Groups.Single(g => g.Kind == "publishedFiles");
        Assert.IsTrue(selected.Eligible, selected.ReasonCode); Assert.AreEqual(scenario == "new-file" ? 2 : 1, selected.FileCount);
        var result = await f.Service.DeleteAsync(new(preview.PreviewId));
        Assert.AreEqual(scenario == "new-file" ? 2 : 1, result.RemovedFiles);
        Assert.IsFalse(Directory.Exists(f.Final.RootDirectory));
        Assert.IsTrue(File.Exists(f.Source.Files.Single()));
        Assert.IsNotNull(await store.GetArtifactAsync(f.Final.Id)); Assert.IsTrue(await db.WorkflowRuns.AnyAsync(r => r.Id == 1));
    }

    [DataTestMethod, DataRow("borrowed"), DataRow("not-accepted"), DataRow("verified")]
    public async Task Current_directory_cleanup_requires_owned_root_and_both_explicit_acceptance_flags(string scenario)
    {
        var f = await OwnedDeliveryFixture();
        var extra = Path.Combine(f.Final.RootDirectory, "manual.bin"); await File.WriteAllTextAsync(extra, "keep");
        if (scenario == "borrowed")
        {
            var part = f.Plan.StageDirectories!.Parts["nested.part"];
            var plan = f.Plan with {StageDirectories = f.Plan.StageDirectories with {Parts = new()
                {["nested.part"] = part with {Delivery = part.Delivery! with {Ownership = "borrowed", BindingId = Guid.NewGuid().ToString("N")}}}}};
            await db.AcquisitionTasks.Where(t => t.Id == f.Task.Id).ExecuteUpdateAsync(u => u
                .SetProperty(t => t.PlanJson, JsonSerializer.Serialize(plan, Json)));
        }
        var preview = await f.Service.PreviewAsync(new([f.Task.Id], false, scenario != "not-accepted",
            scenario != "verified" && scenario != "not-accepted", DeleteDeliveredFiles: true));
        await f.Service.DeleteAsync(new(preview.PreviewId));
        Assert.IsTrue(File.Exists(extra));
    }

    [DataTestMethod, DataRow("file"), DataRow("task"), DataRow("resource"), DataRow("consumer"), DataRow("symlink")]
    public async Task Current_owned_delivery_rechecks_new_files_and_foreign_references_before_deletion(string scenario)
    {
        var f = await OwnedDeliveryFixture();
        var current = Path.Combine(f.Final.RootDirectory, "current.bin"); File.Move(f.Final.Files.Single(), current);
        var preview = await f.Service.PreviewAsync(new([f.Task.Id], false, true, true, DeleteDeliveredFiles: true));
        if (scenario == "file") await File.WriteAllTextAsync(Path.Combine(f.Final.RootDirectory, "later.bin"), "new after preview");
        if (scenario == "task")
        {
            db.AcquisitionTasks.Add(new() {Title = "Foreign", Status = AcquisitionStatus.Waiting,
                PreparedContextJson = JsonSerializer.Serialize(new {Input = current})});
            await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        }
        if (scenario == "resource")
        {db.ResourcesV2.Add(new() {Path = current, IsFile = true}); await db.SaveChangesAsync(); db.ChangeTracker.Clear();}
        if (scenario == "consumer") Assert.IsTrue(await store.AttachConsumerAsync(f.Final.OperationId, "late-reader", TimeSpan.FromHours(1)));
        if (scenario == "symlink") File.CreateSymbolicLink(Path.Combine(f.Final.RootDirectory, "external"), f.Source.Files.Single());
        var result = await f.Service.DeleteAsync(new(preview.PreviewId));
        Assert.AreEqual(0, result.RemovedFiles); Assert.IsTrue(File.Exists(current)); Assert.IsTrue(File.Exists(f.Source.Files.Single()));
    }

    [TestMethod]
    public async Task Current_owned_delivery_does_not_treat_execution_journals_as_ordinary_payload()
    {
        var f = await OwnedDeliveryFixture();
        var history = Path.Combine(f.Final.RootDirectory, ".bakabase-work", "old-run", "state.json");
        Directory.CreateDirectory(Path.GetDirectoryName(history)!); await File.WriteAllTextAsync(history, "execution history");
        var preview = await f.Service.PreviewAsync(new([f.Task.Id], false, true, true, DeleteDeliveredFiles: true));
        Assert.AreEqual(1, preview.CleanupFileCount);
        var result = await f.Service.DeleteAsync(new(preview.PreviewId, RemoveTaskIfCleanupFails: true));
        Assert.AreEqual(1, result.RemovedFiles); Assert.IsTrue(File.Exists(history));
        Assert.IsTrue(result.CleanupResults!.Single().ReasonCodes.Contains("CLEANUP_FILES_RETAINED"));
    }
}
