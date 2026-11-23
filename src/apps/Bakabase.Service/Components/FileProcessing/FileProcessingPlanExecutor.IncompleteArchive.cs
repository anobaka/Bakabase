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

public sealed record IncompleteArchiveGroup(string Token, int MemberCount, long TotalBytes,
    IReadOnlyList<string> Members, int CompletedGroupCount);

internal sealed record ArchiveGroupOwner(long RunId, string TargetId, string StepId);
internal sealed record ArchiveGroupExecution(ArchiveGroupOwner Owner, string ArtifactId, string Fingerprint,
    string OperationId, int Attempt, string? RequestedToken);

public sealed partial class FileProcessingPlanExecutor
{
    // Legacy waits have the typed failure in the workflow and the exact, pending group in
    // the executor journal. Reading these receipts must not open earlier archives or outputs.
    internal static async Task<IncompleteArchiveGroup?> InspectPendingGroupAsync(string directory, ArtifactRecord input,
        ArchiveGroupExecution scope, CancellationToken ct)
    {
        var state = await ReadPinnedStateAsync(directory, ct);
        if (state.Completed || state.FinalAssembly != null || state.PendingOperation is not {} key ||
            state.ReadOnlyVolumeArtifactId is {} borrowedId && borrowedId != input.Id ||
            state.SnapshotSourceArtifactId is {} privateId && privateId != input.Id ||
            state.InputManifest == null || !SameManifestContents(state.InputManifest, input.Manifest)) return null;
        state.ExecutionRoot = directory;
        return DescribeIncompleteGroup(state, key, scope);
    }

    private static IncompleteArchiveGroup? DescribeIncompleteGroup(State state, string key, ArchiveGroupExecution scope)
    {
        var prefix = StepKey(scope.Owner.StepId) + "-";
        if (state.LineageVersion != 1 || state.InputManifest?.Fingerprint != scope.Fingerprint ||
            !key.StartsWith(prefix, StringComparison.Ordinal) || !int.TryParse(key.AsSpan(prefix.Length), out var index) ||
            index < 0 || key != prefix + index || !state.Operations.TryGetValue(key, out var op) ||
            op is not {Completed: false, OutputManifest: null, ExtractionStagingId: null, ArchiveInputs.Count: > 0} ||
            op.OutputDirectory == null || Path.Exists(FileProcessingFiles.Within(op.OutputDirectory, state.ExecutionRoot!)) ||
            Path.Exists(ExtractionStagingDirectory(op.OutputDirectory, op, state.ExecutionRoot!)))
            return null;
        var receipts = new List<ArtifactManifestEntry>();
        var physical = new HashSet<string>(FileProcessingFiles.PathComparer);
        foreach (var source in op.ArchiveInputs)
        {
            if (!state.FileContents.TryGetValue(source, out var entry) || entry.Length < 0 || entry.Sha256.Length != 64 ||
                !physical.Add(source)) return null;
            var path = FileProcessingFiles.Within(Path.Combine(state.ExecutionRoot!, source), state.ExecutionRoot!);
            var matches = state.Active.Where(a => FileProcessingFiles.PathComparer.Equals(a.Path, path)).Take(2).ToArray();
            if (matches.Length != 1 || !File.Exists(path) || new FileInfo(path).Length != entry.Length) return null;
            var relative = matches[0].RelativePath.Replace('\\', '/');
            if (Path.IsPathRooted(relative) || relative.Length > 1 && char.IsAsciiLetter(relative[0]) && relative[1] == ':' ||
                relative.Split('/').Any(p => p is "" or "." or ".." || p.Any(char.IsControl))) return null;
            receipts.Add(entry with {RelativePath = relative});
        }
        var preceding = new List<string>();
        var completed = 0;
        for (var i = 0; i < index; i++)
        {
            if (!state.Operations.TryGetValue(prefix + i, out var previous)) return null;
            if (previous is {Completed: true, OutputManifest: {} manifest} &&
                manifest.Fingerprint == ArtifactManifestFiles.Fingerprint(manifest.Entries, manifest.Directories))
            {
                preceding.Add(manifest.Fingerprint);
                completed++;
            }
            else if (previous.RetainedGroupToken != null) preceding.Add(previous.RetainedGroupToken);
            else return null;
        }
        var token = ArtifactManifestFiles.Hash(JsonSerializer.Serialize(new
        {
            Version = 1, scope.Owner, scope.ArtifactId, scope.Fingerprint, scope.OperationId, scope.Attempt,
            Key = key, op.ArchiveInputs, Members = receipts, Preceding = preceding
        }, Json));
        return new(token, receipts.Count, receipts.Sum(e => e.Length), receipts.Select(e => e.RelativePath).Take(8).ToArray(), completed);
    }
}
