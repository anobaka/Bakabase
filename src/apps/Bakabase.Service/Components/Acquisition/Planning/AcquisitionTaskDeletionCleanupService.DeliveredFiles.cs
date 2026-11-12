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
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Service.Components.Acquisition.Planning;

public sealed partial class AcquisitionTaskDeletionCleanupService
{
    internal sealed record DeliveredCandidate(string ArtifactId, string OperationId, string RootDirectory,
        string Fingerprint, string SelectionFingerprint, IReadOnlyList<AcquisitionTaskDeleteCleanupFile> Files, string? Reason = null,
        IReadOnlyDictionary<string, string>? FileIdentities = null);

    private async Task<DeliveredCandidate> PreviewDeliveredFilesAsync(ArtifactRecord artifact,
        ArtifactDeliveryCleanupAuthorization authorization, ArtifactCleanupReferenceGuard references, CancellationToken ct)
    {
        DeliveredCandidate Block(string reason) => new(artifact.Id, artifact.OperationId, artifact.RootDirectory,
            artifact.Fingerprint, "", artifact.Manifest.Entries.Select(entry => new AcquisitionTaskDeleteCleanupFile(
                Path.GetFullPath(Path.Combine(artifact.RootDirectory, entry.RelativePath)), entry.Length)).ToArray(), reason);
        if (!authorization.Consent.DeleteDeliveredFiles || !await authorization.MatchesAsync(artifact, ct))
            return Block("CLEANUP_DELIVERY_PROOF_MISSING");
        if (await artifacts.HasActiveConsumersAsync(artifact.OperationId, ct)) return Block("ActiveConsumer");
        var owners = authorization.Consent.OwnerRunIds.Select(id => id.ToString(System.Globalization.CultureInfo.InvariantCulture)).ToArray();
        var bindings = await db.ArtifactOperationBindings.AsNoTracking().Where(b => b.OperationId == artifact.OperationId).ToListAsync(ct);
        if (bindings.Count == 0 || bindings.Any(b => b.OwnerKind != "workflow" || !owners.Contains(b.OwnerId))) return Block("SharedOwner");
        // Deleting a publication cannot remove the recoverable input of an outside
        // descendant, even when that descendant has materialized its own files.
        if (!(await authorization.ReleasableViewIdsAsync(ct)).Contains(artifact.Id, StringComparer.Ordinal))
            return Block("ReferencedDescendant");
        var exact = ArtifactManifestFiles.ResolveFiles(artifact.RootDirectory, artifact.Manifest);
        if (await authorization.HasOtherRegisteredFilesAsync(exact, artifact.Id, ct)) return Block("ReferencedArtifact");
        if (await references.HasReferencesAsync(artifact, authorization.Consent.OwnerRunIds, ct,
                previewSupersededRuns: authorization.PreviewSupersededRuns)) return Block("ReferencedPath");
        var selected = new List<AcquisitionTaskDeleteCleanupFile>();
        var identities = new List<object>();
        var fileIdentities = new Dictionary<string, string>(ArtifactManifestFiles.PathComparer);
        var root = Path.GetFullPath(artifact.RootDirectory);
        ArtifactManifestFiles.RejectLinks(root);
        var missingRoot = !Directory.Exists(root);
        if (missingRoot && (Path.GetDirectoryName(root) is not { } parent ||
                ArtifactCleanupAbsence.Probe(parent, root, ct) != ArtifactCleanupPathPresence.ConfirmedAbsent))
            return Block("WorkspaceUnavailable");
        for (var index = 0; index < exact.Count; index++)
        {
            ct.ThrowIfCancellationRequested();
            var path = exact[index]; storage.EnsurePathAllowed(path);
            var entry = artifact.Manifest.Entries[index];
            var presence = missingRoot ? ArtifactCleanupPathPresence.ConfirmedAbsent : ArtifactCleanupAbsence.Probe(root, path, ct);
            if (presence == ArtifactCleanupPathPresence.Unavailable) return Block("WorkspaceUnavailable");
            if (presence == ArtifactCleanupPathPresence.ConfirmedAbsent)
            { identities.Add(new {Path = path, Absent = true}); continue; }
            var information = new FileInfo(path); information.Refresh();
            if (information.Attributes.HasFlag(FileAttributes.Directory) || information.Length != entry.Length) return Block("SelectionChanged");
            selected.Add(new(path, information.Length));
            identities.Add(new {Path = path, information.Length, information.CreationTimeUtc, information.LastWriteTimeUtc});
            fileIdentities.Add(path, JsonSerializer.Serialize(new {information.Length, information.CreationTimeUtc, information.LastWriteTimeUtc}));
        }
        return new(artifact.Id, artifact.OperationId, root, artifact.Fingerprint,
            ArtifactManifestFiles.Hash(JsonSerializer.Serialize(new {artifact.Id, artifact.Fingerprint, Root = root, Files = identities})), selected,
            FileIdentities: fileIdentities);
    }

