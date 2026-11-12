using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain;
using Bakabase.Modules.Workflow.Artifacts;

namespace Bakabase.Service.Components.Acquisition.Planning;

internal sealed record AcquisitionCompositeOrigin(string PartId, AcquisitionPlanArtifactInput Input);
internal sealed record AcquisitionCompositeProvenance(int Version, int TaskId, int RunId, string TargetPartId,
    IReadOnlyList<AcquisitionCompositeOrigin> Inputs);

/// <summary>Composes only task-owned registered manifests. No directory discovery, copying or ownership transfer.</summary>
internal static class AcquisitionCompositeFiles
{
    internal const string Kind = "compositeFiles";
    internal const string OperationKind = "files.compose";
    private static readonly JsonSerializerOptions Json = WorkflowJson.Options;

    internal static (string Root, ArtifactManifest Manifest, FileCompositionProof Proof) Project(
        IReadOnlyList<ArtifactRecord> inputs, AcquisitionCompositeProvenance provenance)
    {
        if (inputs.Count is < 2 or > 10 || inputs.Select(a => a.Id).Distinct().Count() != inputs.Count ||
            inputs.Any(a => a.Kind == Kind || a.Manifest.Entries.Count == 0) ||
            inputs.Sum(a => a.Manifest.Entries.Count) > 10000)
            throw new ArgumentException("PLAN_COMPOSITE_INPUT_INVALID");
        var root = Path.GetFullPath(inputs[0].RootDirectory);
        bool Inside(string path) => ArtifactManifestFiles.PathComparer.Equals(root, path) ||
            path.StartsWith(Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar,
                OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
        foreach (var input in inputs)
        {
            var inputRoot = Path.GetFullPath(input.RootDirectory);
            while (!Inside(inputRoot)) root = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(root))
                ?? throw new ArgumentException("PLAN_COMPOSITE_ROOT_INVALID");
        }
        if (Path.TrimEndingDirectorySeparator(root) == Path.TrimEndingDirectorySeparator(Path.GetPathRoot(root)!))
            throw new ArgumentException("PLAN_COMPOSITE_ROOT_INVALID");
        var entries = new List<ArtifactManifestEntry>();
        var parents = new List<FileDispositionProof>();
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var directories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var input in inputs)
        {
            var files = ArtifactManifestFiles.ResolveFiles(input.RootDirectory, input.Manifest);
            var mappings = new List<FileDispositionEntry>();
            for (var index = 0; index < input.Manifest.Entries.Count; index++)
            {
                var source = input.Manifest.Entries[index];
                var relative = Path.GetRelativePath(root, files[index]).Replace('\\', '/');
                if (!paths.Add(relative.Normalize(NormalizationForm.FormC)))
                    throw new ArgumentException("PLAN_COMPOSITE_PATH_CONFLICT");
                var target = source with {RelativePath = relative}; entries.Add(target);
                mappings.Add(new(source, "preserved", [target]));
            }
            foreach (var directory in ArtifactManifestFiles.ResolveDirectories(input.RootDirectory, input.Manifest))
                directories.Add(Path.GetRelativePath(root, directory).Replace('\\', '/'));
            parents.Add(new(1, input.Id, input.Fingerprint, "", mappings));
        }
        foreach (var path in paths.Concat(directories.Select(d => d.Normalize(NormalizationForm.FormC))))
        {
            var parent = path;
            while (parent.LastIndexOf('/') is var separator && separator >= 0)
            {
                parent = parent[..separator];
                if (paths.Contains(parent)) throw new ArgumentException("PLAN_COMPOSITE_PATH_CONFLICT");
            }
        }
        if (directories.Any(d => paths.Contains(d.Normalize(NormalizationForm.FormC))))
            throw new ArgumentException("PLAN_COMPOSITE_PATH_CONFLICT");
        var ordered = entries.OrderBy(e => e.RelativePath, StringComparer.Ordinal).ToArray();
        var leaves = directories.OrderBy(p => p, StringComparer.Ordinal).ToArray();
        var manifest = new ArtifactManifest(ordered, ArtifactManifestFiles.Fingerprint(ordered, leaves), leaves);
        _ = ArtifactManifestFiles.ResolveFiles(root, manifest);
        var identity = ArtifactManifestFiles.Hash(JsonSerializer.Serialize(new {provenance, manifest.Fingerprint}, Json));
        return (root, manifest, new(1, identity,
            parents.Select(p => p with {OutputFingerprint = manifest.Fingerprint}).ToArray(), JsonSerializer.Serialize(provenance, Json)));
    }

    internal static async Task<bool> IsOwnedAsync(BakabaseDbContext db, IArtifactOperationStore store,
        ArtifactRecord artifact, int taskId, int runId, string partId, CancellationToken ct)
    {
        if (artifact.Kind != Kind || artifact.Managed || artifact.MetadataJson == null) return false;
        try
        {
            var proof = JsonSerializer.Deserialize<FileCompositionProof>(artifact.MetadataJson, Json);
            var origin = proof == null ? null : JsonSerializer.Deserialize<AcquisitionCompositeProvenance>(proof.ProvenanceJson, Json);
            var operation = await store.GetOperationAsync(artifact.OperationId, ct);
            if (proof?.Version != 1 || origin?.Version != 1 || origin.TaskId != taskId || origin.TargetPartId != partId ||
                origin.Inputs == null || origin.Inputs.Count is < 2 or > 10 || origin.Inputs.Any(i => i == null || i.Input == null) ||
                origin.Inputs.Select(i => i.PartId).Distinct().Count() != origin.Inputs.Count ||
                operation?.Kind != OperationKind || operation.State != ArtifactOperationState.Completed ||
                operation.OutputArtifactId != artifact.Id || operation.InputFingerprint != proof.InputFingerprint ||
                !artifact.ParentArtifactIds.ToHashSet().SetEquals(origin.Inputs.Select(i => i.Input.ArtifactId))) return false;
            // A later continuation can borrow the same immutable composition; its origin still binds every donor.
            if (runId != origin.RunId && !await AcquisitionPlanArtifactInputGuard.HasContinuationBindingAsync(db, runId, partId, artifact.OperationId, ct)) return false;
            var inputs = new List<ArtifactRecord>();
            foreach (var source in origin.Inputs)
            {
                if (source.Input.TaskId != taskId || source.Input.PreviousRunId != origin.RunId) return false;
                var parent = await store.GetArtifactAsync(source.Input.ArtifactId, ct);
                if (parent?.Kind == Kind) return false; // Explicitly bounded composition, never recursive owner claims.
                inputs.Add(await AcquisitionPlanArtifactInputGuard.RequireAsync(db, store, taskId, source.PartId, source.Input, ct));
            }
            var projected = Project(inputs, origin);
            return ArtifactManifestFiles.PathComparer.Equals(projected.Root, artifact.RootDirectory) &&
                FileDispositionProofs.SameManifest(projected.Manifest, artifact.Manifest) &&
                projected.Proof.InputFingerprint == proof.InputFingerprint && proof.Parents != null && proof.Parents.Count == inputs.Count &&
                inputs.All(parent => proof.Parents.Count(p => p.InputArtifactId == parent.Id) == 1 &&
                    FileDispositionProofs.ValidFor(proof.Parents.Single(p => p.InputArtifactId == parent.Id), parent, artifact));
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or InvalidDataException or JsonException)
        { return false; }
    }
}
