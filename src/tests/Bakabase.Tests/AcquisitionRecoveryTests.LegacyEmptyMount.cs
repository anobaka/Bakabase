using System.Text.Json;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models.Constants;
using Bakabase.Modules.Downloader.Cloud;
using Bakabase.Service.Components.Acquisition.Planning;
using Bakabase.Service.Components.CloudDownloads;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Tests;

public sealed partial class AcquisitionRecoveryTests
{
    private const string LegacyMountFailurePrefix = "Choose a folder inside a mounted storage location. ";

    [TestMethod]
    public async Task Legacy_native_mount_failure_resumes_the_same_empty_pixeldrain_download_after_destination_change()
    {
        var (task, run, workset, download, operation, _, _) = await SeedRecoverableProvider(PixeldrainList());
        var original = JsonSerializer.Deserialize<CloudDownloadRequest>(download.Options!, JsonSerializerOptions.Web)!;
        var destination = Path.Combine(_root, "local-current");
        var revision = new CloudDownloadDestinationRevision(Guid.NewGuid().ToString("N"), original.OutputDirectory,
            destination, _root, DateTime.UtcNow, new string('A', 64));
        operation.RequestJson = download.Options = JsonSerializer.Serialize(original with {DestinationRevisions = [revision]},
            JsonSerializerOptions.Web);
        download.Message = LegacyMountFailurePrefix + original.OutputDirectory;
        download.DownloadPath = destination;
        await _db.SaveChangesAsync();

        var (recovery, reads) = PixeldrainRecovery(() => PixeldrainMetadata());
        var check = await recovery.CheckAsync(run.Id, workset.Targets.Single(), _root, false, default);
        Assert.IsTrue(check.Candidate);
        Assert.AreEqual("LOCAL_STORAGE_UNAVAILABLE", check.FailureCode);
        Assert.AreEqual(0, reads.Count);
        var diagnosis = await _diagnostics.DiagnoseAsync(task, run, default);
        Assert.IsTrue(diagnosis.Row.CanContinue, diagnosis.Row.ReasonCode);
        var signal = JsonSerializer.Deserialize<CloudSelectionSignal>(diagnosis.SignalJson, JsonSerializerOptions.Web)!;
        CollectionAssert.AreEqual(new[] {workset.Targets.Single().Id}, signal.ResumeTargetIds!);
        Assert.IsTrue(signal.RevalidateExistingDownloads == true);

        var starter = new OnlyDownloadStarter(this, run.Id);
        var service = new AcquisitionRecoveryService(_db, _diagnostics, new(), new RecordingHost(),
            downloadOnly: OnlyDownloads(recovery, starter, new()));
        var preview = await service.PreviewAsync(new([task.Id], "downloads"));
        Assert.AreEqual(1, preview.ContinuableCount, preview.Rows.Single().ReasonCode);
        var result = await service.ApplyAsync(new(preview.PreviewToken, [task.Id]));
        CollectionAssert.AreEqual(new[] {download.Id}, starter.Calls);
        CollectionAssert.AreEqual(new[] {download.Id}, result.Downloads!.Single().StartedDownloadTaskIds.ToArray());
        Assert.AreEqual(1, await _db.DownloadTasks.CountAsync());
        Assert.AreEqual(1, await _db.CloudDownloadOperations.CountAsync());
        Assert.IsFalse(Directory.Exists(original.OutputDirectory));
    }

    [DataTestMethod]
    [DataRow("other-path")]
    [DataRow("provider-text")]
    [DataRow("unbound-native-options")]
    [DataRow("malformed-history")]
    [DataRow("retained-bytes")]
    [DataRow("stopped")]
    [DataRow("equivalent-group")]
    public async Task Legacy_empty_mount_recovery_still_requires_exact_failed_native_and_empty_binding(string scenario)
    {
        var (_, run, workset, download, operation, _, _) = await SeedRecoverableProvider(PixeldrainList());
        var original = JsonSerializer.Deserialize<CloudDownloadRequest>(download.Options!, JsonSerializerOptions.Web)!;
        var destination = Path.Combine(_root, "local-current");
        var previous = scenario == "malformed-history" ? Path.Combine(_root, "other-task") : original.OutputDirectory;
        var revision = new CloudDownloadDestinationRevision(Guid.NewGuid().ToString("N"), previous,
            destination, _root, DateTime.UtcNow, new string('A', 64));
        download.Options = JsonSerializer.Serialize(original with {DestinationRevisions = [revision]}, JsonSerializerOptions.Web);
        if (scenario != "unbound-native-options") operation.RequestJson = download.Options;
        download.DownloadPath = destination;
        download.Message = scenario switch
        {
            "other-path" => LegacyMountFailurePrefix + Path.Combine(_root, "other-task"),
            "provider-text" => "Provider diagnostic: " + LegacyMountFailurePrefix + original.OutputDirectory,
            _ => LegacyMountFailurePrefix + original.OutputDirectory
        };
        if (scenario == "retained-bytes")
        {
            Directory.CreateDirectory(destination);
            await File.WriteAllTextAsync(Path.Combine(destination, ".original.part"), "partial bytes");
        }
        if (scenario == "stopped") download.Status = DownloadTaskDbModelStatus.Disabled;
        await _db.SaveChangesAsync();

        var (recovery, reads) = PixeldrainRecovery(() => PixeldrainMetadata());
        var check = await recovery.CheckAsync(run.Id, workset.Targets.Single(), _root, true, default,
            includeStopped: true, requireEquivalentGroup: scenario == "equivalent-group");
        Assert.IsFalse(check.CanRetry);
        Assert.IsFalse(check.Candidate);
        Assert.AreEqual(0, reads.Count);
    }

    [TestMethod]
    public async Task Legacy_empty_mount_failure_requires_provider_permission_and_cannot_prepare_source_replacement()
    {
        var (_, run, workset, download, _, provider, recovery) = await SeedRecoverableProvider();
        download.Message = LegacyMountFailurePrefix + download.DownloadPath;
        await _db.SaveChangesAsync();

        var target = workset.Targets.Single();
        var check = await recovery.CheckAsync(run.Id, target, _root, true, default);
        Assert.AreEqual("RECOVERY_MANUAL_REVIEW", check.ReasonCode);
        var sourceChange = await recovery.CheckNoOutputForSourceRevisionAsync(run.Id, target, _root, default);
        Assert.AreEqual("RECOVERY_MANUAL_REVIEW", sourceChange.ReasonCode);
        Assert.AreEqual(0, provider.Checks);
    }
}
