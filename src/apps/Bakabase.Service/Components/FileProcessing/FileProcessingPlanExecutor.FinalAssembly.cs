using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.Modules.Workflow.Artifacts;

namespace Bakabase.Service.Components.FileProcessing;

public sealed partial class FileProcessingPlanExecutor
{
    public sealed record FinalAssemblyFile(string Source, string RelativePath, ArtifactManifestEntry Content,
        bool Move, string Mode);
    public sealed record FinalAssemblyJournal(int Version, ArtifactManifest Manifest,
        IReadOnlyList<FinalAssemblyFile> Files, string CopyDirectoryName)
    {
        // A missing boundary identifies the historical final-result journal.
        public string? BoundaryStepId { get; init; }
        public bool PromoteExtractionOutputs { get; init; }
        public string? StandaloneExtractionStep { get; init; }
        public IReadOnlyList<ArtifactManifestEntry> ReplacedPartialCopies { get; init; } = [];
    }

    private static bool CanMoveToFinal(State state, string directory, Artifact file)
    {
        var policy = file.StepId != null ? state.StepPolicies.GetValueOrDefault(file.StepId) : state.StoragePolicy;
        if (file.RetainedIncompleteArchive || state.MaterializationVersion < 2 || state.LineageVersion != 1 || state.StoragePolicy?.CleanupIntermediates != true ||
            policy?.CleanupIntermediates == false || MaterializationMode(state, policy) != "auto" ||
            state.Active.Count(a => FileProcessingFiles.PathComparer.Equals(a.Path, file.Path)) != 1 ||
            !new[] {"inputs", "outputs"}.Any(name => IsInside(Path.Combine(directory, name), file.Path)) ||
            state.RetainedFiles.Contains(file.Path, FileProcessingFiles.PathComparer) ||
            state.BoundaryResults.Values.SelectMany(b => b.Files).Any(a => FileProcessingFiles.PathComparer.Equals(a.Path, file.Path)))
            return false;
        // An earlier published boundary can still require the complete original-byte
        // snapshot for a pinned continuation. Preserve its input and rename chain.
        if (state.BoundaryResults.Count > 0 && (IsInside(Path.Combine(directory, "inputs"), file.Path) ||
            state.Operations.Values.Any(o => o.RenameFiles?.Any(f =>
                FileProcessingFiles.PathComparer.Equals(f.Source, file.Path) || o.OutputDirectory != null &&
                FileProcessingFiles.PathComparer.Equals(Path.Combine(o.OutputDirectory, f.RelativePath), file.Path)) == true)))
            return false;
        return true;
    }

    // A committed extraction receipt proves ownership of the private source. Ancestor boundaries
    // and explicit retention still pin their bytes; original or borrowed inputs cannot qualify.
    internal static HashSet<string> PromotableExtractions(State state, string directory, string? boundary, CancellationToken ct,
        string? standaloneExtractionStep = null)
    {
        var promotable = new HashSet<string>(FileProcessingFiles.PathComparer);
        if (state.MaterializationVersion != 2 || state.LineageVersion != 1 || state.StoragePolicy?.CleanupIntermediates == false ||
            state.Active.Any(f => !IsInside(directory, f.Path)) || !OperationDirectoriesAreDisjoint(state)) return promotable;
        // A standalone registered-input review has no prior boundary to pin its writer outputs.
        // The saved step identity also lets a partly moved result resume the same assembly.
        var standalone = standaloneExtractionStep != null && boundary == null && state.BoundaryResults.Count == 0 &&
            state.InputManifest != null && (state.SnapshotSourceArtifactId != null || state.ReadOnlyVolumeArtifactId != null) &&
            state.Outputs.Count == 2 && state.Outputs.ContainsKey("download") && state.Outputs.ContainsKey(standaloneExtractionStep);
        if (!standalone && (state.Active.Select(f => f.StepId).Distinct(StringComparer.Ordinal).Take(2).Count() < 2 ||
            boundary == null && state.BoundaryResults.Count == 0)) return promotable;
        var unique = state.Active.GroupBy(f => f.Path, FileProcessingFiles.PathComparer)
            .Where(group => group.Count() == 1).Select(group => group.Key).ToHashSet(FileProcessingFiles.PathComparer);
        var pinned = state.BoundaryResults.Values.SelectMany(b => b.Files).Select(f => f.Path)
            .Concat(state.RetainedFiles).ToHashSet(FileProcessingFiles.PathComparer);
        foreach (var group in state.Active.Where(f => f.Extracted && !f.RetainedIncompleteArchive && f.StepId != null && (boundary == null || f.StepId == boundary))
                     .GroupBy(f => f.StepId!))
        {
            var policy = state.StepPolicies.GetValueOrDefault(group.Key);
            if (policy?.CleanupIntermediates == false || MaterializationMode(state, policy) != "auto" ||
                !state.Outputs.TryGetValue(group.Key, out var outputs)) continue;
            var owned = RegisteredExtractionOutputs(state, directory, group.Key, ct, promoting: true);
            if (owned == null) continue;
            var produced = outputs.Select(f => f.Path).ToHashSet(FileProcessingFiles.PathComparer);
            foreach (var item in group)
                if (unique.Contains(item.Path) && !pinned.Contains(item.Path) && produced.Contains(item.Path) && owned.TryGetValue(item.Path, out var writer) &&
                    state.FileContents.TryGetValue(ContentKey(state, item.Path), out var contents) && SameContent(writer, contents))
                    promotable.Add(item.Path);
        }
        return promotable;
    }

