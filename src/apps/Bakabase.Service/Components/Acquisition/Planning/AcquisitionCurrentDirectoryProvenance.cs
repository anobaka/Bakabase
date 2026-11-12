using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Service.Components.FileProcessing;

namespace Bakabase.Service.Components.Acquisition.Planning;

/// <summary>Preserves recorded layout facts across read-only snapshots, never infers them from names.</summary>
internal static class AcquisitionCurrentDirectoryProvenance
{
    internal static async Task<ArtifactDirectoryProvenance?> ReadAsync(BakabaseDbContext db,
        IArtifactOperationStore store, int taskId, string partId, AcquisitionPlanArtifactInput input, CancellationToken ct)
    {
        var snapshots = new List<ArtifactManifest>();
        var visited = new HashSet<string>(StringComparer.Ordinal);
        string? directory = null;
        try
        {
            for (var depth = 0; depth < 32 && visited.Add(input.ArtifactId); depth++)
            {
                ct.ThrowIfCancellationRequested();
                // This checks the producer receipt, operation completion, exact part binding
                // and same-task run ownership. A metadata parent ID alone is never authority.
                var source = await AcquisitionPlanArtifactInputGuard.RequireAsync(db, store, taskId, partId,
                    input, ct, currentDirectorySource: true);
                if (directory != null && !ArtifactManifestFiles.PathComparer.Equals(directory, source.RootDirectory)) return null;
                directory = source.RootDirectory;
                var proof = FileArtifactDeliveryLayout.ReadProvenance(source);
                if (proof != null)
                {
                    var manifest = source.Manifest;
                    foreach (var snapshot in snapshots.AsEnumerable().Reverse())
                    {
                        proof = Project(manifest, proof, snapshot);
                        manifest = snapshot;
                    }
                    return proof;
                }
                if (source.Kind != AcquisitionCurrentDirectoryFiles.Kind || source.MetadataJson == null) return null;
                var receipt = JsonSerializer.Deserialize<AcquisitionCurrentDirectoryReceipt>(source.MetadataJson, WorkflowJson.Options);
                if (receipt?.TaskId != taskId || receipt.PartId != partId || receipt.PriorInput == null) return null;
                snapshots.Add(source.Manifest);
                input = receipt.PriorInput;
            }
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or IOException or InvalidDataException or JsonException)
        {
            // Missing historical proof does not invalidate the current file selection. It
            // merely leaves those directories intact for an explicit user layout decision.
        }
        return null;
    }

    internal static ArtifactDirectoryProvenance? Project(ArtifactManifest source, ArtifactDirectoryProvenance? proof,
        ArtifactManifest current)
    {
        if (!FileArtifactDeliveryLayout.Valid(proof, source)) return null;
        var oldFiles = source.Entries.ToDictionary(entry => entry.RelativePath, StringComparer.Ordinal);
        var currentFiles = current.Entries.ToDictionary(entry => entry.RelativePath, StringComparer.Ordinal);
        var oldDirectories = (source.Directories ?? []).ToHashSet(StringComparer.Ordinal);
        var currentDirectories = (current.Directories ?? []).ToHashSet(StringComparer.Ordinal);
        var retained = proof!.Entries.Where(entry => entry.IsDirectory
            ? oldDirectories.Contains(entry.RelativePath) && currentDirectories.Contains(entry.RelativePath)
            : oldFiles.TryGetValue(entry.RelativePath, out var oldFile) &&
              currentFiles.TryGetValue(entry.RelativePath, out var currentFile) &&
              FileDispositionProofs.SameContent(oldFile, currentFile)).ToArray();
        // Preview requires evidence for every current leaf below a generated directory.
        // Added or modified files deliberately have no evidence, so their wrappers survive.
        return FileArtifactDeliveryLayout.Bind(current, retained);
    }
}
