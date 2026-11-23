using System.Security.Cryptography;
using System.Text.Json;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models.Constants;
using Bakabase.InsideWorld.Business.Components.Downloader.Models.Db;
using Bakabase.InsideWorld.Business.Components.Downloader.Services;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Workflow.Components;
using Bakabase.Service.Components.Acquisition.Planning;
using Bakabase.Service.Components.CloudDownloads;
using Bakabase.Service.Components.Downloader;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Tests;

public sealed partial class AcquisitionRecoveryTests
{
    [DataTestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task Download_center_reopens_verified_mismatch_without_reading_again_and_expires_it_on_revision(bool completeNext)
    {
        var (run, target, native, _, _, provider, recovery) = await SeedRetainedDownload();
        var workset = AcquisitionRecoveryDiagnostics.ReadWorkset(run.CurrentItemJson!)!;
        run.CurrentItemJson = WorkflowItemSnapshot.Capture(workset with {Targets = [target]});
        run.Status = WorkflowRunStatus.Failed;
        native.Message = "BAIDU_DOWNLOAD_CHECKSUM_MISMATCH";
        native.Key = "original-download";
        await _db.SaveChangesAsync();
        using var cache = new MemoryCache(new MemoryCacheOptions());
        using var reopened = new MemoryCache(new MemoryCacheOptions());
        var downloads = _services.GetRequiredService<DownloadTaskService>();
        var center = new DownloadCenterExistingFiles(_db, recovery, downloads, cache);
        var fresh = new DownloadCenterExistingFiles(_db, recovery, downloads, reopened);
        var site = $"site:{native.Id}";
        provider.VerificationFailure = "BAIDU_DOWNLOAD_CHECKSUM_MISMATCH";
        provider.VerificationLocation = new(1, 1, new(3, 1, 2, 2));
        var preview = await center.PreviewAsync(site, default);
        Assert.IsTrue(preview.CanVerify, preview.ReasonCode);
        var controller = new Bakabase.Service.Controllers.DownloadCenterController(null!);
        var response = await controller.VerifyExistingFiles(site, new(preview.PreviewToken!), center, default);
        Assert.AreNotEqual(0, response.Code);
        Assert.AreEqual(provider.VerificationFailure, response.Message);
        Assert.AreEqual(native.Id, response.Data!.SiteTaskId);
        Assert.IsNull(response.Data.DownloadResultId);
        var problem = response.Data.VerificationProblem!;
        Assert.AreEqual(provider.VerificationLocation, problem.Location);
        Assert.AreEqual(problem, (await center.VerifyAsync(site, preview.PreviewToken!, default)).VerificationProblem);
        var last = await fresh.PreviewAsync(site, default);
        Assert.IsTrue(last.CanVerify);
        Assert.AreEqual(problem, last.VerificationProblem);
        Assert.AreEqual(1, provider.VerificationCalls);
        Assert.IsFalse(await _db.DownloadResults.AnyAsync());
        Assert.AreEqual(DownloadTaskDbModelStatus.Failed, (await _db.DownloadTasks.AsNoTracking().SingleAsync()).Status);
        (await _db.AcquisitionTasks.SingleAsync()).PlanRevision++;
        await _db.SaveChangesAsync();
        var revised = await fresh.PreviewAsync(site, default);
        Assert.IsNull(revised.VerificationProblem);
        Assert.AreEqual(1, provider.VerificationCalls);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => center.VerifyAsync(site, preview.PreviewToken!, default));
        Assert.IsTrue(revised.CanVerify);
        if (completeNext)
        {
            provider.VerificationFailure = null;
            var path = Path.Combine(native.DownloadPath, "retained-fixture");
            var bytes = await File.ReadAllBytesAsync(path);
            provider.ExistingProof = new([new(path, bytes.Length, Convert.ToHexString(SHA256.HashData(bytes)))]);
            Assert.IsTrue((await fresh.VerifyAsync(site, revised.PreviewToken!, default)).DownloadResultId > 0);
        }
        else
        {
            provider.VerificationLocation = null;
            provider.VerificationFailure = "BAIDU_MANIFEST_FAILED";
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => fresh.VerifyAsync(site, revised.PreviewToken!, default));
        }
        Assert.IsNull((await _db.DownloadTasks.AsNoTracking().SingleAsync()).LastFileVerificationJson);
    }

    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Download_center_verifies_only_selected_native_record_and_replays_receipt(bool workflowWithoutAcquisition)
    {
        var (run, target, native, operation, transfer, provider, recovery) = await SeedRetainedDownload();
        if (workflowWithoutAcquisition) _db.AcquisitionTasks.Remove(await _db.AcquisitionTasks.SingleAsync());
        native.Message = "Choose a folder inside a mounted storage location.";
        native.Key = "original";
        var sibling = new DownloadTaskDbModel {Key = "other", Status = DownloadTaskDbModelStatus.Failed, ThirdPartyId = native.ThirdPartyId,
            DownloadPath = Path.Combine(_root, "other"), Options = "{}", Message = "LOCAL_STORAGE_UNAVAILABLE"};
        _db.DownloadTasks.Add(sibling); await _db.SaveChangesAsync();
        var workset = AcquisitionRecoveryDiagnostics.ReadWorkset(run.CurrentItemJson!)!;
        run.CurrentItemJson = WorkflowItemSnapshot.Capture(workset with {Targets = [
            target with {DownloadOperationId = null}, target with {Id = "other", DownloadTaskId = sibling.Id}]});
        run.Status = WorkflowRunStatus.Failed; run.WaitPromptJson = null;
        await _db.SaveChangesAsync();
        var before = JsonSerializer.Serialize(await _db.WorkflowRuns.AsNoTracking().SingleAsync());
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var center = new DownloadCenterExistingFiles(_db, recovery, _services.GetRequiredService<DownloadTaskService>(), cache);
        var siteId = $"site:{native.Id}";
        var preview = await center.PreviewAsync(siteId, default);
        Assert.IsTrue(preview.CanVerify, preview.ReasonCode);
        Assert.AreEqual(1, preview.FileCount); Assert.AreEqual(5L, preview.TotalBytes);
        Assert.AreEqual(0, provider.VerificationCalls);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => center.VerifyAsync($"site:{sibling.Id}", preview.PreviewToken!, default));
        var path = Path.Combine(native.DownloadPath, "retained-fixture");
        var original = await File.ReadAllBytesAsync(path);
        provider.ExistingProof = new([new(path, original.Length, Convert.ToHexString(SHA256.HashData(original)))]);
        var result = await center.VerifyAsync(siteId, preview.PreviewToken!, default);
        Assert.AreEqual(result, await center.VerifyAsync(siteId, preview.PreviewToken!, default));
        Assert.AreEqual(1, provider.VerificationCalls);
        Assert.AreEqual(0, provider.RetainedInspectionCalls);
        Assert.AreEqual(native.Id, (await _db.DownloadResults.AsNoTracking().SingleAsync(r => r.Id == result.DownloadResultId)).DownloadTaskId);
        Assert.AreEqual(DownloadTaskDbModelStatus.Complete, (await _db.DownloadTasks.AsNoTracking().SingleAsync(d => d.Id == native.Id)).Status);
        Assert.AreEqual(DownloadTaskDbModelStatus.Failed, (await _db.DownloadTasks.AsNoTracking().SingleAsync(d => d.Id == sibling.Id)).Status);
        Assert.AreEqual(before, JsonSerializer.Serialize(await _db.WorkflowRuns.AsNoTracking().SingleAsync()));
        Assert.AreEqual(2, await _db.CloudDownloadOperations.CountAsync());
        Assert.AreEqual("started", (await _db.CloudDownloadOperations.AsNoTracking().SingleAsync(o => o.Id == operation.Id)).State);
        Assert.AreEqual("succeeded", (await _db.CloudDownloadOperations.AsNoTracking().SingleAsync(o => o.Id == transfer.Id)).State);
        CollectionAssert.AreEqual(original, await File.ReadAllBytesAsync(path));
    }

    [DataTestMethod]
    [DataRow("incomplete")]
    [DataRow("no-inventory")]
    [DataRow("overlapping-folder")]
    [DataRow("manual-native")]
    [DataRow("provider-task")]
    [DataRow("owner-deleted")]
    [DataRow("run-advanced")]
    [DataRow("shared-site")]
    [DataRow("foreign-slot")]
    public async Task Download_center_rejects_unbound_or_incomplete_existing_groups(string change)
    {
        var (run, target, native, operation, _, provider, recovery) = await SeedRetainedDownload();
        var workset = AcquisitionRecoveryDiagnostics.ReadWorkset(run.CurrentItemJson!)!;
        run.Status = WorkflowRunStatus.Failed;
        run.CurrentItemJson = WorkflowItemSnapshot.Capture(workset with {Targets = change == "shared-site"
            ? [target, target with {Id = "other-target"}] : [target]});
        switch (change)
        {
            case "incomplete": provider.ExistingInspectionFailure = "BAIDU_EXISTING_DOWNLOAD_UNVERIFIED"; break;
            case "no-inventory": provider.IncompleteInventory = true; break;
            case "overlapping-folder": _db.DownloadTasks.Add(new() {Key = "nested", ThirdPartyId = native.ThirdPartyId,
                DownloadPath = Path.Combine(native.DownloadPath, "subdirectory"), Status = DownloadTaskDbModelStatus.Failed}); break;
            case "manual-native": _db.CloudDownloadOperations.Remove(operation); break;
            case "owner-deleted": (await _db.AcquisitionTasks.SingleAsync()).DeletedAt = DateTime.Now; break;
            case "run-advanced": run.Status = WorkflowRunStatus.Running; break;
            case "foreign-slot": (await _db.ArtifactOperationBindings.SingleAsync(b => b.OperationId == operation.Id)).OwnerId = "different"; break;
        }
        await _db.SaveChangesAsync();
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var center = new DownloadCenterExistingFiles(_db, recovery, _services.GetRequiredService<DownloadTaskService>(), cache);
        var preview = await center.PreviewAsync(change == "provider-task" ? $"http:{native.Id}" : $"site:{native.Id}", default);
        Assert.IsFalse(preview.CanVerify); Assert.IsNull(preview.PreviewToken);
        if (change == "incomplete") Assert.AreEqual(provider.ExistingInspectionFailure, preview.ReasonCode);
        Assert.AreEqual(0, provider.VerificationCalls); Assert.AreEqual(0, provider.RetainedInspectionCalls);
        Assert.AreEqual(0, await _db.DownloadResults.CountAsync());
    }

    [DataTestMethod]
    [DataRow("operation-after-preview")]
    [DataRow("owner-during-verify")]
    [DataRow("bytes-during-verify")]
    public async Task Download_center_confirmation_rechecks_ownership_and_actual_file_contents(string change)
    {
        var (run, target, native, operation, _, provider, recovery) = await SeedRetainedDownload();
        var workset = AcquisitionRecoveryDiagnostics.ReadWorkset(run.CurrentItemJson!)!;
        run.CurrentItemJson = WorkflowItemSnapshot.Capture(workset with {Targets = [target]});
        run.Status = WorkflowRunStatus.Failed;
        await _db.SaveChangesAsync();
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var center = new DownloadCenterExistingFiles(_db, recovery, _services.GetRequiredService<DownloadTaskService>(), cache);
        var site = $"site:{native.Id}";
        var preview = await center.PreviewAsync(site, default);
        Assert.IsTrue(preview.CanVerify, preview.ReasonCode);
        var path = Path.Combine(native.DownloadPath, "retained-fixture");
        var before = await File.ReadAllBytesAsync(path);
        provider.ExistingProof = new([new(path, before.Length, Convert.ToHexString(SHA256.HashData(before)))]);
        switch (change)
        {
            case "operation-after-preview": operation.RequestJson += " "; await _db.SaveChangesAsync(); break;
            case "owner-during-verify": provider.DuringVerification = async () =>
                { (await _db.AcquisitionTasks.SingleAsync()).PlanRevision++; await _db.SaveChangesAsync(); }; break;
            case "bytes-during-verify": provider.DuringVerification = () => File.WriteAllTextAsync(path, "other"); break;
        }
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => center.VerifyAsync(site, preview.PreviewToken!, default));
        Assert.AreEqual(0, await _db.DownloadResults.CountAsync());
        Assert.AreEqual(DownloadTaskDbModelStatus.Failed, (await _db.DownloadTasks.AsNoTracking().SingleAsync()).Status);
        Assert.AreEqual(0, provider.RetainedInspectionCalls);
    }
}
