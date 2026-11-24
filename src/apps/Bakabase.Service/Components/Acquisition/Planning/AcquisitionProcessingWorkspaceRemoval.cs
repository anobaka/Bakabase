using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Service.Components.FileProcessing;

namespace Bakabase.Service.Components.Acquisition.Planning;

internal sealed record ProcessingWorkspaceFile(ArtifactManifestEntry Entry, long LastWriteUtcTicks, string FileIdentity);
internal sealed record ProcessingWorkspaceSelection(string PartId, string BindingId, string Root, string Generation,
    string Evidence, IReadOnlyList<ProcessingWorkspaceFile> Files, string Token, string Phase)
{
    internal string QuarantineDirectory => Path.Combine(Root, ".bakabase-reclaim-" + Token);
    internal IReadOnlyList<ProcessingWorkspaceFile> Remaining { get; init; } = Files;
}

/// <summary>The only removable paths are producer-attested files captured at confirmation. Moving the selection
/// first allows a new file at an old name to survive retries, including a restart partway through unlinking.</summary>
internal static class AcquisitionProcessingWorkspaceRemoval
{
    internal const int PurgeBatchFiles = 128;
    private const long PurgeBatchBytes = 64L * 1024 * 1024;
    private sealed record Receipt(string BindingId, string Generation, string Evidence, string Token,
        string Phase, IReadOnlyList<ProcessingWorkspaceFile> Files);
    private static readonly JsonSerializerOptions Json = JsonSerializerOptions.Web;
    private static string ReceiptPath(string directory, string binding) => Path.Combine(directory, binding + ".json");
    private static string HeldRoot(string root, string token) => Path.Combine(root, ".bakabase-reclaim-" + token);
    private static string Held(string directory, int i) => Path.Combine(directory, i.ToString("X8"));

    internal static async Task<bool> CoversCompletedSelectionAsync(string directory, string binding, string root,
        ArtifactManifest manifest, CancellationToken ct)
    {
        if (!Guid.TryParseExact(binding, "N", out _) || manifest.Entries.Count == 0) return false;
        var path = ReceiptPath(directory, binding);
        if (!Exists(path) || new FileInfo(path).Length > 64 * 1024 * 1024) return false;
        await using var stream = File.OpenRead(path);
        var receipt = await JsonSerializer.DeserializeAsync<Receipt>(stream, Json, ct);
        if (receipt is not {Phase: "done", Files.Count: > 0} || receipt.BindingId != binding ||
            !Guid.TryParseExact(receipt.Token, "N", out _) || Exists(HeldRoot(root, receipt.Token))) return false;
        var files = receipt.Files.Select(f => f.Entry).ToHashSet();
        return files.Count == receipt.Files.Count && manifest.Entries.All(files.Contains) &&
            (new HashSet<string>(files.Select(f => f.RelativePath), FileProcessingFiles.PathComparer).Count == files.Count) &&
            ArtifactManifestFiles.Fingerprint(receipt.Files.Select(f => f.Entry).ToArray()) == receipt.Evidence;
    }

    private static bool Exists(string path)
    {
        ArtifactManifestFiles.RejectLinks(path);
        try { _ = File.GetAttributes(path); return true; }
        catch (FileNotFoundException) { return false; }
        catch (DirectoryNotFoundException) { return false; }
    }