    private static async Task<bool> PrepareFinalAssemblyAsync(State state, FileProcessingPlan plan, string directory, string resultDirectory,
        string? boundary, Func<Task> save, CancellationToken ct)
    {
        if (state.Active.Any(f => !IsInside(directory, f.Path))) return false;
        var standalone = plan.Steps is [{ Op: "extractArchive" } step] && step.StoragePolicy?.CleanupIntermediates != false &&
            step.StoragePolicy?.Materialization != "copy" ? step.Id : null;
        var promotable = PromotableExtractions(state, directory, boundary, ct, standalone);
        bool Move(Artifact file) => promotable.Contains(file.Path) || boundary == null && CanMoveToFinal(state, directory, file);
        if (!state.Active.Any(Move)) return false;
        await VerifyContents(state, state.Active.Select(a => a.Path), ct);
        VerifyActiveDirectories(state);
        var manifest = ManifestFor(state, state.Active, resultDirectory);
        var entries = manifest.Entries.ToDictionary(e => e.RelativePath, FileProcessingFiles.PathComparer);
        state.FinalAssembly = new(1, manifest, state.Active.Select(a => new FinalAssemblyFile(a.Path,
            a.RelativePath.Replace('\\', '/'), entries[a.RelativePath.Replace('\\', '/')], Move(a),
            MaterializationMode(state, a.StepId != null ? state.StepPolicies.GetValueOrDefault(a.StepId) : state.StoragePolicy))).ToArray(),
            "final-copy-" + Guid.NewGuid().ToString("N"))
        {BoundaryStepId = boundary, PromoteExtractionOutputs = promotable.Count > 0, StandaloneExtractionStep = boundary == null ? standalone : null};
        // This receipt precedes the first rename. Files can subsequently live at their
        // original private path, in pending, or in the committed result directory.
        await save();
        return true;
    }

