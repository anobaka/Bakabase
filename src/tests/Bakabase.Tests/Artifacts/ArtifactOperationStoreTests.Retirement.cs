using System.Text.Json;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Workflow.Artifacts;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Tests.Artifacts;

public sealed partial class ArtifactOperationStoreTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Retired_publication_is_inspectable_but_never_revived_when_files_exist_or_are_removed(bool removeFiles)
    {
        var registration = (await Files("retired-publication", managed: false)) with {Kind = "publishedFiles"};
        var artifact = await Adopt("retired-publication", registration);
        var original = await _db.Set<ArtifactDbModel>().AsNoTracking().SingleAsync(a => a.Id == artifact.Id);
        var operation = JsonSerializer.Serialize(await _store.GetOperationAsync(artifact.OperationId));
        await _db.Set<ArtifactDbModel>().Where(a => a.Id == artifact.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.State, ArtifactLocationState.Retired));
        if (removeFiles) Directory.Delete(registration.RootDirectory, true);

        Assert.IsNull(await _store.ResolveByIdAsync(artifact.Id));
        Assert.IsNull(await _store.ResolveAsync("intent-retired-publication"));
        var inspected = await _store.GetArtifactAsync(artifact.Id);
        Assert.IsNotNull(inspected);
        Assert.AreEqual(ArtifactLocationState.Retired, inspected.State);
        Assert.AreEqual(artifact.RootDirectory, inspected.RootDirectory);
        Assert.AreEqual(artifact.Fingerprint, inspected.Fingerprint);
        CollectionAssert.AreEqual(artifact.Manifest.Entries.ToArray(), inspected.Manifest.Entries.ToArray());
        var retained = await _db.Set<ArtifactDbModel>().AsNoTracking().SingleAsync(a => a.Id == artifact.Id);
        Assert.AreEqual(original.ManifestJson, retained.ManifestJson);
        Assert.AreEqual(original.UpdatedAt, retained.UpdatedAt, "Resolve must not probe or write a retired location.");
        Assert.AreEqual(operation, JsonSerializer.Serialize(await _store.GetOperationAsync(artifact.OperationId)));
    }

    [TestMethod]
    public async Task Retired_completed_generation_cannot_be_claimed_adopted_or_completed_again()
    {
        var request = Request("retired-generation");
        var registration = await Files("retired-generation");
        var claim = await _store.ClaimAsync(request, "owner", LeaseTime);
        var artifact = await _store.CompleteAsync(claim.Lease!, registration);
        await _db.Set<ArtifactDbModel>().Where(a => a.Id == artifact.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.State, ArtifactLocationState.Retired));
        var before = JsonSerializer.Serialize(await _store.GetOperationAsync(artifact.OperationId));

        Assert.AreEqual(ArtifactClaimStatus.NeedsReconciliation,
            (await _store.ClaimAsync(request, "retry", LeaseTime)).Status);
        Assert.AreEqual(ArtifactClaimStatus.NeedsReconciliation,
            (await _store.ClaimSameAttemptAsync(request, 1, "owner", LeaseTime)).Status);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => _store.AdoptAsync(request, registration, "retry"));
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => _store.CompleteAsync(claim.Lease!, registration));
        Assert.AreEqual(before, JsonSerializer.Serialize(await _store.GetOperationAsync(artifact.OperationId)));
        Assert.AreEqual(1, await _db.Set<ArtifactDbModel>().CountAsync());
        Assert.AreEqual("content-retired-generation", await File.ReadAllTextAsync(registration.Files[0]));
    }

    [TestMethod]
    public async Task Retired_output_refuses_new_consumers_and_stale_reader_renewals()
    {
        var artifact = await Adopt("retired-reader", await Files("retired-reader"));
        Assert.IsTrue(await _store.AttachConsumerAsync(artifact.OperationId, "prior-reader", LeaseTime));
        var run = new WorkflowRunDbModel {Status = WorkflowRunStatus.Running};
        _db.Add(run); await _db.SaveChangesAsync();
        await _db.Set<ArtifactDbModel>().Where(a => a.Id == artifact.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.State, ArtifactLocationState.Retired));
        var before = await _db.Set<ArtifactConsumerDbModel>().AsNoTracking().SingleAsync();

        Assert.IsFalse(await _store.AttachConsumerAsync(artifact.OperationId, "new-reader", LeaseTime));
        Assert.IsFalse(await _store.PinConsumerAsync(artifact.OperationId, "pinned-reader", "workflow", run.Id.ToString()));
        Assert.IsFalse(await _store.RenewExistingConsumerAsync(artifact.OperationId, "prior-reader", LeaseTime + LeaseTime));
        Assert.AreEqual(before.LeaseUntil, (await _db.Set<ArtifactConsumerDbModel>().AsNoTracking().SingleAsync()).LeaseUntil);
        Assert.IsFalse(await _store.TryQuarantineAsync(artifact.Id));
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            _store.SetLocationAsync(artifact.Id, artifact.RootDirectory, ArtifactLocationState.Available));
    }

    [TestMethod]
    public async Task Verified_quarantine_can_finish_as_retired_without_destroying_manifest_history()
    {
        var artifact = await Adopt("retired-cleanup", await Files("retired-cleanup"));
        Assert.IsTrue(await _store.TryQuarantineAsync(artifact.Id));
        File.Delete(artifact.Files[0]);
        await _store.SetLocationAsync(artifact.Id, artifact.RootDirectory, ArtifactLocationState.Retired);
        Assert.AreEqual(ArtifactLocationState.Retired, (await _store.GetArtifactAsync(artifact.Id))!.State);
        Assert.AreEqual(artifact.Fingerprint, (await _store.GetArtifactAsync(artifact.Id))!.Fingerprint);
        Assert.AreEqual(ArtifactClaimStatus.NeedsReconciliation,
            (await _store.ClaimAsync(Request("retired-cleanup"), "retry", LeaseTime)).Status);
    }
}
