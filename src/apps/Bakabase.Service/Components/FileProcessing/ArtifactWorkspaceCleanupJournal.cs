using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.Modules.Workflow.Artifacts;

namespace Bakabase.Service.Components.FileProcessing;

/// <summary>Small metadata receipts live in AppData; payload removal is always exact-manifest based.</summary>
internal sealed class ArtifactWorkspaceCleanupJournal(string appData)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly string root = Path.Combine(Path.GetFullPath(appData), "file-processing", "workspace-cleanup-receipts");

    internal sealed record FileEntry(string Path, long Bytes, bool Removed = false);
    internal sealed record CheckpointBoundary(string Directory, string MarkerFingerprint);
    internal sealed record Receipt(string Id, string ArtifactId, string OperationId, string ResultFingerprint, string ResultDirectory,
        DateTime StartedAt, string Status, IReadOnlyList<FileEntry> Files, DateTime? FinishedAt = null,
        ArtifactDeliveryCleanupConsent? Consent = null, IReadOnlyList<string>? AlreadyAbsentPaths = null,
        string? RelatedArtifactId = null, CheckpointBoundary? CleanupCheckpoint = null)
    {
        public int RemovedFiles => Files.Count(f => f.Removed);
        public long RemovedBytes => Files.Where(f => f.Removed).Sum(f => f.Bytes);
    }

    public async Task<Receipt> BeginAsync(ArtifactRecord artifact, IReadOnlyDictionary<string, long> files, CancellationToken ct,
        ArtifactDeliveryCleanupConsent? consent = null, string? relatedArtifactId = null, string? checkpointDirectory = null)
    {
        CheckpointBoundary? checkpoint = null;
        if (checkpointDirectory != null)
        {
            if (consent?.DeliveryAccepted != true || files.Keys.Any(path => !FileProcessingPlanExecutor.IsInside(checkpointDirectory, path)))
                throw new InvalidDataException("The accepted cleanup files must belong to their retained checkpoint.");
            checkpoint = await CaptureCheckpointAsync(checkpointDirectory, ct);
        }
        var receipt = new Receipt(Guid.NewGuid().ToString("N"), artifact.Id, artifact.OperationId,
            artifact.Fingerprint, Encode(artifact.RootDirectory), DateTime.UtcNow, "Prepared",
            files.Select(f => new FileEntry(Encode(f.Key), f.Value)).ToArray(), Consent: consent,
            RelatedArtifactId: relatedArtifactId, CleanupCheckpoint: checkpoint);
        await Save(receipt, ct);
        return receipt;
    }

    internal async Task RecordAlreadyAbsentAsync(ArtifactRecord artifact, IReadOnlyList<string> paths,
        ArtifactDeliveryCleanupConsent consent, CancellationToken ct)
    {
        // These bytes were not removed by this operation. Keep the full observation separately
        // from actual unlink entries so both initial confirmation and retries report zero.
        if (!ArtifactCleanupAbsence.AllConfirmedAbsent(artifact.RootDirectory, paths, ct))
            throw new IOException("The previously absent cleanup paths changed or became unavailable.");
        var now = DateTime.UtcNow;
        var receipt = new Receipt(Guid.NewGuid().ToString("N"), artifact.Id, artifact.OperationId,
            artifact.Fingerprint, Encode(artifact.RootDirectory), now, "AlreadyAbsent", [], now, consent,
            paths.Select(Encode).ToArray());
        await Save(receipt, ct);
    }

    public async Task<Receipt> FinishAsync(Receipt receipt, string status, CancellationToken ct)
    {
        // File.Exists treats disconnected/unreadable storage as missing. It cannot prove an
        // unlink. Confirm each containing directory is still readable before recording removal.
        string? boundary = null;
        if (receipt.CleanupCheckpoint is { } checkpoint)
        {
            boundary = Decode(checkpoint.Directory);
            if (receipt.Consent?.DeliveryAccepted != true || await CaptureCheckpointAsync(boundary, ct) != checkpoint)
                throw new IOException("The retained cleanup checkpoint changed or became unavailable.");
        }
        else
        {
            var resultRoot = Decode(receipt.ResultDirectory);
            ArtifactManifestFiles.RejectLinks(resultRoot);
            _ = Directory.EnumerateFileSystemEntries(resultRoot).Take(1).ToArray();
        }
        var files = receipt.Files.Select(file => file with {Removed = IsAbsent(Decode(file.Path), boundary)}).ToArray();
        if (status == "Completed" && files.Any(file => !file.Removed)) status = "Partial";
        var updated = receipt with {Status = status, Files = files, FinishedAt = DateTime.UtcNow};
        await Save(updated, ct);
        return updated;
    }

    private async Task<CheckpointBoundary> CaptureCheckpointAsync(string directory, CancellationToken ct)
    {
        ArtifactManifestFiles.RejectLinks(directory);
        _ = Directory.EnumerateFileSystemEntries(directory).Take(1).ToArray();
        var manifest = await ArtifactManifestFiles.CaptureAsync(directory, [Path.Combine(directory, "state.json")], ct);
        return new(Encode(directory), manifest.Fingerprint);
    }

    private static bool IsAbsent(string path, string? boundary)
    {
        ArtifactManifestFiles.RejectLinks(path);
        if (boundary != null && !FileProcessingPlanExecutor.IsInside(boundary, path))
            throw new InvalidDataException("The cleanup file is outside its retained checkpoint.");
        try { _ = File.GetAttributes(path); return false; }
        catch (FileNotFoundException) { }
        catch (DirectoryNotFoundException) { }
        var directory = Path.GetDirectoryName(path)!;
        // Executors remove only empty private parents; find the surviving checkpoint owner.
        while (true)
        {
            try
            {
                if (!File.GetAttributes(directory).HasFlag(FileAttributes.Directory)) return false;
                _ = Directory.EnumerateFileSystemEntries(directory).Take(1).ToArray();
                return true;
            }
            catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException)
            {
                if (boundary != null && ArtifactManifestFiles.PathComparer.Equals(directory, boundary))
                    throw new IOException("The retained cleanup checkpoint became unavailable.", error);
                if (Path.GetDirectoryName(directory) is not { } parent) return false;
                directory = parent;
            }
        }
    }

    private string Encode(string path)
    {
        var relative = Path.GetRelativePath(Path.GetFullPath(appData), path);
        return !Path.IsPathRooted(relative) && relative != ".." && !relative.StartsWith(".." + Path.DirectorySeparatorChar)
            ? "appdata:" + relative.Replace('\\', '/') : Path.GetFullPath(path);
    }
    private string Decode(string path) => path.StartsWith("appdata:", StringComparison.Ordinal)
        ? FileProcessingFiles.Within(Path.Combine(appData, path[8..]), appData) : path;

    private async Task Save(Receipt receipt, CancellationToken ct)
    {
        ArtifactManifestFiles.RejectLinks(root);
        if (OperatingSystem.IsWindows()) Directory.CreateDirectory(root);
        else Directory.CreateDirectory(root, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var destination = Path.Combine(root, receipt.Id + ".json");
        var pending = destination + ".tmp";
        ArtifactManifestFiles.RejectLinks(destination);
        ArtifactManifestFiles.RejectLinks(pending);
        var options = new FileStreamOptions {Mode = FileMode.Create, Access = FileAccess.Write,
            Share = FileShare.None, Options = FileOptions.Asynchronous | FileOptions.WriteThrough};
        if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        await using (var output = new FileStream(pending, options))
        {
            await JsonSerializer.SerializeAsync(output, receipt, Json, ct);
            await output.FlushAsync(ct);
            output.Flush(flushToDisk: true);
        }
        ct.ThrowIfCancellationRequested();
        File.Move(pending, destination, true);
    }
}