    private async Task AssembleFinalAsync(State state, string directory, string resultDirectory, Func<Task> save, CancellationToken ct)
    {
        var journal = state.FinalAssembly!;
        if (journal.Version != 1 || journal.Files.Count != state.Active.Count ||
            !SameManifestContents(journal.Manifest, ManifestFor(state, state.Active, resultDirectory)) ||
            !journal.CopyDirectoryName.StartsWith("final-copy-", StringComparison.Ordinal) ||
            !Guid.TryParseExact(journal.CopyDirectoryName[11..], "N", out _))
            throw new InvalidDataException("The final assembly receipt no longer matches the processing result.");
        var promotable = journal.PromoteExtractionOutputs ? PromotableExtractions(state, directory, journal.BoundaryStepId, ct, journal.StandaloneExtractionStep)
            : new HashSet<string>(FileProcessingFiles.PathComparer);
        for (var index = 0; index < journal.Files.Count; index++)
        {
            var entry = journal.Files[index]; var active = state.Active[index];
            if (!FileProcessingFiles.PathComparer.Equals(entry.Source, active.Path) ||
                !FileProcessingFiles.PathComparer.Equals(entry.RelativePath, active.RelativePath.Replace('\\', '/')) ||
                !state.FileContents.TryGetValue(ContentKey(state, entry.Source), out var content) || !SameContent(entry.Content, content) ||
                entry.Mode is not ("auto" or "copy") || entry.Move && !promotable.Contains(active.Path) &&
                    (journal.BoundaryStepId != null || !CanMoveToFinal(state, directory, active)))
                throw new InvalidDataException("A final assembly member changed after it was recorded.");
            FileProcessingFiles.Within(entry.Source, directory);
            ArtifactManifestFiles.RejectLinks(entry.Source);
        }
        var pending = FileProcessingFiles.Within(resultDirectory + ".pending", directory);
        var copying = FileProcessingFiles.Within(Path.Combine(directory, journal.CopyDirectoryName), directory);
        FileProcessingFiles.Within(resultDirectory, directory);
        var committed = Directory.Exists(resultDirectory);
        if (committed && Directory.Exists(pending))
            throw new InvalidDataException("Both pending and committed final directories exist; neither was discarded.");
        var destination = committed ? resultDirectory : pending;
        if (Directory.Exists(destination)) CheckFinalAssemblyEntries(destination, journal.Manifest, committed);
        if (Directory.Exists(copying) && Directory.EnumerateFileSystemEntries(copying).Any(p =>
                !FileProcessingFiles.PathComparer.Equals(p, Path.Combine(copying, "file")) || Directory.Exists(p)))
            throw new InvalidDataException("The private final-copy staging directory contains an unknown entry.");
        ArtifactManifestFiles.RejectLinks(copying);
        ArtifactManifestFiles.RejectLinks(Path.Combine(copying, "file"));
        // Preflight every survivor before moving another member. Existing legacy
        // pending copies are reusable only when their full content matches the receipt.
        var partials = new Dictionary<string, ArtifactManifestEntry>(FileProcessingFiles.PathComparer);
        foreach (var entry in journal.Files)
        {
            var target = FileProcessingFiles.Within(Path.Combine(destination, entry.RelativePath), destination);
            if (File.Exists(target))
            {
                var actual = (await ArtifactManifestFiles.CaptureAsync(destination, [target], ct)).Entries.Single();
                if (!SameContent(actual, entry.Content))
                {
                    if (committed || actual.Length >= entry.Content.Length || !File.Exists(entry.Source) ||
                        !await IsExactPrefixAsync(target, entry.Source, actual.Length, ct))
                        throw new InvalidDataException("A pending final assembly file is incomplete or changed; it was retained for review.");
                    partials.Add(entry.RelativePath, actual);
                }
            }
            else if (committed) throw new InvalidDataException("A committed final assembly member is missing.");
            if (File.Exists(entry.Source)) await VerifyFinalAssemblyFile(entry.Source, entry.Content, ct);
            else if (!entry.Move || !File.Exists(target))
                throw new InvalidDataException("A final assembly source and its committed replacement are unavailable.");
        }
        if (committed) return;
        if (partials.Count > 0)
        {
            // A legacy interrupted copy can be replaced only with positive byte evidence:
            // its complete contents are an exact prefix of the still-verified full source.
            // Persist each precise partial identity before retiring that redundant prefix.
            state.FinalAssembly = journal with {ReplacedPartialCopies = journal.ReplacedPartialCopies
                .Concat(partials.Values).Distinct().ToArray()};
            await save();
        }
        Directory.CreateDirectory(pending);
        ArtifactManifestFiles.CreateDirectories(pending, journal.Manifest);
        foreach (var entry in journal.Files)
        {
            ct.ThrowIfCancellationRequested();
            var target = FileProcessingFiles.Within(Path.Combine(pending, entry.RelativePath), pending);
            if (partials.TryGetValue(entry.RelativePath, out var partial))
            {
                await VerifyFinalAssemblyFile(target, partial, ct);
                File.Delete(target);
            }
            if (File.Exists(target)) continue;
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            if (entry.Move && await materializer.TryMoveAsync(entry.Source, target, ct))
            {
                await VerifyFinalAssemblyFile(target, entry.Content, ct);
                continue;
            }
            Directory.CreateDirectory(copying);
            var staged = Path.Combine(copying, "file");
            // Only this persisted journal owns this exact disposable fallback file.
            // Its original is verified above; never discard a member of result.pending.
            if (File.Exists(staged)) File.Delete(staged);
            await Materialize(entry.Source, staged, entry.Mode, ct);
            await VerifyFinalAssemblyFile(staged, entry.Content, ct);
            File.Move(staged, target);
            Directory.Delete(copying);
        }
        CheckFinalAssemblyEntries(pending, journal.Manifest, complete: true);
        await VerifyOutputManifest(pending, journal.Manifest, ct);
        Directory.Move(pending, resultDirectory);
    }

    private static async Task<bool> IsExactPrefixAsync(string candidate, string source, long length, CancellationToken ct)
    {
        ArtifactManifestFiles.RejectLinks(candidate);
        ArtifactManifestFiles.RejectLinks(source);
        await using var partial = new FileStream(candidate, FileMode.Open, FileAccess.Read, FileShare.Read);
        await using var full = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (partial.Length != length || full.Length <= length) return false;
        var left = new byte[65536]; var right = new byte[65536];
        for (long remaining = length; remaining > 0;)
        {
            var count = (int)Math.Min(left.Length, remaining);
            await partial.ReadExactlyAsync(left.AsMemory(0, count), ct);
            await full.ReadExactlyAsync(right.AsMemory(0, count), ct);
            if (!left.AsSpan(0, count).SequenceEqual(right.AsSpan(0, count))) return false;
            remaining -= count;
        }
        return true;
    }

