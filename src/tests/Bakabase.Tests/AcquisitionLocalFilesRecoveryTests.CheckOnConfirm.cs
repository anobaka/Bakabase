using System.Text.Json;
using System.Text.Json.Nodes;
using Bakabase.Service.Components.Acquisition.Planning;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Tests;

public sealed partial class AcquisitionRecoveryTests
{
    [DataTestMethod]
    [DataRow(false, false)]
    [DataRow(false, true)]
    [DataRow(true, false)]
    [DataRow(true, true)]
    public async Task LocalFiles_explicit_preview_can_inspect_a_locked_payload_and_both_confirmations_require_its_bytes(
        bool alreadyPrepared, bool prepareOnly)
    {
        var f = await LocalFilesFixture();
        if (alreadyPrepared)
        {
            var initial = await f.Recovery.PreviewAsync(new([f.TaskId], "localFiles"));
            var prepared = await f.Recovery.ApplyAsync(new(initial.PreviewToken, [f.TaskId], PrepareOnly: true));
            Assert.AreEqual("prepared", prepared.LocalFiles!.Single().Status);
        }
        var originalTask = await _db.AcquisitionTasks.AsNoTracking().SingleAsync();
        var originalRun = JsonSerializer.Serialize(await _db.WorkflowRuns.AsNoTracking().SingleAsync());
        var download = await _db.DownloadResults.AsNoTracking().SingleAsync();
        var file = JsonSerializer.Deserialize<string[]>(download.FilesJson)!.First();
        var count = await _db.Artifacts.CountAsync();
        AcquisitionRecoveryPreview preview;
        using (File.Open(file, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            preview = await f.Recovery.PreviewAsync(new([f.TaskId], "localFiles"));
        Assert.AreEqual(1, preview.ContinuableCount, preview.Rows.Single().ReasonCode);
        Assert.AreEqual("RECOVERY_LOCAL_FILES_CHECK_ON_CONFIRM", preview.Rows.Single().ReasonCode);
        Assert.IsTrue(preview.Rows.Single().LocalFiles!.SupportsPrepareOnly);
        var bytes = await File.ReadAllBytesAsync(file);
        bytes[^1] ^= 1;
        await File.WriteAllBytesAsync(file, bytes);
        var result = await f.Recovery.ApplyAsync(new(preview.PreviewToken, [f.TaskId], PrepareOnly: prepareOnly));
        Assert.AreEqual("RECOVERY_FILES_UNVERIFIED", result.Skipped.Single().ReasonCode);
        Assert.AreEqual(0, f.Host.Calls);
        Assert.AreEqual(count, await _db.Artifacts.CountAsync());
        Assert.AreEqual(JsonSerializer.Serialize(originalTask),
            JsonSerializer.Serialize(await _db.AcquisitionTasks.AsNoTracking().SingleAsync()));
        Assert.AreEqual(originalRun, JsonSerializer.Serialize(await _db.WorkflowRuns.AsNoTracking().SingleAsync()));
    }

    [DataTestMethod]
    [DataRow("missing")]
    [DataRow("prepared-missing")]
    [DataRow("prepared-length")]
    [DataRow("no-result")]
    [DataRow("unusable-fingerprint")]
    [DataRow("replaced-source")]
    [DataRow("shared")]
    public async Task LocalFiles_check_on_confirmation_requires_owned_registered_files_to_be_present(string damage)
    {
        var f = await LocalFilesFixture();
        if (damage.StartsWith("prepared-", StringComparison.Ordinal))
        {
            var original = await f.Recovery.PreviewAsync(new([f.TaskId], "localFiles"));
            Assert.AreEqual("prepared", (await f.Recovery.ApplyAsync(new(original.PreviewToken, [f.TaskId], PrepareOnly: true))).LocalFiles!.Single().Status);
        }
        var result = await _db.DownloadResults.AsNoTracking().SingleAsync();
        var file = JsonSerializer.Deserialize<string[]>(result.FilesJson)!.First();
        switch (damage)
        {
            case "missing" or "prepared-missing": File.Delete(file); break;
            case "prepared-length": await File.AppendAllTextAsync(file, "more"); break;
            case "no-result": await _db.DownloadResults.ExecuteDeleteAsync(); break;
            case "unusable-fingerprint": await _db.DownloadResults.ExecuteUpdateAsync(s => s.SetProperty(r => r.Fingerprint, "damaged")); break;
            case "replaced-source":
                var operation = await _db.CloudDownloadOperations.AsNoTracking().SingleAsync();
                var request = JsonNode.Parse(operation.RequestJson)!.AsObject();
                request["source"]!["url"] = "https://pan.baidu.com/s/different";
                var json = request.ToJsonString();
                await _db.CloudDownloadOperations.ExecuteUpdateAsync(s => s.SetProperty(o => o.RequestJson, json));
                await _db.DownloadTasks.ExecuteUpdateAsync(s => s.SetProperty(d => d.Options, json));
                break;
            case "shared":
                _db.ArtifactOperationBindings.Add(new() {Id = Guid.NewGuid().ToString("N"), OwnerKind = "workflow", OwnerId = "999",
                    Slot = "part:download", OperationId = (await _db.CloudDownloadOperations.AsNoTracking().SingleAsync()).Id});
                await _db.SaveChangesAsync();
                break;
        }
        var preview = await f.Recovery.PreviewAsync(new([f.TaskId], "localFiles"));
        Assert.IsFalse(preview.Rows.Single().CanContinue);
        Assert.AreEqual(0, preview.ContinuableCount);
        Assert.IsNull(preview.Rows.Single().LocalFiles);
        Assert.AreEqual(0, f.Host.Calls);
    }

    [TestMethod]
    public async Task LocalFiles_confirmation_rejects_relocated_prepared_input_even_if_its_contents_match()
    {
        var f = await LocalFilesFixture();
        var initial = await f.Recovery.PreviewAsync(new([f.TaskId], "localFiles"));
        Assert.AreEqual("prepared", (await f.Recovery.ApplyAsync(new(initial.PreviewToken, [f.TaskId], PrepareOnly: true))).LocalFiles!.Single().Status);
        var preview = await f.Recovery.PreviewAsync(new([f.TaskId], "localFiles"));
        var reviewed = preview.Rows.Single().LocalFiles!.Parts.Single();
        var root = Directory.CreateDirectory(Path.Combine(_root, "other-source")).FullName;
        var paths = JsonSerializer.Deserialize<string[]>((await _db.DownloadResults.AsNoTracking().SingleAsync()).FilesJson)!;
        foreach (var file in paths)
        {
            var relocated = Path.Combine(root, Path.GetRelativePath(reviewed.SourceDirectory, file));
            Directory.CreateDirectory(Path.GetDirectoryName(relocated)!);
            File.Copy(file, relocated);
        }
        await _db.Artifacts.Where(a => a.Id == reviewed.ArtifactId).ExecuteUpdateAsync(s => s.SetProperty(a => a.RootPath, root));
        var applied = await f.Recovery.ApplyAsync(new(preview.PreviewToken, [f.TaskId]));
        Assert.AreEqual("RECOVERY_FILES_UNVERIFIED", applied.Skipped.Single().ReasonCode);
        Assert.AreEqual(0, f.Host.Calls);
        Assert.AreEqual(2, (await _db.AcquisitionTasks.AsNoTracking().SingleAsync()).PlanRevision);
    }

    [TestMethod]
    public async Task LocalFiles_cancelled_confirmation_and_implicit_diagnosis_do_not_adopt_the_preview()
    {
        var f = await LocalFilesFixture();
        var task = await _db.AcquisitionTasks.AsNoTracking().SingleAsync();
        var run = await _db.WorkflowRuns.AsNoTracking().SingleAsync();
        var implicitCheck = await f.Local.DiagnoseAsync(task, run, false, default);
        Assert.IsFalse(implicitCheck.Row.CanContinue);
        Assert.AreEqual("RECOVERY_VERIFY_FILES", implicitCheck.Row.ReasonCode);
        var preview = await f.Recovery.PreviewAsync(new([f.TaskId], "localFiles"));
        using var cancelled = new CancellationTokenSource();
        await Assert.ThrowsAsync<OperationCanceledException>(() => f.Recovery.ApplyAsync(
            new(preview.PreviewToken, [f.TaskId], PrepareOnly: true), cancelled.Token,
            _ => { cancelled.Cancel(); return Task.CompletedTask; }));
        Assert.AreEqual(JsonSerializer.Serialize(task), JsonSerializer.Serialize(await _db.AcquisitionTasks.AsNoTracking().SingleAsync()));
        Assert.AreEqual(0, await _db.Artifacts.CountAsync());
        Assert.AreEqual(0, f.Host.Calls);
    }
}