    private static async Task<ProcessingWorkspaceFile> VerifyAsync(string path, ArtifactManifestEntry expected, CancellationToken ct)
    {
        ArtifactManifestFiles.RejectLinks(path);
        var info = new FileInfo(path);
        var stamp = info.LastWriteTimeUtc.Ticks;
        if (info.Length != expected.Length) throw new InvalidDataException("files_changed");
        var identity = ArtifactFileIdentity.Read(path);
        var capture = await ArtifactManifestFiles.CaptureAsync(Path.GetDirectoryName(path)!, [path], ct);
        info.Refresh();
        if (info.Length != expected.Length || info.LastWriteTimeUtc.Ticks != stamp || identity != ArtifactFileIdentity.Read(path) ||
            !string.Equals(capture.Entries[0].Sha256, expected.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("files_changed");
        return new(expected, stamp, identity);
    }

    internal static async Task<ProcessingWorkspaceSelection?> InspectAsync(string directory, string partId, string binding,
        string root, string generation, IReadOnlyList<ArtifactManifestEntry> evidence, CancellationToken ct,
        Func<string?, Task>? requireReferences = null)
    {
        if (!Guid.TryParseExact(binding, "N", out _) || evidence.Count == 0) return null;
        var sorted = evidence.OrderBy(f => f.RelativePath, StringComparer.Ordinal).ToArray();
        var fingerprint = ArtifactManifestFiles.Fingerprint(sorted);
        var paths = ArtifactManifestFiles.ResolveFiles(root, new(sorted, fingerprint));
        var journal = ReceiptPath(directory, binding);
        Receipt? prior = null;
        if (Exists(journal))
        {
            await using var stream = File.OpenRead(journal);
            prior = await JsonSerializer.DeserializeAsync<Receipt>(stream, Json, ct) ?? throw new InvalidDataException("files_changed");
            if (prior.Generation != generation || prior.BindingId != binding || prior.Evidence != fingerprint ||
                prior.Files == null || !Guid.TryParseExact(prior.Token, "N", out _) || prior.Phase is not ("moving" or "purging" or "done") ||
                !prior.Files.Select(f => f.Entry).SequenceEqual(sorted)) throw new InvalidDataException("files_changed");
            if (prior.Phase == "done") return null;
        }
        var files = new List<ProcessingWorkspaceFile>();
        var remaining = new List<ProcessingWorkspaceFile>();
        var token = prior?.Token ?? Guid.NewGuid().ToString("N");
        var quarantine = HeldRoot(root, token);
        if (prior == null && Exists(quarantine)) throw new IOException("files_changed");
        if (requireReferences != null) await requireReferences(prior == null ? null : quarantine);
        var absent = new AcquisitionProcessingWorkspaceAbsence(root, quarantine, ct);
        for (var i = 0; i < sorted.Length; i++)
        {
            var held = Held(quarantine, i);
            var moved = prior != null && Exists(held);
            if (prior?.Phase == "purging" && !moved)
            {
                absent.Add(i);
                files.Add(prior.Files[i]);
                continue;
            }
            var path = moved ? held : paths[i];
            var item = await VerifyAsync(path, sorted[i], ct);
            if (prior != null && prior.Files[i] != item) throw new InvalidDataException("files_changed");
            files.Add(item);
            remaining.Add(item);
        }
        absent.Flush();
        return new(partId, binding, root, generation, fingerprint, files, token, prior?.Phase ?? "new") {Remaining = remaining};
    }

    internal static bool Same(ProcessingWorkspaceSelection old, ProcessingWorkspaceSelection current) =>
        old.PartId == current.PartId && old.BindingId == current.BindingId && old.Root == current.Root &&
        old.Generation == current.Generation && old.Evidence == current.Evidence && old.Files.SequenceEqual(current.Files) &&
        old.Remaining.SequenceEqual(current.Remaining) && old.Phase == current.Phase &&
        (old.Phase == "new" || old.Token == current.Token);

    internal static async Task<ArtifactWorkspaceCleanupResult> RemoveAsync(string directory, ProcessingWorkspaceSelection selection,
        CancellationToken ct, Func<CancellationToken, Task>? retire = null,
        Func<CancellationToken, Task>? beforePurgeBatch = null)
    {
        var root = selection.Root;
        var quarantine = selection.QuarantineDirectory;
        var journal = ReceiptPath(directory, selection.BindingId);
        var phase = selection.Phase;
        var files = 0;
        long bytes = 0;
        var receipt = new Receipt(selection.BindingId, selection.Generation, selection.Evidence, selection.Token, phase, selection.Files);
        try
        {
            if (phase == "new")
            {
                if (Exists(quarantine) || Exists(journal)) throw new IOException("files_changed");
                await SaveAsync(journal, receipt with {Phase = "moving"}, false, ct);
                phase = "moving";
            }
            if (phase == "moving")
            {
                ArtifactManifestFiles.RejectLinks(quarantine);
                if (OperatingSystem.IsWindows()) Directory.CreateDirectory(quarantine);
                else Directory.CreateDirectory(quarantine, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
                for (var i = 0; i < selection.Files.Count; i++)
                {
                    var item = selection.Files[i];
                    var source = AcquisitionStageDirectoryFileMove.Resolve(root, item.Entry.RelativePath);
                    var held = Held(quarantine, i);
                    if (Exists(held)) continue;
                    ArtifactManifestFiles.RejectLinks(source);
                    if (new FileInfo(source).LastWriteTimeUtc.Ticks != item.LastWriteUtcTicks || ArtifactFileIdentity.Read(source) != item.FileIdentity) throw new IOException("files_changed");
                    File.Move(source, held, false);
                    try
                    {
                        if (await VerifyAsync(held, item.Entry, ct) != item) throw new IOException("files_changed");
                    }
                    catch
                    {
                        ArtifactManifestFiles.RejectLinks(source);
                        if (!Exists(source)) File.Move(held, source, false);
                        throw;
                    }
                }
                await SaveAsync(journal, receipt with {Phase = "purging"}, true, ct);
            }
            var absent = new AcquisitionProcessingWorkspaceAbsence(root, quarantine, ct);
            var batchFiles = PurgeBatchFiles;
            long batchBytes = 0;
            for (var i = 0; i < selection.Files.Count; i++)
            {
                if (batchFiles >= PurgeBatchFiles || batchBytes >= PurgeBatchBytes)
                {
                    if (beforePurgeBatch != null) await beforePurgeBatch(ct);
                    batchFiles = 0;
                    batchBytes = 0;
                }
                batchFiles++;
                var held = Held(quarantine, i);
                if (!Exists(held)) { absent.Add(i); continue; }
                var item = selection.Files[i];
                if (await VerifyAsync(held, item.Entry, ct) != item) throw new IOException("files_changed");
                File.Delete(held);
                absent.Add(i);
                files++;
                bytes += item.Entry.Length;
                batchBytes += item.Entry.Length;
            }
            if (beforePurgeBatch != null) await beforePurgeBatch(ct);
            absent.Flush();
            if (Exists(quarantine) && !Directory.EnumerateFileSystemEntries(quarantine).Any()) Directory.Delete(quarantine);
            absent.VerifyBeforeRetirement(selection.Files.Count);
            if (retire != null) await retire(ct);
            await SaveAsync(journal, receipt with {Phase = "done"}, true, ct);
            return new(selection.BindingId, true, null, files, bytes);
        }
        catch (ProcessingWorkspaceReferencedException)
        {
            return new(selection.BindingId, false, "artifact_busy", files, bytes);
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            return new(selection.BindingId, false, "files_changed", files, bytes);
        }
    }

    private static async Task SaveAsync(string path, Receipt receipt, bool replace, CancellationToken ct)
    {
        var parent = Path.GetDirectoryName(path)!;
        ArtifactManifestFiles.RejectLinks(parent);
        Directory.CreateDirectory(parent);
        ArtifactManifestFiles.RejectLinks(path);
        var pending = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var stream = new FileStream(pending, FileMode.CreateNew, FileAccess.Write, FileShare.None, 8192,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, receipt, Json, ct);
                await stream.FlushAsync(ct);
                stream.Flush(true);
            }
            File.Move(pending, path, replace);
        }
        finally { File.Delete(pending); }
    }
}
