using System;
using System.Globalization;
using System.IO;
using System.Linq;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Service.Components.FileProcessing;

namespace Bakabase.Service.Components.Acquisition.Planning;

/// <summary>A single unfinished extraction group in an existing processing attempt. Its executor
/// rechecks the archive catalog, reserves this volume and monitors the writer when the size is unknown.</summary>
internal sealed record AcquisitionCheckpointWrite(string StorageRoot, string WritePath, string Evidence, long? ExpandedBytes, string? VolumeId = null)
{
    internal static AcquisitionCheckpointWrite? Inspect(FileProcessingPlanExecutor.State state, ArtifactRecord input,
        string directory, string step, string storageRoot, string placementStamp)
    {
        if (state is not {Completed: false, LineageVersion: 1, PendingOperation: null, FinalAssembly: null} ||
            state.InputManifest?.Fingerprint != input.Fingerprint || !state.InputManifest.Entries.SequenceEqual(input.Manifest.Entries) ||
            !(state.InputManifest.Directories ?? []).SequenceEqual(input.Manifest.Directories ?? []) ||
            state.Operations == null || state.Active == null || state.FileContents == null || state.Outputs == null) return null;
        var prefix = FileProcessingPlanExecutor.StepKey(step) + "-";
        var pending = state.Operations.Where(p => p.Key.StartsWith(prefix, StringComparison.Ordinal) && !p.Value.Completed).Take(2).ToArray();
        if (pending.Length != 1) return null;
        var (key, op) = pending[0];
        if (!int.TryParse(key.AsSpan(prefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out var index) ||
            key != prefix + index.ToString(CultureInfo.InvariantCulture) || op is not {ArchiveInputs.Count: > 0, OutputDirectory: not null} ||
            op.OutputManifest != null || op.RetainedGroupToken != null || op.ExpandedBytes < 0) return null;
        var outputs = Path.Combine(directory, "outputs");
        if (!ArtifactCleanupPathReferences.StrictlyUnder(op.OutputDirectory, outputs)) return null;
        FileProcessingFiles.Within(op.OutputDirectory, outputs);
        var members = op.ArchiveInputs;
        if (members.Distinct(FileProcessingFiles.PathComparer).Count() != members.Count) return null;
        var active = state.Active.ToLookup(a => a.Path, FileProcessingFiles.PathComparer);
        foreach (var member in members)
        {
            if (!state.FileContents.TryGetValue(member, out var receipt) || receipt.Length < 0 || receipt.Sha256 is not {Length: 64}) return null;
            var path = FileProcessingFiles.Within(Path.Combine(directory, member), directory);
            if (ArtifactManifestFiles.RelativeFile(directory, path) != member || active[path].Take(2).Count() != 1 ||
                !File.Exists(path) || new FileInfo(path).Length != receipt.Length) return null;
        }
        var evidence = AcquisitionLocalFilesRecoveryReceipt.Hash(new {placementStamp, step, key, op.OutputDirectory,
            Members = members.Select(m => new {Path = m, Receipt = state.FileContents[m]}), op.ExpandedBytes});
        return new(storageRoot, FileProcessingPlanExecutor.ExtractionStagingDirectory(op.OutputDirectory, op, directory), evidence, op.ExpandedBytes);
    }
}

