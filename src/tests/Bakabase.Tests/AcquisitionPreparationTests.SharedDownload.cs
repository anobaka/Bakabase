using System.Text.Json;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Service.Components.Acquisition.Planning;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Tests;

public sealed partial class AcquisitionPreparationTests
{
    private async Task<AcquisitionPlanTaskDetail> RepeatedPreparedDownload(string? mismatch = null)
    {
        var origin = new WorkflowSourceRef("test", "post", "417", "4");
        var processing = new FileProcessingPlan {Requirement = "required",
            Steps = [new() {Id = "extract", Op = "extractArchive", Selector = "*.zip", Password = "archive-key"}]};
        var parts = new[] {"a", "b", "c"}.Select(id =>
        {
            var evidence = origin with {ResultKey = id};
            var selected = new AcquisitionPlanSource
            {
                Id = id + "-selected", Url = "https://example.test/shared.zip?version=1", Kind = "http",
                AccessCode = id == "a" && mismatch == "access-code" ? "different-code" : "shared-code",
                ArchivePassword = id == "a" && mismatch == "archive-password" ? "different-password" : "archive-key",
                Processing = processing, ProcessingSource = evidence, AppliedProcessingRevisionId = id + "-review"
            };
            var mirror = selected with
            {
                Id = id + "-mirror", Url = "https://example.test/mirror.zip",
                AccessCode = id == "a" && mismatch == "mirror-code" ? "different-mirror-code" : "mirror-code",
                ArchivePassword = "archive-key"
            };
            return new AcquisitionPlanPart
            {
                Id = "group-" + id, Title = "Same download", SelectedSourceId = selected.Id,
                Sources = [selected, mirror], Processing = processing, ProcessingSource = evidence,
                AppliedProcessingRevisionId = id + "-review"
            };
        }).ToList();
        return await Plans.CreatePreparedPlanAsync(new AcquisitionPlan
        {
            Title = "Repeated original links", OutputDirectory = _root, Parts = parts
        }, [origin, ..parts.Select(p => p.ProcessingSource!)]);
    }

    [TestMethod]
    public async Task SharedDownloadCollapsesSavedGroupsWithoutChangingSurvivingPlanOrDeliveryBinding()
    {
        var initial = await RepeatedPreparedDownload();
        var representative = initial.Plan!.Parts[1];
        var stageDirectories = initial.StageDirectories!.Parts[representative.Id];
        var delivery = stageDirectories.Delivery!.Directory;
        var frozen = await SavedTask(initial.Id);
        var request = new AcquisitionPreparationInput(initial.Task.PlanRevision, CheckAvailability: false);

        var preview = await Plans.PreviewPreparationAsync(initial.Id, request, default);
        Assert.IsTrue(preview.CanConfirm);
        Assert.IsNotNull(preview.SharedDownload);
        CollectionAssert.AreEqual(initial.Plan.Parts.Select(p => p.Id).ToArray(),
            preview.SharedDownload.Groups.Select(g => g.PartId).ToArray());
        Assert.AreEqual(delivery, preview.SharedDownload.Groups.Single(g => g.PartId == representative.Id).DeliveryDirectory);
        Assert.IsTrue(preview.SharedDownload.CanUseArchiveDirectories);
        Assert.IsFalse(preview.SharedDownload.AlreadyUsesArchiveDirectories);
        Assert.AreEqual(frozen, await SavedTask(initial.Id));

        var revised = await Plans.ConfirmPreparationAsync(initial.Id, request with
        {
            SharedDownload = new(representative.Id, UseArchiveDirectories: true, ExpectedFingerprint: preview.SharedDownload.Fingerprint)
        }, default);
        Assert.AreEqual(initial.Task.PlanRevision + 1, revised.Task.PlanRevision);
        Assert.AreEqual(JsonSerializer.Serialize(representative), JsonSerializer.Serialize(revised.Plan!.Parts.Single()),
            "Keep the complete representative configuration and provenance, including the alternative and processing revisions.");
        Assert.AreEqual(JsonSerializer.Serialize(stageDirectories), JsonSerializer.Serialize(revised.StageDirectories!.Parts[representative.Id]));
        Assert.AreEqual(delivery, revised.Plan.DeliveryLayout!.PartDirectories.Single().Value);
        CollectionAssert.AreEqual(initial.SourceRefs.ToArray(), revised.SourceRefs.ToArray());
        Assert.AreEqual(3, revised.PlanHistory.Single(h => h.Revision == initial.Task.PlanRevision).Plan.Parts.Count);
        Assert.AreEqual(FileExtractionLayouts.ArchiveDirectory, revised.Plan.ExtractionLayout);
        Assert.IsNull(revised.Plan.Parts.Single().Processing!.Steps.Single().ExtractionLayout);
        var next = await Plans.PreviewPreparationAsync(initial.Id, new(revised.Task.PlanRevision, CheckAvailability: false), default);
        Assert.IsNull(next.SharedDownload);
        Assert.AreEqual(1, next.Groups.Count);
        Assert.IsNull(revised.Task.WorkflowRunId);
        Assert.AreEqual(0, await Db.WorkflowRuns.CountAsync());
        Assert.AreEqual(0, await Db.DownloadTasks.CountAsync());
        Assert.AreEqual(0, Directory.GetFileSystemEntries(_root).Length);
    }

