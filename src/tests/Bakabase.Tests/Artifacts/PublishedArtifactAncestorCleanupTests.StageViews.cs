using System.Text.Json;
using Bakabase.Service.Components.Acquisition.Planning;
using Bakabase.Service.Components.FileProcessing;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Tests.Artifacts;

public sealed partial class PublishedArtifactAncestorCleanupTests
{
    [DataTestMethod]
    [DataRow("unregistered")]
    [DataRow("asserted-receipt")]
    [DataRow("malformed")]
    public async Task Unmanaged_processed_views_do_not_release_references_without_a_verified_stage_commit(string situation)
    {
        var f = await DeletionFixture();
        var view = await Artifact("processedFiles", f.Final);
        var metadata = situation switch
        {
            "asserted-receipt" => JsonSerializer.Serialize(new {stageCommit = new {taskId = f.Task.Id,
                bindingId = Guid.NewGuid().ToString("N"), artifactId = view.Id, completed = true}}),
            "malformed" => "{",
            _ => view.MetadataJson
        };
        await db.Artifacts.Where(a => a.Id == view.Id).ExecuteUpdateAsync(set =>
            set.SetProperty(a => a.Managed, false).SetProperty(a => a.MetadataJson, metadata));
        var consent = new ArtifactDeliveryCleanupConsent(Guid.NewGuid().ToString("N"), DateTimeOffset.UtcNow,
            f.Task.Id, f.Task.PlanRevision, 1, true, [1],
            [new(f.Final.Id, f.Final.OperationId, f.Final.Fingerprint, f.Final.RootDirectory)], true, true);

        var releasable = await AcquisitionTaskCleanupViews.ReadAsync(db, store, consent, default, new(root, new Storage()));

        Assert.IsFalse(releasable.Contains(view.Id), "An unowned view cannot authorize deletion of its inputs.");
        Assert.IsTrue(File.Exists(view.Files.Single()));
        Assert.IsTrue(File.Exists(f.Final.Files.Single()));
    }
}
