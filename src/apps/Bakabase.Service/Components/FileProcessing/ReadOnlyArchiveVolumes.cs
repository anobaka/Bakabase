using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.InsideWorld.Business.Components.Compression;

namespace Bakabase.Service.Components.FileProcessing;

internal sealed class FileProcessingInputChangedException(string message) : InvalidOperationException(message);

/// <summary>Manifest-bound archives that the extractor can open through private read-only aliases.
/// Other inputs remain independent private copies in the processing checkpoint.</summary>
internal sealed class ReadOnlyArchiveVolumes
{
    private readonly Dictionary<string, ArtifactManifestEntry> members;
    internal ArtifactRecord Source { get; }
    internal IReadOnlyList<SevenZipVolumeSet> Groups { get; }
    internal IReadOnlyList<string> RelativeMembers => members.Values.Select(e => e.RelativePath).Order(StringComparer.Ordinal).ToArray();

    private ReadOnlyArchiveVolumes(ArtifactRecord source, IReadOnlyList<SevenZipVolumeSet> groups, IEnumerable<string> pathsToBorrow)
    {
        Source = source;
        Groups = groups;
        var paths = pathsToBorrow.ToHashSet(FileProcessingFiles.PathComparer);
        members = source.Manifest.Entries.Select(e => (Path: Path.GetFullPath(Path.Combine(source.RootDirectory, e.RelativePath)), Entry: e))
            .Where(e => paths.Contains(e.Path)).ToDictionary(e => e.Path, e => e.Entry, FileProcessingFiles.PathComparer);
        if (paths.Count != members.Count) throw new FileProcessingInputChangedException("The split archive changed after its inputs were selected.");
    }

    internal static bool Supports(FileProcessingPlan? plan, ArtifactRecord input, ArtifactStoragePolicy? policy) =>
        !OperatingSystem.IsWindows() && input.Kind is "download" or "currentFiles" && input.State == ArtifactLocationState.Available &&
        input.Files.Any(SevenZipVolumeSet.MayContainVolumes) &&
        plan is {Requirement: "required", Steps.Count: > 0} && plan.Steps[0] is {Op: "extractArchive", Selector: null, ArchiveOpenMode: null} step &&
        step.GetInputReferences().SequenceEqual(["download"]) &&
        plan.Steps.Zip(plan.Steps.Skip(1)).All(pair => pair.Second.Op == "extractArchive" &&
            pair.Second.GetInputReferences().SequenceEqual([pair.First.Id])) &&
        policy?.Materialization is null or "auto" &&
        step.StoragePolicy?.Materialization is null or "auto";

    internal static bool SupportsProcessedSelection(FileProcessingPlan? plan, ArtifactRecord input, ArtifactStoragePolicy? policy) =>
        !OperatingSystem.IsWindows() && input is {Kind: "processedFiles", Managed: true, State: ArtifactLocationState.Available} &&
        input.Files.Count is > 0 and <= 1024 && plan is {Requirement: "required", Steps: [var step]} &&
        step is {Op: "extractArchive", Selector: not null, ArchiveMemberSelector: null, ArchiveOpenMode: null, ZipFileNameCodePage: null, ZipPasswordCodePage: null} &&
        step.GetInputReferences().SequenceEqual(["download"]) && policy is not ({Materialization: "copy"} or
            {CleanupIntermediates: false} or {SourceCleanup: "keep" or "quarantine"}) &&
        step.StoragePolicy is not ({Materialization: "copy"} or {CleanupIntermediates: false} or {SourceCleanup: "keep" or "quarantine"});

    internal bool IsStandaloneZip(IReadOnlyList<string> files) => Groups.Count == 0 && files.Count == 1 && Contains(files[0]);

    internal static async Task<ReadOnlyArchiveVolumes?> InspectAsync(FileProcessingPlan? plan, ArtifactRecord input,
        ArtifactStoragePolicy? policy, CancellationToken ct, bool borrowProcessedSelection = false)
    {
        if (borrowProcessedSelection && SupportsProcessedSelection(plan, input, policy))
        {
            var selected = input.Files.Where(f => FileProcessingPlanExecutor.Matches(
                new(f, Path.GetRelativePath(input.RootDirectory, f)), plan!.Steps[0].Selector)).ToArray();
            if (selected.Length is 0 or > 8 || CompressedFileHelper.DetectCompressedFileGroups(input.Files.ToArray(), true)
                .Any(group => group.Files.Count > 1 && group.Files.Intersect(selected, FileProcessingFiles.PathComparer).Any())) return null;
            // Use the same archive grouping as execution before lending any paths. A
            // conflicting multipart family or an executable stays inside the ordinary snapshot.
            IReadOnlyList<DetectedArchiveGroup> groups;
            try { groups = await AuthorizedArchiveDetection.DetectAsync(selected, recursive: false, ct); }
            catch (ArchiveFormatProbeIncompleteException) { return null; }
            var standalone = new List<string>();
            foreach (var file in groups.Where(g => g.Extension == ".zip" && g.Files.Count == 1)
                         .Select(g => g.Files[0]))
            {
                ct.ThrowIfCancellationRequested();
                ArtifactManifestFiles.RejectLinks(file);
                try
                {
                    if (!await ArchiveSignature.IsCompleteStandaloneZipAsync(file, new FileInfo(file).Length, ct)) continue;
                    // Resolve capability before committing the input layout. An unavailable
                    // private view keeps the normal independently verified snapshot.
                    using var view = new SevenZipVolumeSet.ToolInputs(file);
                    standalone.Add(file);
                }
                catch (ArchiveFormatProbeIncompleteException) { /* Bounded inspection could not prove a standalone ZIP. */ }
            }
            return standalone.Count == 0 ? null : new(input, [], standalone);
        }
        if (!Supports(plan, input, policy)) return null;
        foreach (var file in input.Files) ArtifactManifestFiles.RejectLinks(file);
        var sets = await SevenZipVolumeSet.FindAsync(input.Files, ct, includeNamedFirst: true);
        return sets.Count == 0 ? null : new(input, sets, sets.SelectMany(group => group.Sources));
    }

    internal bool Contains(string physicalPath) => members.ContainsKey(physicalPath);
    internal bool TrySource(string relativePath, out string path)
    {
        path = Path.GetFullPath(Path.Combine(Source.RootDirectory, relativePath));
        return members.ContainsKey(path);
    }

    internal async Task VerifyReadAsync(IEnumerable<string> files, CancellationToken ct)
    {
        foreach (var file in files.Where(Contains))
        {
            var actual = await ArtifactManifestFiles.CaptureAsync(Source.RootDirectory, [file], ct);
            var expected = members[file];
            if (actual.Entries is not [var entry] || entry.Length != expected.Length || !string.Equals(entry.Sha256, expected.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new FileProcessingInputChangedException("A read-only archive volume changed during extraction.");
        }
    }
}
