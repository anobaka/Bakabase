using System.Text.Json;
using Bakabase.InsideWorld.Business.Components.Downloader.Models.Db;
using Bakabase.Modules.Downloader.Cloud;
using Bakabase.Service.Components.CloudDownloads;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Tests;

public sealed partial class CloudDownloadDestinationTests
{
    private Task<CloudDownloadDestinationPreview> PreviewSource(bool explicitReview = true, string? requested = null,
        Func<string, Task>? pathGuard = null) => Service.PreviewAsync(_run.Id, _target, _oldRoot, _newRoot,
            requested ?? NewDirectory, default, explicitReview, pathGuard);

    private Task<CloudDownloadDestinationRevisionReceipt> ConfirmSource(CloudDownloadDestinationPreview preview, string id) =>
        Service.ApplyAsync(_run.Id, _target, _oldRoot, _newRoot, NewDirectory, preview.Fingerprint!, id, default,
            reuseExistingFiles: true);

    private async Task<CloudDownloadDestinationRevision> PlaceRetainedSource()
    {
        Directory.CreateDirectory(_download.DownloadPath);
        await File.WriteAllTextAsync(Path.Combine(_download.DownloadPath, "retained"), "original");
        var review = await PreviewSource();
        Assert.IsTrue(review.CanRevise, review.ReasonCode);
        return (await ConfirmSource(review, Guid.NewGuid().ToString("N"))).Revision;
    }

    [TestMethod]
    public async Task ReapprovedInventoryKeepsOriginalSourceAndBothLayoutsAndPreservesPriorReceipts()
    {
        var placement = await PlaceRetainedSource();
        var originalFingerprint = _provider.ObservedFingerprint;
        var actualLayout = _operation.Id + ":destination:" + placement.RevisionId;
        var source = _download.DownloadPath;
        var count = _provider.ReuseInspections;
        var availability = await PreviewSource(false, Path.Combine(_newRoot, "name-from-new-preferences"));
        Assert.IsFalse(availability.CanRevise);
        Assert.IsTrue(availability.CanPreviewReuse);
        Assert.AreEqual("DOWNLOAD_DESTINATION_REUSE_REFRESH_AVAILABLE", availability.ReasonCode);
        Assert.AreEqual(source, availability.PreviousDirectory);
        Assert.AreEqual(NewDirectory, availability.NewDirectory);
        Assert.AreEqual(count, _provider.ReuseInspections, "Listing the action must not inventory the remote disk.");
        Assert.AreEqual("DOWNLOAD_DESTINATION_UNCHANGED", (await PreviewSource()).ReasonCode);

        foreach (var fingerprint in new[] {new string('b', 64), new string('c', 64)})
        {
            _provider.ObservedFingerprint = fingerprint;
            var review = await PreviewSource(requested: Path.Combine(_newRoot, "new-preference"));
            Assert.AreEqual("DOWNLOAD_DESTINATION_REUSE_REFRESH_READY", review.ReasonCode);
            Assert.AreEqual(source, review.PreviousDirectory);
            Assert.AreEqual(NewDirectory, review.NewDirectory);
            Assert.AreEqual(100L, _guard.ExpectedBytes);
            Assert.AreEqual(70L, review.ReuseEstimate!.RequiredDownloadBytes);
            Assert.AreEqual(source, _provider.LastSourceInspection!.OutputDirectory);
            Assert.AreEqual(_operation.Id, _provider.LastSourceInspection.LayoutOperationId);
            Assert.IsNull(_provider.LastSourceInspection.ReuseSource);
            Assert.AreEqual(actualLayout, _provider.LastDestinationInspection!.LayoutOperationId);
            Assert.AreEqual(NewDirectory, _provider.LastDestinationInspection.OutputDirectory);
            var id = Guid.NewGuid().ToString("N");
            var changed = await ConfirmSource(review, id);
            Assert.AreEqual(placement.RevisionId, changed.Revision.RefreshOfRevisionId);
            Assert.AreEqual(fingerprint, changed.Revision.ReuseInventoryFingerprint);
            Assert.IsTrue((await ConfirmSource(review, id)).AlreadyApplied);
            Assert.AreEqual("DOWNLOAD_DESTINATION_UNCHANGED", (await PreviewSource()).ReasonCode);
        }

        var native = await _db.DownloadTasks.AsNoTracking().SingleAsync(d => d.Id == _download.Id);
        var operation = await _db.CloudDownloadOperations.AsNoTracking().SingleAsync(o => o.Id == _operation.Id);
        Assert.AreEqual(native.Options, operation.RequestJson);
        var persisted = JsonSerializer.Deserialize<CloudDownloadRequest>(native.Options!, JsonSerializerOptions.Web)!;
        Assert.AreEqual(3, persisted.DestinationRevisions!.Count);
        Assert.AreEqual(placement, persisted.DestinationRevisions[0]);
        Assert.AreEqual(originalFingerprint, placement.ReuseInventoryFingerprint);
        Assert.AreEqual(placement, CloudDownloadDestinations.RetainedSources(persisted).Single());
        var execution = CloudDownloadDestinations.ExecutionRequest(persisted);
        Assert.AreEqual(actualLayout, execution.LayoutOperationId);
        Assert.AreEqual(new CloudDownloadReuseSource(_operation.Id, source, _provider.ObservedFingerprint), execution.ReuseSource);
        Assert.AreEqual(_download.Status, native.Status);
        Assert.AreEqual(_download.Message, native.Message);
        Assert.AreEqual("original", await File.ReadAllTextAsync(Path.Combine(source, "retained")));
        Assert.IsFalse(Directory.Exists(NewDirectory));
        Assert.IsFalse(await _db.DownloadResults.AnyAsync());

        var last = persisted.DestinationRevisions[^1];
        foreach (var invalid in new[] {
            last with {RefreshOfRevisionId = persisted.DestinationRevisions[1].RevisionId},
            last with {PreviousDirectory = NewDirectory}, last with {Directory = source},
            last with {RootDirectory = _oldRoot}, last with {ReuseInventoryFingerprint = null},
            last with {ReuseInventoryFingerprint = persisted.DestinationRevisions[1].ReuseInventoryFingerprint}
        })
            Assert.ThrowsException<InvalidDataException>(() => CloudDownloadDestinations.Validate(persisted with
                {DestinationRevisions = [placement, persisted.DestinationRevisions[1], invalid]}));
    }

