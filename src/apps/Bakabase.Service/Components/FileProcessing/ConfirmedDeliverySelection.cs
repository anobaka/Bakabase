using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Modules.Workflow.Artifacts;

namespace Bakabase.Service.Components.FileProcessing;

/// <summary>An explicitly approved subset, registered without taking ownership of the reviewed files.</summary>
public sealed record ConfirmedDeliverySelection(string ArtifactId, string Intent)
{
    internal const string Kind = "deliverySelection";

    internal static async Task<ConfirmedDeliverySelection> CreateAsync(WorkflowExecutionContext context,
        IArtifactOperationStore store, string targetId, ArtifactRecord input, IReadOnlyList<string> excludedFiles,
        CancellationToken ct)
    {
        if (excludedFiles.Count is 0 or > 256 || excludedFiles.Any(path => string.IsNullOrEmpty(path) || path.Length > 4096))
            throw new ArgumentException("FILE_DELIVERY_REVIEW_INVALID");
        var excluded = excludedFiles.ToHashSet(StringComparer.Ordinal);
        var entries = input.Manifest.Entries.Where(entry => !excluded.Contains(entry.RelativePath)).ToArray();
        // Only exact paths already present in this immutable manifest can be excluded.
        if (excluded.Count != excludedFiles.Count || entries.Length == 0 || entries.Length + excluded.Count != input.Manifest.Entries.Count)
            throw new ArgumentException("FILE_DELIVERY_REVIEW_INVALID");
        var directories = input.Manifest.Directories;
        var manifest = new ArtifactManifest(entries, ArtifactManifestFiles.Fingerprint(entries, directories), directories);
        var files = ArtifactManifestFiles.ResolveFiles(input.RootDirectory, manifest);
        var provenance = FileArtifactDeliveryLayout.ReadProvenance(input);
        var selectedProvenance = provenance == null ? null : FileArtifactDeliveryLayout.Bind(manifest,
            provenance.Entries.Where(entry => entry.IsDirectory || !excluded.Contains(entry.RelativePath)));
        var key = ArtifactManifestFiles.Hash(JsonSerializer.Serialize(new[] {"delivery-selection-v1", input.Id, input.Fingerprint, manifest.Fingerprint}));
        var intent = "delivery-selection:" + key;
        var metadata = JsonSerializer.Serialize(new {directoryProvenance = selectedProvenance}, WorkflowJson.Options);
        var selected = await store.AdoptAsync(new(key, intent, "files.deliverySelection", input.Fingerprint),
            new(input.RootDirectory, files, Kind, Managed: false, ParentArtifactIds: [input.Id], MetadataJson: metadata,
                ExpectedManifestFingerprint: manifest.Fingerprint, Directories: ArtifactManifestFiles.ResolveDirectories(input.RootDirectory, manifest)),
            "workflow:" + context.RunId + ":delivery-selection:" + targetId, ct);
        if (selected.Kind != Kind || selected.Fingerprint != manifest.Fingerprint || !selected.ParentArtifactIds.SequenceEqual([input.Id]))
            throw new InvalidOperationException("FILE_DELIVERY_INPUT_CHANGED");
        await ArtifactWorkflowBindings.BindAsync(context, targetId + ":delivery-selection:" + selected.Id, selected.OperationId, ct);
        return new(selected.Id, intent);
    }
}