    private async Task<ArtifactWorkspaceCleanupResult> DeleteDeliveredFilesAsync(DeliveredCandidate expected,
        ArtifactDeliveryCleanupAuthorization authorization, CancellationToken ct)
    {
        ArtifactWorkspaceCleanupJournal.Receipt? receipt = null;
        var removed = 0; long bytes = 0;
        try
        {
            await using var fence = await artifacts.AcquireMaintenanceAsync(ct);
            var artifact = await artifacts.GetArtifactAsync(expected.ArtifactId, ct);
            if (artifact == null || artifact.OperationId != expected.OperationId || artifact.RootDirectory != expected.RootDirectory ||
                artifact.Fingerprint != expected.Fingerprint) return new(expected.ArtifactId, false, "SelectionChanged");
            var references = new ArtifactCleanupReferenceGuard(db, workspaces);
            var current = await PreviewDeliveredFilesAsync(artifact, authorization, references, ct);
            if (current.Reason != null) return new(expected.ArtifactId, false, current.Reason);
            // A selected file which disappeared is already cleaned. Never expand the fixed
            // selection to a file which appeared, or to a replacement at the same path.
            if (!MatchesRemainingSelection(expected, current)) return new(expected.ArtifactId, false, "SelectionChanged");
            if (current.Files.Count == 0)
            {
                RemoveEmptyDeliveryDirectories(artifact, ct);
                return new(expected.ArtifactId, true, "AlreadyAbsent");
            }
            var snapshot = new ArtifactCleanupFileSnapshot();
            foreach (var file in current.Files) snapshot.Observe(file.Path, file.Bytes, ct);
            if (!authorization.Consent.SkipFileVerification)
            {
                var actual = await ArtifactManifestFiles.CaptureAsync(artifact.RootDirectory, current.Files.Select(f => f.Path).ToArray(), ct);
                var expectedEntries = artifact.Manifest.Entries.ToDictionary(e => e.RelativePath, StringComparer.Ordinal);
                if (actual.Entries.Any(e => !expectedEntries.TryGetValue(e.RelativePath, out var previous) || e != previous))
                    return new(expected.ArtifactId, false, "SelectionChanged");
            }
            if ((await PreviewDeliveredFilesAsync(artifact, authorization, references, ct)).SelectionFingerprint != current.SelectionFingerprint)
                return new(expected.ArtifactId, false, "SelectionChanged");
            receipt = await maintenance.BeginDeliveredCleanupAsync(artifact, current.Files.ToDictionary(f => f.Path, f => f.Bytes), authorization.Consent, ct);
            foreach (var file in current.Files)
            {
                ct.ThrowIfCancellationRequested();
                if (!snapshot.Matches(file.Path, file.Bytes, ct)) throw new IOException("The selected delivery file changed.");
                File.Delete(file.Path);
                removed++; bytes += file.Bytes;
            }
            await maintenance.FinishDeliveredCleanupAsync(receipt, "Completed", ct);
            RemoveEmptyDeliveryDirectories(artifact, ct);
            // Preserve the immutable publication record for history. Only now-empty parents
            // of registered files are removed; unknown additions are never recursively removed.
            return new(expected.ArtifactId, true, null, removed, bytes);
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or
            InvalidOperationException or ArgumentException or JsonException or OperationCanceledException)
        {
            if (receipt != null)
                try { await maintenance.FinishDeliveredCleanupAsync(receipt, error is OperationCanceledException ? "Interrupted" : "Failed", CancellationToken.None); }
                catch (Exception journalError) when (journalError is IOException or UnauthorizedAccessException or InvalidOperationException) { }
            if (error is OperationCanceledException) throw;
            return new(expected.ArtifactId, false, "CLEANUP_FILES_RETAINED", removed, bytes);
        }
    }

    private static bool MatchesRemainingSelection(DeliveredCandidate expected, DeliveredCandidate current) =>
        current.FileIdentities != null && expected.FileIdentities != null && current.FileIdentities.All(pair =>
            expected.FileIdentities.TryGetValue(pair.Key, out var previous) && previous == pair.Value);

    private static void RemoveEmptyDeliveryDirectories(ArtifactRecord artifact, CancellationToken ct)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(artifact.RootDirectory));
        if (ArtifactManifestFiles.PathComparer.Equals(root, Path.GetPathRoot(root))) return;
        var directories = new HashSet<string>(ArtifactManifestFiles.PathComparer);
        foreach (var file in ArtifactManifestFiles.ResolveFiles(root, artifact.Manifest))
            // A historical publication may have used a shared configured root. Its
            // registration authorizes files, not removal of that allocation directory.
            for (var directory = Path.GetDirectoryName(file); directory != null &&
                ArtifactCleanupPathReferences.StrictlyUnder(directory, root);
                directory = Path.GetDirectoryName(directory))
            {
                directories.Add(directory);
            }
        foreach (var directory in directories.OrderByDescending(p => p.Length))
        {
            ct.ThrowIfCancellationRequested();
            ArtifactManifestFiles.RejectLinks(directory);
            try
            {
                // Non-recursive rmdir also refuses a file created after this observation.
                if (Directory.Exists(directory) && !Directory.EnumerateFileSystemEntries(directory).Any())
                    Directory.Delete(directory, recursive: false);
            }
            catch (DirectoryNotFoundException) { }
            catch (IOException) { /* Keep non-empty or concurrently changed directories. */ }
        }
    }
}