    [DataTestMethod]
    [DataRow("access-code")]
    [DataRow("archive-password")]
    [DataRow("mirror-code")]
    public async Task SharedDownloadRequiresIdenticalFrozenCredentialsForSelectedSourceAndAlternatives(string mismatch)
    {
        var initial = await RepeatedPreparedDownload(mismatch);
        var frozen = await SavedTask(initial.Id);
        var request = new AcquisitionPreparationInput(initial.Task.PlanRevision, CheckAvailability: false);
        var preview = await Plans.PreviewPreparationAsync(initial.Id, request, default);
        Assert.IsTrue(preview.CanConfirm, "Ordinary preparation remains available for distinct sources.");
        Assert.IsNull(preview.SharedDownload);
        var refused = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => Plans.ConfirmPreparationAsync(initial.Id,
            request with {SharedDownload = new("group-b", ExpectedFingerprint: new string('a', 64))}, default));
        Assert.AreEqual("SHARED_DOWNLOAD_UNAVAILABLE", refused.Message);
        Assert.AreEqual(frozen, await SavedTask(initial.Id));
    }

    [DataTestMethod]
    [DataRow("payload")]
    [DataRow("wrong-fingerprint")]
    [DataRow("previous-revision")]
    [DataRow("historical-run")]
    public async Task SharedDownloadConfirmationRechecksThePreparedTaskAndItsFolders(string changed)
    {
        var initial = await RepeatedPreparedDownload();
        var previewRequest = new AcquisitionPreparationInput(initial.Task.PlanRevision, CheckAvailability: false);
        var preview = await Plans.PreviewPreparationAsync(initial.Id, previewRequest, default);
        Assert.IsNotNull(preview.SharedDownload);
        var request = previewRequest with
            {SharedDownload = new("group-b", ExpectedFingerprint: preview.SharedDownload.Fingerprint)};
        string? file = null;
        switch (changed)
        {
            case "payload":
                var removed = initial.StageDirectories!.Parts["group-c"].Delivery!.Directory;
                Directory.CreateDirectory(removed);
                file = Path.Combine(removed, "manual.txt");
                await File.WriteAllTextAsync(file, "keep this existing file");
                break;
            case "wrong-fingerprint":
                request = request with {SharedDownload = request.SharedDownload! with {ExpectedFingerprint = new string('0', 64)}};
                break;
            case "previous-revision":
                request = request with {ExpectedRevision = initial.Task.PlanRevision - 1};
                break;
            case "historical-run":
                Db.WorkflowRuns.Add(new WorkflowRunDbModel {Sources =
                    [new() {Module = "acquisition", EntityKind = "task", EntityId = initial.Id.ToString(), Revision = "prior"}]});
                await Db.SaveChangesAsync();
                break;
        }
        var frozen = await SavedTask(initial.Id);
        var refused = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            Plans.ConfirmPreparationAsync(initial.Id, request, default));
        StringAssert.StartsWith(refused.Message, changed switch
        {
            "payload" => "SHARED_DOWNLOAD_UNAVAILABLE",
            "historical-run" => "PLAN_ALREADY_STARTED",
            _ => "PLAN_VERSION_CONFLICT"
        });
        Assert.AreEqual(frozen, await SavedTask(initial.Id));
        if (file != null) Assert.AreEqual("keep this existing file", await File.ReadAllTextAsync(file));
        Assert.AreEqual(changed == "historical-run" ? 1 : 0, await Db.WorkflowRuns.CountAsync());
        Assert.AreEqual(0, await Db.DownloadTasks.CountAsync());
    }
}
