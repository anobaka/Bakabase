using System.Text.Json;
using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.Abstractions.Exceptions;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models.Constants;
using Bakabase.InsideWorld.Business.Components.Downloader.Models.Db;
using Bakabase.InsideWorld.Business.Components.Downloader.Services;
using Bakabase.Modules.Downloader.Cloud;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Service.Components.CloudDownloads;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Tests;

public sealed partial class CloudWorkflowTests
{
    [DataTestMethod]
    [DataRow("io")]
    [DataRow("denied")]
    public async Task Completed_download_read_failure_preserves_generation_without_redownload(string failure)
    {
        var target = Chosen(Source("storage-" + failure, "googleDrive"));
        var workset = Workset(target.SelectedSource!);
        var id = await Operations.EnqueueAsync(7201, target, workset.OutputDirectory, default);
        await _services.GetRequiredService<DownloadTaskService>().Stop(task => task.Id == id);
        var task = await Db.DownloadTasks.AsNoTracking().SingleAsync(row => row.Id == id);
        Directory.CreateDirectory(task.DownloadPath);
        var file = Path.Combine(task.DownloadPath, "completed.bin");
        await File.WriteAllTextAsync(file, "completed download");
        var manifest = await ArtifactManifestFiles.CaptureAsync(task.DownloadPath, [file]);
        Db.DownloadResults.Add(new DownloadResultDbModel
        {
            DownloadTaskId = id, DownloadDirectory = task.DownloadPath, Path = task.DownloadPath,
            FilesJson = JsonSerializer.Serialize(new[] {file}), Fingerprint = manifest.Fingerprint
        });
        await Db.SaveChangesAsync();
        await Db.DownloadTasks.Where(row => row.Id == id)
            .ExecuteUpdateAsync(update => update.SetProperty(row => row.Status, DownloadTaskDbModelStatus.Complete));
        var before = await Db.CloudDownloadOperations.AsNoTracking().SingleAsync();
        Exception error = failure == "io" ? new IOException("Fixture storage read failed.") :
            new UnauthorizedAccessException("Fixture read denied.");
        var operations = new CloudOperationService(Db, _services.GetServices<ICloudDownloadProvider>(),
            _services.GetRequiredService<CloudDownloaderHelper>(), _services.GetRequiredService<DownloadTaskService>(),
            _services.GetRequiredService<IUserStoragePolicy>(), _services,
            (root, files, ct) => ArtifactManifestFiles.CaptureAsync(root, files,
                path => new FileInfo(path), (_, _) => ValueTask.FromException<byte[]>(error), ct));

        await Assert.ThrowsExactlyAsync<UserStoragePathException>(() =>
            operations.EnqueueAsync(7201, target, workset.OutputDirectory, default));

        var after = await Db.CloudDownloadOperations.AsNoTracking().SingleAsync();
        Assert.AreEqual(id, after.DownloadTaskId);
        Assert.AreEqual(before.RequestJson, after.RequestJson);
        Assert.AreEqual(before.State, after.State);
        Assert.AreEqual(1, await Db.DownloadTasks.CountAsync());
        Assert.AreEqual(DownloadTaskDbModelStatus.Complete,
            (await Db.DownloadTasks.AsNoTracking().SingleAsync()).Status);
        Assert.AreEqual(0, _google.DownloadCalls);
        Assert.AreEqual("completed download", await File.ReadAllTextAsync(file));
    }

    [TestMethod]
    public async Task Completed_result_verified_change_is_distinct_from_unavailable_storage()
    {
        var directory = Path.Combine(_root, "completed-validation");
        Directory.CreateDirectory(directory);
        var file = Path.Combine(directory, "result.bin");
        await File.WriteAllTextAsync(file, "original");
        var manifest = await ArtifactManifestFiles.CaptureAsync(directory, [file]);
        var result = new DownloadResultDbModel
        {
            DownloadDirectory = directory, FilesJson = JsonSerializer.Serialize(new[] {file}),
            Fingerprint = manifest.Fingerprint
        };
        await File.WriteAllTextAsync(file, "modified");
        Assert.IsFalse(await CloudOperationService.HasValidResult(result,
            (root, files, ct) => ArtifactManifestFiles.CaptureAsync(root, files, ct), default));

        File.Delete(file);
        Assert.IsFalse(await CloudOperationService.HasValidResult(result,
            (root, files, ct) => ArtifactManifestFiles.CaptureAsync(root, files, ct), default));

        Directory.Delete(directory);
        await Assert.ThrowsExactlyAsync<UserStoragePathException>(() => CloudOperationService.HasValidResult(result,
            (root, files, ct) => ArtifactManifestFiles.CaptureAsync(root, files, ct), default));
    }
}
