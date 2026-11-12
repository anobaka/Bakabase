using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.Modules.Workflow.Artifacts;

namespace Bakabase.Service.Components.FileProcessing;

public sealed record FileCleanupCoverageEntry(string RelativePath, string Disposition, string? Reason = null);
public sealed record FileCleanupCoverage(bool Complete, IReadOnlyList<FileCleanupCoverageEntry> Files);

/// <summary>
/// Read-only ledger proof. It does not inspect or remove files; callers must additionally
/// verify the final bytes, references and maintenance fence immediately before cleanup.
/// </summary>
public static class ArtifactFileCoverage
{
    public const string IncompleteCode = "FILE_CLEANUP_COVERAGE_INCOMPLETE";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static async Task<FileCleanupCoverage> EvaluateAsync(IArtifactOperationStore store,
        ArtifactRecord source, IReadOnlyList<string> outputIds, CancellationToken ct = default)
    {
        var records = new Dictionary<string, ArtifactRecord>(StringComparer.Ordinal) {[source.Id] = source};
        var operations = new Dictionary<string, ArtifactOperationDbModel?>(StringComparer.Ordinal);
        var manifests = new Dictionary<string, Dictionary<string, ArtifactManifestEntry>>(StringComparer.Ordinal);
        var children = new Dictionary<string, List<ArtifactRecord>>(StringComparer.Ordinal);
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var visiting = new HashSet<string>(StringComparer.Ordinal);
        var targets = new HashSet<string>(outputIds, StringComparer.Ordinal);

        async Task<bool> Read(string id, int depth = 0)
        {
            ct.ThrowIfCancellationRequested();
            if (visited.Contains(id)) return true;
            if (depth > 128 || visited.Count + visiting.Count >= 10000 || !visiting.Add(id)) return false;
            if (!records.TryGetValue(id, out var artifact))
            {
                artifact = await store.GetArtifactAsync(id, ct);
                if (artifact == null) return false;
                records[id] = artifact;
            }
            // Validate registered manifest structure without opening intermediate files. They
            // may have been legitimately reclaimed after their immutable receipt was committed.
            ArtifactManifestFiles.ResolveFiles(artifact.RootDirectory, artifact.Manifest);
            manifests[id] = artifact.Manifest.Entries.ToDictionary(e => e.RelativePath, StringComparer.Ordinal);
            operations[id] = await store.GetOperationAsync(artifact.OperationId, ct);
            foreach (var parent in id == source.Id ? [] : artifact.ParentArtifactIds)
            {
                if (!await Read(parent, depth + 1)) return false;
                if (!children.TryGetValue(parent, out var list)) children[parent] = list = [];
                list.Add(artifact);
            }
            visiting.Remove(id); visited.Add(id);
            return true;
        }

        FileCleanupCoverage Unknown(string reason) => new(false,
            source.Manifest.Entries.Select(e => new FileCleanupCoverageEntry(e.RelativePath, "unknown", reason)).ToArray());

        if (targets.Count == 0) return Unknown("missingOutput");
        try
        {
            foreach (var id in targets)
                if (!await Read(id)) return Unknown("incompleteLineage");
        }
        catch (Exception error) when (error is System.IO.InvalidDataException or ArgumentException or JsonException)
        { return Unknown("invalidManifest"); }
        if (!children.ContainsKey(source.Id)) return Unknown("unrelatedOutput");

        var mappings = new Dictionary<(string Parent, string Child), Dictionary<string, FileDispositionEntry>? >();
        Dictionary<string, FileDispositionEntry>? Proof(ArtifactRecord parent, ArtifactRecord child)
        {
            var key = (parent.Id, child.Id);
            if (mappings.TryGetValue(key, out var cached)) return cached;
            FileDispositionProof? proof = null;
            try
            {
                // Only metadata committed by the concrete successful operation is executable
                // evidence. Nested source metadata, prompt text and plan fields are ignored.
                if (operations.GetValueOrDefault(child.Id) is {State: ArtifactOperationState.Completed} operation &&
                    operation.OutputArtifactId == child.Id && child.MetadataJson != null)
                {
                    if (child.Kind == "processedFiles" && operation.InputFingerprint == parent.Fingerprint && operations[child.Id]?.Kind == "files.process")
                    {
                        var metadata = JsonSerializer.Deserialize<FileArtifactProcessingMetadata>(child.MetadataJson, Json);
                        if (metadata?.InputArtifactId == parent.Id) proof = metadata.FileDisposition;
                    }
                    else if (child.Kind == "publishedFiles" && operation.InputFingerprint == parent.Fingerprint && operations[child.Id]?.Kind == "files.publish")
                    {
                        var metadata = JsonSerializer.Deserialize<FileArtifactPublicationMetadata>(child.MetadataJson, Json);
                        if (metadata?.SourceArtifactId == parent.Id && metadata.SourceOperationId == parent.OperationId)
                            proof = metadata.FileDisposition;
                    }
                    else if (child.Kind == "compositeFiles" && operation.Kind == "files.compose" && !child.Managed)
                    {
                        var metadata = JsonSerializer.Deserialize<FileCompositionProof>(child.MetadataJson, Json);
                        if (metadata is {Version: 1, Parents: not null} && metadata.InputFingerprint == operation.InputFingerprint &&
                            metadata.Parents.Count(p => p.InputArtifactId == parent.Id) == 1 &&
                            child.ParentArtifactIds.ToHashSet(StringComparer.Ordinal)
                                .SetEquals(metadata.Parents.Select(p => p.InputArtifactId)))
                            proof = metadata.Parents.Single(p => p.InputArtifactId == parent.Id);
                    }
                }
            }
            catch (JsonException) { }
            return mappings[key] = FileDispositionProofs.ValidFor(proof, parent, child)
                ? proof!.Entries.ToDictionary(e => e.Source.RelativePath, StringComparer.Ordinal) : null;
        }

        var memo = new Dictionary<(string Artifact, string Path), string?>();
        string? Covered(ArtifactRecord current, ArtifactManifestEntry file)
        {
            ct.ThrowIfCancellationRequested();
            var key = (current.Id, file.RelativePath);
            if (memo.TryGetValue(key, out var known)) return known;
            if (targets.Contains(current.Id) && operations.GetValueOrDefault(current.Id)?.State == ArtifactOperationState.Completed)
                return memo[key] = "preserved";
            if (children.TryGetValue(current.Id, out var next))
                foreach (var child in next)
                {
                    if (operations.GetValueOrDefault(child.Id)?.State != ArtifactOperationState.Completed) continue;
                    var proof = Proof(current, child);
                    if (proof != null && proof.TryGetValue(file.RelativePath, out var disposition) &&
                        disposition.Disposition is "preserved" or "replaced" && disposition.Outputs.Count > 0)
                    {
                        var downstream = disposition.Outputs.Select(e => Covered(child, e)).ToArray();
                        // Every file of an extraction/replacement must reach a verified final
                        // result. Delivering only one child is not sufficient to delete its archive.
                        if (downstream.All(value => value != null))
                            return memo[key] = disposition.Disposition == "replaced" || downstream.Contains("replaced")
                                ? "replaced" : "preserved";
                    }
                    else
                    {
                        // A composite has no owned directory and is always proof-bearing.
                        // A matching path alone cannot replace a missing donor receipt.
                        if (child.Kind == "compositeFiles") continue;
                        // Legacy identity is deliberately path-preserving, not a hash search:
                        // identical key/config bytes at different paths can both be necessary.
                        if (manifests[child.Id].TryGetValue(file.RelativePath, out var retained) &&
                            FileDispositionProofs.SameEntry(file, retained) && Covered(child, retained) is { } result)
                            return memo[key] = result;
                    }
                }
            return memo[key] = null;
        }

        var decisions = source.Manifest.Entries.Select(entry =>
        {
            var disposition = Covered(source, entry);
            return new FileCleanupCoverageEntry(entry.RelativePath, disposition ?? "unknown",
                disposition == null ? "missingDispositionOrDelivery" : null);
        }).ToArray();
        return new(decisions.All(d => d.Disposition != "unknown"), decisions);
    }
}
