using System.Text.Json;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models.Constants;
using Bakabase.Service.Components.CloudDownloads;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Modules.Downloader.Cloud;
using Bakabase.Service.Components.Acquisition.Planning;
using Bakabase.Service.Components.FileProcessing;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Tests;

public sealed partial class AcquisitionReplanTests
{
    private const string SmbMarker = ".smbdeleteAAA05af4.4";

    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Download_directory_continuation_skips_only_unreceipted_Darwin_files(bool stageBinding)
    {
        var (task, run, original) = await CurrentDirectoryInput();
        var root = original.RootDirectory;
        var marker = Path.Combine(root, SmbMarker);
        var nested = Directory.CreateDirectory(Path.Combine(root, "empty-folder")).FullName;
        var markerDirectory = Directory.CreateDirectory(Path.Combine(root, ".smbdeleteZAB05af4.4")).FullName;
        var userFiles = new[] {".smbdeleteNotes.txt", ".smbdeleteREADME", ".secret",
            Path.Combine(Path.GetFileName(markerDirectory), "notes.txt")}.Select(name => Path.Combine(root, name)).ToArray();
        foreach (var path in userFiles) await File.WriteAllTextAsync(path, "user data");
        await File.WriteAllBytesAsync(marker, new byte[159]);
        var nestedMarker = Path.Combine(nested, SmbMarker);
        await File.WriteAllBytesAsync(nestedMarker, new byte[159]);

        if (stageBinding) task = await BindCurrentStage(task, "download", root);
        var revised = await Plans.ReplanAsync(task.Id, stageBinding ? StageRequest(task, run.Id, "download") :
            FreshRequest(task, run.Id, original.Id), default);
        var captured = (await Artifacts.GetArtifactAsync(revised.Plan!.Parts.Single().ContinueFrom!.ArtifactId))!;
        CollectionAssert.AreEquivalent(original.Files.Concat(userFiles).ToArray(), captured.Files.ToArray());
        CollectionAssert.AreEqual(new[] {nested}, ArtifactManifestFiles.ResolveDirectories(root, captured.Manifest).ToArray());
        Assert.IsTrue(File.Exists(marker));

        File.Delete(marker);
        File.Move(nestedMarker, Path.Combine(root, ".smbdeleteZBAacde4.4"));
        var resolver = _services.GetRequiredService<FileProcessingWorkspaceResolver>();
        await AcquisitionCurrentDirectoryFiles.ValidateSelectionAsync(captured, resolver, default);
        await File.WriteAllTextAsync(Path.Combine(root, ".user-added"), "new real input");
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            AcquisitionCurrentDirectoryFiles.ValidateSelectionAsync(captured, resolver, default));
    }

    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Explicit_download_receipt_preserves_a_real_file_even_with_Darwin_marker_name(bool stageBinding)
    {
        var (task, run, original) = await DownloadedInput(archiveFileName: SmbMarker);
        await File.WriteAllTextAsync(Path.Combine(original.RootDirectory, ".smbdeleteAAA064f4.4"), "unreceipted");
        if (stageBinding) task = await BindCurrentStage(task, "download", original.RootDirectory);
        var revised = await Plans.ReplanAsync(task.Id, stageBinding ? StageRequest(task, run.Id, "download") :
            FreshRequest(task, run.Id, original.Id), default);
        var captured = (await Artifacts.GetArtifactAsync(revised.Plan!.Parts.Single().ContinueFrom!.ArtifactId))!;
        CollectionAssert.AreEqual(original.Manifest.Entries.ToArray(), captured.Manifest.Entries.ToArray());
        Assert.IsTrue(await AcquisitionCurrentDirectoryFiles.IsOwnedAsync(Db, Artifacts, captured, task.Id, run.Id, "main", default));
        var resolver = _services.GetRequiredService<FileProcessingWorkspaceResolver>();
        await AcquisitionCurrentDirectoryFiles.ValidateSelectionAsync(captured, resolver, default);
        File.Delete(original.Files.Single());
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            AcquisitionCurrentDirectoryFiles.ValidateSelectionAsync(captured, resolver, default));
    }

    [DataTestMethod]
    [DataRow("owned")]
    [DataRow("other-part")]
    [DataRow("superseded")]
    public async Task Stage_capture_uses_the_exact_native_download_receipt_for_a_marker_named_payload(string source)
    {
        var (task, run, original) = await CurrentDirectoryInput();
        var root = Directory.CreateDirectory(Path.Combine(_root, "native")).FullName;
        var file = Path.Combine(root, SmbMarker);
        var regular = Path.Combine(root, "current.zip");
        await File.WriteAllTextAsync(file, "remote file");
        await File.WriteAllTextAsync(regular, "archive");
        var manifest = await ArtifactManifestFiles.CaptureAsync(root, [file, regular]);
        var transport = new CloudDownloadRequest("native-download", new() {Id = "share", ProviderId = "baidu",
            Url = "https://example.test/share"}, "account", null, root);
        Db.DownloadTasks.Add(new() {Id = 888, Key = "completed", DownloadPath = root,
            Status = DownloadTaskDbModelStatus.Complete, Options = JsonSerializer.Serialize(transport, WorkflowJson.Options)});
        Db.CloudDownloadOperations.Add(new() {Id = transport.OperationId, WorkflowRunId = run.Id, DownloadTaskId = 888,
            TargetId = source == "other-part" ? "other" : "main", Kind = "download", State = "succeeded"});
        Db.DownloadResults.Add(new() {DownloadTaskId = 888, DeduplicationKey = "native", DownloadDirectory = root,
            FilesJson = JsonSerializer.Serialize(new[] {file, regular}), Fingerprint = manifest.Fingerprint});
        await Db.SaveChangesAsync();
        if (source == "superseded")
        {
            Db.DownloadResults.Add(new() {DownloadTaskId = 888, DeduplicationKey = "latest", DownloadDirectory = root,
                FilesJson = JsonSerializer.Serialize(new[] {regular}), Fingerprint = (await ArtifactManifestFiles.CaptureAsync(root, [regular])).Fingerprint});
            await Db.SaveChangesAsync();
        }
        task = await BindCurrentStage(task, "download", root);
        var captured = await AcquisitionCurrentDirectoryFiles.CaptureStageAsync(Db, Artifacts, new CompositeStorage(),
            _services.GetRequiredService<FileProcessingWorkspaceResolver>(), task.Id, 2, run.Id, "main", "download",
            task.Plan!.StageDirectories!.Parts["main"].Download!, () => Task.CompletedTask, default);
        CollectionAssert.AreEquivalent(source == "owned" ? new[] {regular, file} : new[] {regular}, captured.Files.ToArray());
        Assert.IsTrue(File.Exists(original.Files.Single()));
    }

    [TestMethod]
    public async Task Marker_named_link_cannot_bypass_current_download_directory_validation()
    {
        if (OperatingSystem.IsWindows()) Assert.Inconclusive("Unprivileged Windows builds may not create symbolic links.");
        var (task, run, original) = await CurrentDirectoryInput();
        File.CreateSymbolicLink(Path.Combine(original.RootDirectory, SmbMarker), original.Files.Single());
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => Plans.ReplanAsync(task.Id,
            FreshRequest(task, run.Id, original.Id), default));
    }
}