    [DataTestMethod]
    [DataRow("changed-source", "DOWNLOAD_DESTINATION_PREVIEW_CHANGED")]
    [DataRow("destination-checkpoint", "DOWNLOAD_DESTINATION_PROVIDER_STATE_PRESENT")]
    [DataRow("journal-while-checking-source", "DOWNLOAD_DESTINATION_PROVIDER_STATE_PRESENT")]
    [DataRow("target-file", "DOWNLOAD_DESTINATION_HAS_OUTPUT")]
    [DataRow("shared-source", "DOWNLOAD_DESTINATION_SHARED")]
    [DataRow("progress", "DOWNLOAD_DESTINATION_HAS_OUTPUT")]
    [DataRow("budget", "LOCAL_STORAGE_RESERVE_LOW")]
    public async Task ReapprovalRechecksChangedSourceOwnershipDestinationAndBudget(string change, string reason)
    {
        await PlaceRetainedSource();
        _provider.ObservedFingerprint = new('b', 64);
        var review = await PreviewSource();
        Assert.IsTrue(review.CanRevise, review.ReasonCode);
        var stored = (await _db.CloudDownloadOperations.AsNoTracking().SingleAsync(o => o.Id == _operation.Id)).RequestJson;
        switch (change)
        {
            case "changed-source": _provider.ObservedFingerprint = new('c', 64); break;
            case "destination-checkpoint": _provider.Problem = reason; break;
            case "journal-while-checking-source": _provider.DuringSourceInspection = () => _provider.Problem = reason; break;
            case "target-file":
                Directory.CreateDirectory(NewDirectory);
                await File.WriteAllTextAsync(Path.Combine(NewDirectory, "unrelated"), "keep");
                break;
            case "shared-source":
                _db.DownloadTasks.Add(new DownloadTaskDbModel {DownloadPath = Path.Combine(_download.DownloadPath, "other")});
                await _db.SaveChangesAsync(); break;
            case "progress":
                await _db.DownloadTasks.Where(d => d.Id == _download.Id).ExecuteUpdateAsync(s => s.SetProperty(d => d.Progress, 12m));
                break;
            case "budget": _guard.Reject = true; break;
        }
        try
        {
            await ConfirmSource(review, Guid.NewGuid().ToString("N"));
            Assert.Fail("A changed review must not update the persistent requests.");
        }
        catch (Exception error) when (error is InvalidOperationException or IOException)
        {
            Assert.AreEqual(reason, error is Bakabase.Abstractions.Components.FileSystem.LocalStorageException storage
                ? storage.Code : error.Message);
        }
        Assert.AreEqual(stored, (await _db.CloudDownloadOperations.AsNoTracking().SingleAsync(o => o.Id == _operation.Id)).RequestJson);
        Assert.AreEqual(stored, (await _db.DownloadTasks.AsNoTracking().SingleAsync(d => d.Id == _download.Id)).Options);
    }

    [TestMethod]
    public async Task ReapprovalRequiresAccessToTheRetainedDirectoryBeforeInspectingEitherLayout()
    {
        await PlaceRetainedSource();
        var inspections = _provider.ReuseInspections;
        var destinationChecks = _provider.Checks;
        var denied = await PreviewSource(pathGuard: p => p == _download.DownloadPath
            ? throw new UnauthorizedAccessException() : Task.CompletedTask);
        Assert.IsFalse(denied.CanRevise || denied.CanPreviewReuse);
        Assert.IsNull(denied.PreviousDirectory);
        Assert.AreEqual(inspections, _provider.ReuseInspections);
        Assert.AreEqual(destinationChecks, _provider.Checks);
    }
}