    private static async Task VerifyFinalAssemblyFile(string path, ArtifactManifestEntry expected, CancellationToken ct)
    {
        var actual = await ArtifactManifestFiles.CaptureAsync(Path.GetDirectoryName(path)!, [path], ct);
        if (!SameContent(actual.Entries.Single(), expected))
            throw new InvalidDataException("A pending final assembly file is incomplete or changed; it was retained for review.");
    }

    private static void CheckFinalAssemblyEntries(string root, ArtifactManifest manifest, bool complete)
    {
        var expected = ArtifactManifestFiles.ResolveFiles(root, manifest).ToHashSet(FileProcessingFiles.PathComparer);
        var actual = FileProcessingFiles.Enumerate(root).ToHashSet(FileProcessingFiles.PathComparer);
        if (complete ? !actual.SetEquals(expected) : !actual.IsSubsetOf(expected))
            throw new InvalidDataException("The final assembly directory contains unexpected or missing files.");
        var allowedDirectories = new HashSet<string>(ArtifactManifestFiles.ResolveDirectories(root, manifest), FileProcessingFiles.PathComparer);
        foreach (var path in expected.Concat(allowedDirectories).ToArray())
            for (var parent = Path.GetDirectoryName(path); parent != null && !FileProcessingFiles.PathComparer.Equals(parent, root); parent = Path.GetDirectoryName(parent))
                allowedDirectories.Add(FileProcessingFiles.Within(parent, root));
        foreach (var path in Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories))
            if (!allowedDirectories.Contains(FileProcessingFiles.Within(path, root)))
                throw new InvalidDataException("The final assembly directory contains an unknown directory.");
        if (complete) ArtifactManifestFiles.VerifyDirectories(root, manifest);
    }

    private static async Task<FileProcessingPlanResult> CommitResultAsync(State state, string resultDirectory,
        string? stopAfterStepId, bool isFinalBoundary, Func<Task> save, CancellationToken ct)
    {
        state.FinalFiles = FileProcessingFiles.Enumerate(resultDirectory);
        ArtifactManifest? resultManifest = null;
        if (state.LineageVersion == 1)
        {
            resultManifest = await CaptureContents(state, resultDirectory, state.FinalFiles, ct,
                state.DirectoryVersion == 1 ? state.ActiveDirectories.Select(d =>
                    FileProcessingFiles.Within(Path.Combine(resultDirectory, d.RelativePath), resultDirectory)).Distinct(FileProcessingFiles.PathComparer).ToArray() : null);
            var expected = ManifestFor(state, state.Active, resultDirectory);
            if (!SameManifestContents(resultManifest, expected))
                throw new InvalidDataException("A final processing file differs from its recorded output.");
            if (isFinalBoundary) state.FinalManifest = resultManifest;
        }
        if (state.FinalAssembly != null)
            foreach (var entry in state.FinalAssembly.Files)
                AddTransition(state, entry.Source, [FileProcessingFiles.Within(Path.Combine(resultDirectory, entry.RelativePath), resultDirectory)]);
        if (stopAfterStepId != null)
            state.BoundaryResults[stopAfterStepId] = new() {Directory = resultDirectory, Directories = state.ActiveDirectories.ToList(),
                Files = state.Active.Select(a => a with {Path = FileProcessingFiles.Within(Path.Combine(resultDirectory, a.RelativePath), resultDirectory)}).ToList(),
                Manifest = resultManifest};
        state.FinalExtractedFiles = state.Active.Where(a => a.Extracted)
            .Select(a => FileProcessingFiles.Within(Path.Combine(resultDirectory, a.RelativePath), resultDirectory)).ToList();
        if (!isFinalBoundary && state.FinalAssembly is {BoundaryStepId: not null} assembly)
        {
            // Future steps and branches address the canonical, pinned boundary. Keep the old
            // private path in cleanup evidence when an unsupported rename fell back to copying.
            var promoted = assembly.Files.Where(f => f.Move).ToDictionary(f => f.Source,
                f => FileProcessingFiles.Within(Path.Combine(resultDirectory, f.RelativePath), resultDirectory),
                FileProcessingFiles.PathComparer);
            Artifact Canonical(Artifact file) => promoted.TryGetValue(file.Path, out var path) ? file with {Path = path} : file;
            foreach (var step in state.Outputs.Keys.ToArray()) state.Outputs[step] = state.Outputs[step].Select(Canonical).ToList();
            state.Active = state.Active.Select(Canonical).ToList();
            state.StagedFiles = state.StagedFiles.Concat(promoted.Keys.Where(File.Exists)).Distinct(FileProcessingFiles.PathComparer).ToList();
            state.FinalAssembly = null;
        }
        state.Completed = isFinalBoundary;
        await save();
        return CompletedResult(state, state.FinalFiles, resultDirectory);
    }
}
