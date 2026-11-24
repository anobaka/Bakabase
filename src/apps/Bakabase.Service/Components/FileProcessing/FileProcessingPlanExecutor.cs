using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Enumeration;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.Compression;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.InsideWorld.Business.Components.Dependency.Exceptions;

namespace Bakabase.Service.Components.FileProcessing;

public record FileProcessingPlanResult(bool Completed, IReadOnlyList<string> Files, string? OutputDirectory,
    string? Message = null, string? StepId = null, bool NeedsPassword = false,
    IReadOnlyList<string>? TriedPasswords = null, IReadOnlyList<string>? ExtractedFiles = null,
    ArtifactStoragePolicy? StoragePolicy = null, IReadOnlyDictionary<string, ArtifactStoragePolicy>? FilePolicies = null, string? StorageTitle = null,
    string? FailureCode = null, FileLineage? FileLineage = null, IReadOnlyList<string>? Directories = null,
    ArtifactDirectoryProvenance? DirectoryProvenance = null, IReadOnlyDictionary<string, string>? ExtractionLayouts = null,
    string? RequiredComponentId = null, FileProcessingPathConflict? PathConflict = null)
{
    internal IncompleteArchiveGroup? IncompleteArchiveGroup { get; init; }
    internal IReadOnlyList<string>? RetainedArchiveFiles { get; init; }
    internal IReadOnlyList<string>? RetainedGroupTokens { get; init; }
}

/// <summary>
/// Interprets the restricted file-processing vocabulary using the shared archive service. Each run
/// isolates its inputs and commits operation outputs atomically, so retries never rename or
/// unpack completed work again. Original downloads and shared outputs are retained.
/// </summary>
public sealed partial class FileProcessingPlanExecutor(IArchiveExtractionService extraction, IFileArtifactMaterializer? materializer = null,
    IStorageWriteGuard? writeGuard = null)
{
    private readonly IFileArtifactMaterializer materializer = materializer ?? (writeGuard == null ? new FileArtifactMaterializer() : new FileArtifactMaterializer(writeGuard));
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Gates = new();
    private const string NoInputMatchesMessage = "No input files matched this step. Correct the selector before continuing.";

    public sealed record RegisteredSnapshotSource(ArtifactRecord Registration, Func<CancellationToken, Task<ArtifactManifest>> ReadCurrentManifest)
    {
        internal ArchiveGroupExecution? ArchiveGroup { get; init; }
        internal bool BorrowSelectedProcessedArchives { get; init; }
    }

    public static void Validate(FileProcessingPlan plan) => FileProcessingPlanValidator.Validate(plan);

    public async Task<FileProcessingPlanResult> ExecuteAsync(FileProcessingPlan plan, string root,
        IReadOnlyList<string> files, string stateDirectory, string? password = null,
        Action<int>? progress = null, CancellationToken ct = default, string? stopAfterStepId = null,
        Func<string, string>? identityPath = null, ArtifactStoragePolicy? storagePolicy = null, string? storageTitle = null,
        bool extractionContentsAsRoot = false, IReadOnlyList<string>? inputDirectories = null,
        FileProcessingCheckpointInput? checkpointInput = null, ArtifactDirectoryProvenance? inputDirectoryProvenance = null,
        Func<string, CancellationToken, Task<IReadOnlyList<string>>>? passwordEvidence = null,
        Func<string, Task>? reportStage = null, string? originalDownloadPassword = null,
        Func<CancellationToken, Task<ArtifactRecord>>? resolveReadOnlyArchiveInput = null,
        RegisteredSnapshotSource? snapshotSource = null)
    {
        Validate(plan);
        if (reportStage != null) await reportStage("preparingFiles");
        if (stopAfterStepId != null && !plan.Steps.Any(s => s.Id == stopAfterStepId))
            throw new ArgumentException("The requested processing boundary is not part of the plan.");
        if (plan.Requirement == "unknown" || (plan.Requirement == "required" && plan.Steps.Count == 0))
            return new(false, files, null, "The file-processing instructions are incomplete. Provide a plan or confirm the files were processed manually.");
        // A pinned snapshot has its own fully checked physical paths. Its original paths are
        // semantic identities only; touching a disconnected source here defeats local replay.
        var normalized = (checkpointInput == null ? files.Select(f => FileProcessingFiles.Within(f, root)) :
                ArtifactManifestFiles.ResolveFiles(root, checkpointInput.InputManifest))
            .Distinct(FileProcessingFiles.PathComparer).OrderBy(x => x, FileProcessingFiles.PathComparer).ToList();
        if (checkpointInput != null && !normalized.ToHashSet(FileProcessingFiles.PathComparer).SetEquals(files))
            throw new InvalidDataException("The pinned input differs from the requested artifact.");
        if (normalized.Count == 0) throw new InvalidOperationException("No files were supplied for this plan.");
        ct.ThrowIfCancellationRequested();
        // The download manifest already names the first step's inputs. Reject a known empty
        // selection before copying potentially large originals; later outputs do not exist yet.
        var first = plan.Steps.FirstOrDefault();
        if (first != null && first.Op != "rebaseDirectory" && first.GetInputReferences().SequenceEqual(["download"]) && !normalized.Any(file =>
                Matches(new Artifact(file, Path.GetRelativePath(root, file)), first.Selector)))
            return new(false, normalized, null, NoInputMatchesMessage, first.Id, FailureCode: "ProcessingInputNotMatched");
        var identity = CheckpointIdentity(plan, normalized, identityPath);
        var directories = (checkpointInput == null ? (inputDirectories ?? []).Select(path => FileProcessingFiles.Within(path, root)) :
                ArtifactManifestFiles.ResolveDirectories(root, checkpointInput.InputManifest))
            .OrderBy(path => path, StringComparer.Ordinal).ToArray();
        if (first?.Op == "rebaseDirectory" && first.GetInputReferences().SequenceEqual(["download"]) &&
            !normalized.Any(path => IsWithinRelativeDirectory(Path.GetRelativePath(root, path), first.SourceDirectory!)) &&
            !directories.Any(path => IsWithinRelativeDirectory(Path.GetRelativePath(root, path), first.SourceDirectory!, includeSelf: true)))
            return new(false, normalized, null, NoInputMatchesMessage, first.Id, FailureCode: "ProcessingInputNotMatched");
        if (directories.Length > 0)
            identity = ArtifactManifestFiles.Hash(identity + "\ndirectories-v1\n" + JsonSerializer.Serialize(
                directories.Select(path => Path.GetRelativePath(root, path).Replace('\\', '/')).ToArray()));
        var directory = Path.Combine(stateDirectory, identity);
        if (checkpointInput != null && !FileProcessingFiles.PathComparer.Equals(directory, checkpointInput.Directory))
            throw new InvalidDataException("The processing checkpoint identity changed.");
        var gate = Gates.GetOrAdd(directory, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            if (checkpointInput != null) await ValidatePinnedInputAsync(directory, plan, checkpointInput, ct);
            return await Run(plan, root, normalized, directory, password, progress, ct, stopAfterStepId, storagePolicy, storageTitle, extractionContentsAsRoot, directories, inputDirectoryProvenance, passwordEvidence, reportStage, originalDownloadPassword, resolveReadOnlyArchiveInput, snapshotSource);
        }
        catch (LocalStorageException error)
        { return new(false, normalized, null, error.Code, FailureCode: "StorageUnavailable"); }
        catch (PathConflictException error)
        { return new(false, normalized, null, error.Message, error.Conflict.StepId,
            FailureCode: FileProcessingPathProjection.FailureCode, PathConflict: error.Conflict); }
        finally { gate.Release(); }
    }

    internal static string CheckpointIdentity(FileProcessingPlan plan, IReadOnlyList<string> normalized,
        Func<string, string>? identityPath = null) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
        IdentityJson(plan) + "\n" + string.Join("\n", normalized.Select(path => identityPath?.Invoke(path) ?? path))))).ToLowerInvariant();

    // Policy changes never abandon a compatible legacy checkpoint or cause another download.
    internal static string IdentityJson(FileProcessingPlan plan) => JsonSerializer.Serialize(
        plan with {Steps = plan.Steps.Select(s => s with {StoragePolicy = null}).ToList()}, Json);

    private async Task<FileProcessingPlanResult> Run(FileProcessingPlan plan, string root,
        List<string> files, string directory, string? password, Action<int>? progress, CancellationToken ct, string? stopAfterStepId, ArtifactStoragePolicy? storagePolicy, string? storageTitle, bool extractionContentsAsRoot,
        IReadOnlyList<string> inputDirectories, ArtifactDirectoryProvenance? inputDirectoryProvenance,
        Func<string, CancellationToken, Task<IReadOnlyList<string>>>? passwordEvidence, Func<string, Task>? reportStage,
        string? originalDownloadPassword, Func<CancellationToken, Task<ArtifactRecord>>? resolveReadOnlyArchiveInput,
        RegisteredSnapshotSource? snapshotSource)
    {
        FileProcessingFiles.Within(directory, directory);
        var stateFile = Path.Combine(directory, "state.json");
        var hadState = File.Exists(stateFile);
        var state = hadState
            ? JsonSerializer.Deserialize<State>(await File.ReadAllTextAsync(stateFile, ct), Json)!
            : NewState(plan, storagePolicy, storageTitle, extractionContentsAsRoot, inputDirectoryProvenance);
        if (state.ExtractionContentsAsRoot != extractionContentsAsRoot)
            throw new InvalidOperationException("The processing checkpoint belongs to a different extraction layout.");
        if ((state.ReadOnlyVolumeArtifactId == null) != (state.ReadOnlyVolumeMembers == null) ||
            state.ReadOnlyVolumeMembers is { Count: 0 })
            throw new FileProcessingInputChangedException("The registered read-only archive receipt is incomplete.");
        if (state.SnapshotSourceArtifactId != null && (snapshotSource?.Registration.Id != state.SnapshotSourceArtifactId ||
            !FileDispositionProofs.SameManifest(snapshotSource.Registration.Manifest, state.InputManifest)))
            throw new FileProcessingInputChangedException("The private processing checkpoint no longer matches its registered source.");
        state.ExecutionRoot = directory;
        async Task Save()
        {
            var temp = stateFile + ".tmp";
            await using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                await JsonSerializer.SerializeAsync(stream, state, Json, ct);
                await stream.FlushAsync(ct);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temp, stateFile, true);
        }
        var groupScope = snapshotSource?.ArchiveGroup;
        if (groupScope?.RequestedToken is {} requested && !state.Operations.Values.Any(o => o.RetainedGroupToken == requested) &&
            (state.PendingOperation == null || DescribeIncompleteGroup(state, state.PendingOperation, groupScope)?.Token != requested))
            throw new FileProcessingInputChangedException("The incomplete archive group changed. Review its checkpoint again.");
        var boundaryIndex = stopAfterStepId == null ? plan.Steps.Count - 1 : plan.Steps.FindIndex(s => s.Id == stopAfterStepId);
        var isFinalBoundary = boundaryIndex == plan.Steps.Count - 1;
        var resultDirectory = stopAfterStepId == null || isFinalBoundary ? Path.Combine(directory, "result") :
            Path.Combine(directory, "result-step-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(stopAfterStepId)))[..16]);
        if (state.FinalAssembly != null && !state.Completed)
        {
            var assembling = state.FinalAssembly.BoundaryStepId;
            if (assembling != null ? isFinalBoundary || assembling != stopAfterStepId : !isFinalBoundary)
                throw new InvalidDataException("Resume the journaled processing boundary before entering another step.");
            await AssembleFinalAsync(state, directory, resultDirectory, Save, ct);
            return await CommitResultAsync(state, resultDirectory, stopAfterStepId, isFinalBoundary, Save, ct);
        }
        if (stopAfterStepId != null && state.BoundaryResults.TryGetValue(stopAfterStepId, out var boundary))
        {
            FileProcessingFiles.Within(boundary.Directory, directory);
            var recorded = boundary.Files.Select(a => FileProcessingFiles.Within(a.Path, boundary.Directory)).ToArray();
            if (!Directory.Exists(boundary.Directory) ||
                !FileProcessingFiles.Enumerate(boundary.Directory).ToHashSet(FileProcessingFiles.PathComparer).SetEquals(recorded))
                throw new InvalidOperationException("A recorded processing boundary changed. Restore its files before replaying it.");
            return CompletedResult(state, recorded, boundary.Directory, boundary.Files);
        }
        if (stopAfterStepId != null && Directory.Exists(resultDirectory))
            return CompletedResult(state, FileProcessingFiles.Enumerate(resultDirectory), resultDirectory);
        if (stopAfterStepId != null && plan.Steps.Skip(boundaryIndex + 1).Any(s => state.Outputs.ContainsKey(s.Id)))
            throw new InvalidOperationException("A recorded intermediate output is missing. Restore it before replaying this processing boundary.");
        if (state.Completed)
        {
            if (state.FinalFiles.Any(f => !File.Exists(f)))
                throw new InvalidOperationException("The completed outputs were moved or removed after this run.");
            return CompletedResult(state, state.FinalFiles, Path.Combine(directory, "result"));
        }
        if (!state.Outputs.ContainsKey("download"))
            PreflightKnownPaths(plan, root, files, inputDirectories, directory, boundaryIndex, state);
        var inputs = Path.Combine(directory, "inputs");
        ReadOnlyArchiveVolumes? borrowed = null;
        ArtifactRecord? registered = null;
        var newSnapshot = !hadState && !Directory.Exists(inputs) && !Directory.Exists(inputs + ".pending");
        // Never reinterpret a complete or partial legacy snapshot. A new receipt binds
        // immutable registered volumes to this exact processing checkpoint and workflow.
        if (resolveReadOnlyArchiveInput != null && (state.ReadOnlyVolumeArtifactId != null ||
            newSnapshot))
        {
            registered = await resolveReadOnlyArchiveInput(ct);
            if (!FileProcessingFiles.PathComparer.Equals(registered.RootDirectory, root) ||
                !files.ToHashSet(FileProcessingFiles.PathComparer).SetEquals(registered.Files))
                throw new FileProcessingInputChangedException("The registered archive input no longer matches this checkpoint.");
            try { borrowed = await ReadOnlyArchiveVolumes.InspectAsync(plan, registered, storagePolicy, ct, snapshotSource?.BorrowSelectedProcessedArchives == true); }
            catch (ArchiveFormatProbeIncompleteException error)
            {
                return new(false, files, null, error.Message, plan.Steps[0].Id, FailureCode: error.Code);
            }
        }
        if (state.ReadOnlyVolumeArtifactId != null && (borrowed == null ||
            state.ReadOnlyVolumeArtifactId != borrowed.Source.Id || state.InputManifest == null ||
            !SameManifestContents(state.InputManifest, borrowed.Source.Manifest) ||
            state.ReadOnlyVolumeMembers?.SequenceEqual(borrowed.RelativeMembers) != true))
            throw new FileProcessingInputChangedException("The registered read-only archive volumes changed.");
        var copyFiles = borrowed == null ? files : files.Where(path => !borrowed.Contains(path)).ToList();
        if (!Directory.Exists(inputs) && writeGuard != null)
        {
            long snapshotBytes = 0;
            foreach (var file in copyFiles)
            {
                ct.ThrowIfCancellationRequested();
                snapshotBytes = checked(snapshotBytes + new FileInfo(file).Length);
            }
            await writeGuard.CheckAllocationAsync(inputs, snapshotBytes, StorageWriteScope.Current, ct);
        }
        Directory.CreateDirectory(directory);
        // Only new checkpoints can authorize content-aware cleanup. Historical steps are
        // reusable but never acquire replacement evidence from today's instructions.
        if (state.LineageVersion == 1 && state.InputManifest == null)
        {
            state.ReadOnlyVolumeArtifactId = borrowed?.Source.Id;
            state.ReadOnlyVolumeMembers = borrowed?.RelativeMembers;
            var expected = borrowed?.Source.Manifest;
            if (borrowed == null && !hadState && snapshotSource != null)
            {
                expected = registered?.Manifest ?? await snapshotSource.ReadCurrentManifest(ct);
                // A fresh capture of the source would silently accept a change made after registration.
                // Keep its frozen manifest and check every byte of the independent copy against it below.
                ArtifactManifestFiles.VerifyDirectories(root, expected);
                state.SnapshotSourceArtifactId = snapshotSource.Registration.Id;
            }
            state.InputManifest = expected ?? await ArtifactManifestFiles.CaptureAsync(root, files, ct,
                state.DirectoryVersion == 1 ? inputDirectories : null);
            await Save();
        }

        // Commit the entire input snapshot together; a crash leaves only our disposable staging directory.
        if (!Directory.Exists(inputs))
        {
            var pending = inputs + ".pending";
            ResetOwnedDirectory(pending, directory);
            foreach (var file in copyFiles)
            {
                ct.ThrowIfCancellationRequested();
                if (!File.Exists(file)) throw new FileNotFoundException("An input file is no longer available.", file);
                var target = FileProcessingFiles.Within(Path.Combine(pending, Path.GetRelativePath(root, file)), pending);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                await Materialize(file, target, MaterializationMode(state, state.StoragePolicy), ct);
            }
            if (state.DirectoryVersion == 1) ArtifactManifestFiles.CreateDirectories(pending, state.InputManifest!);
            Directory.Move(pending, inputs);
        }
        if (!state.Outputs.ContainsKey("download"))
        {
            var provenance = state.GeneratedDirectoryVersion == 1 && state.InputManifest != null
                ? FileArtifactDeliveryLayout.Index(state.InputDirectoryProvenance, state.InputManifest) :
                    new Dictionary<(string, bool), IReadOnlyList<string>>();
            var localInputs = FileProcessingFiles.Enumerate(inputs);
            var logicalInputs = borrowed == null ? localInputs : ArtifactManifestFiles.ResolveFiles(inputs, state.InputManifest!);
            state.Outputs["download"] = logicalInputs
                .Select(f => new Artifact(f, Path.GetRelativePath(inputs, f), Origins: state.LineageVersion == 1
                    ? [new FileOrigin(Path.GetRelativePath(inputs, f).Replace('\\', '/'))] : null,
                    GeneratedDirectories: provenance.GetValueOrDefault((Path.GetRelativePath(inputs, f).Replace('\\', '/'), false)))).ToList();
            if (state.LineageVersion == 1)
            {
                var expected = state.InputManifest!;
                if (borrowed != null)
                {
                    var copied = new List<ArtifactManifestEntry>();
                    foreach (var entry in expected.Entries)
                    {
                        if (borrowed.TrySource(entry.RelativePath, out _))
                            RecordFileContent(state, Path.Combine(inputs, entry.RelativePath), entry);
                        else copied.Add(entry);
                    }
                    expected = new(copied, ArtifactManifestFiles.Fingerprint(copied, expected.Directories), expected.Directories);
                }
                if (borrowed != null && expected.Entries.Count == 0 && localInputs.Count == 0)
                    ArtifactManifestFiles.VerifyDirectories(inputs, expected);
                else
                {
                    var captured = await CaptureContents(state, inputs, localInputs, ct,
                        state.DirectoryVersion == 1 ? ArtifactManifestFiles.ResolveDirectories(inputs, state.InputManifest!) : null);
                    if (!SameManifestContents(captured, expected))
                    {
                        if (state.SnapshotSourceArtifactId != null)
                            throw new FileProcessingInputChangedException("The private processing snapshot differs from the registered input files.");
                        throw new InvalidDataException("The processing snapshot differs from the recorded input files.");
                    }
                }
            }
            state.Active = state.Outputs["download"].ToList();
            if (state.DirectoryVersion == 1)
                state.ActiveDirectories = (state.InputManifest?.Directories ?? []).Select(relative =>
                    new DirectoryArtifact(FileProcessingFiles.Within(Path.Combine(inputs, relative), inputs), relative,
                        provenance.GetValueOrDefault((relative, true)))).ToList();
            state.DirectoryOutputs["download"] = state.ActiveDirectories.ToList();
            if (state.StoragePolicy?.CleanupIntermediates == false)
                state.RetainedFiles = state.RetainedFiles.Concat(state.Active.Select(a => a.Path)).Distinct(FileProcessingFiles.PathComparer).ToList();
            await Save();
        }

        var detectionBudget = new ArchiveDetectionBudget();
        if (reportStage != null) await reportStage("processingFiles");
        foreach (var step in plan.Steps.Take(boundaryIndex + 1))
        {
            ct.ThrowIfCancellationRequested();
            if (state.Outputs.ContainsKey(step.Id)) continue;
            state.StepPolicies.TryGetValue(step.Id, out var stepPolicy);
            var inputReferences = step.GetInputReferences();
            var directVolumes = inputReferences.SequenceEqual(["download"]) ? borrowed : null;
            var supplied = inputReferences.SelectMany(input => state.Outputs[input]).ToList();
            var suppliedDirectories = inputReferences.SelectMany(input => state.DirectoryOutputs.GetValueOrDefault(input) ?? [])
                .Distinct().ToList();
            if (inputReferences.Count > 1)
                CheckProjection(step.Id, supplied.Select(PathEntry));
            var selected = supplied.Where(a => step.Op == "rebaseDirectory"
                ? IsWithinRelativeDirectory(a.RelativePath, step.SourceDirectory!) : Matches(a, step.Selector)).ToList();
            if (step.Op == "extractArchive" && step.ArchiveOpenMode != ArchiveOpenMode.SevenZipParser)
                selected = ExpandAuthorizedArchiveSelection(supplied, selected);
            var selectedDirectories = step.Op == "rebaseDirectory" ? suppliedDirectories.Where(d =>
                IsWithinRelativeDirectory(d.RelativePath, step.SourceDirectory!, includeSelf: true)).ToList() : [];
            // Check logical names before rereading potentially large input contents. A journal
            // still owns its original input identities even when an interrupted move removed them.
            if (step.Op is "renameExtension" or "renameFile" or "renameLiteral" or "moveFile")
                CheckRenameProjection(step, state.Active, state.ActiveDirectories,
                    ProjectRename(step, supplied, selected, Path.Combine(directory, "path-preview")));
            else if (step.Op == "rebaseDirectory")
                CheckRebaseProjection(step, state.Active, state.ActiveDirectories, selected, selectedDirectories);
            if (step.Op != "extractArchive" && (selected.Count > 0 || selectedDirectories.Count > 0))
            {
                var renameKey = RenameOperationKey(step.Id);
                if (!state.Operations.ContainsKey(renameKey))
                    CheckUntrackedDestination(step.Id, OutputDirectory(state, directory, renameKey, step, stepPolicy), directory);
            }
            if (state.LineageVersion == 1)
            {
                var renameKey = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(step.Id)))[..16] + "-rename";
                // An interrupted atomic move may already have removed a source path. Its
                // durable rename journal verifies the moved bytes before committing instead.
                var journalInputs = (step.Op is "renameExtension" or "renameFile" or "renameLiteral" or "moveFile" or "rebaseDirectory") &&
                    state.Operations.TryGetValue(renameKey, out var pendingRename) && pendingRename.RenameFiles != null
                    ? pendingRename.RenameFiles.Where(f => state.FileContents.TryGetValue(ContentKey(state, f.Source), out var recorded) &&
                        SameContent(recorded, f.Content)).Select(f => f.Source).ToHashSet(FileProcessingFiles.PathComparer)
                    : new HashSet<string>(FileProcessingFiles.PathComparer);
                await VerifyContents(state, supplied.Where(a => directVolumes == null || !directVolumes.TrySource(a.RelativePath, out _))
                    .Select(a => a.Path).Where(p => !journalInputs.Contains(p)), ct);
            }
            if (selected.Count == 0 && selectedDirectories.Count == 0)
                return new(false, state.Active.Select(x => x.Path).ToList(), null,
                    NoInputMatchesMessage, step.Id, FailureCode: "ProcessingInputNotMatched");
            if (stepPolicy?.CleanupIntermediates == false)
                state.RetainedFiles = state.RetainedFiles.Concat(supplied.Select(a => a.Path)).Distinct(FileProcessingFiles.PathComparer).ToList();
            var output = new List<Artifact>();
            var outputProjection = new Dictionary<string, FileProcessingPathProjectionEntry>(FileProcessingFiles.PathComparer);
            var extractionReceipts = new Dictionary<string, ArtifactManifestEntry>(FileProcessingFiles.PathComparer);
            var outputDirectories = new List<DirectoryArtifact>();
            var consumed = new HashSet<string>(FileProcessingFiles.PathComparer);
            if (step.Op == "rebaseDirectory")
            {
                var projection = await RebaseDirectory(plan, step, selected, selectedDirectories, state,
                    directory, stepPolicy, Save, ct);
                output.AddRange(projection.Files);
                outputDirectories.AddRange(projection.Directories);
                foreach (var source in selected) consumed.Add(source.Path);
                state.ActiveDirectories = state.ActiveDirectories.Except(selectedDirectories).Concat(outputDirectories).Distinct().ToList();
            }
            else if (step.Op is "renameExtension" or "renameFile" or "renameLiteral" or "moveFile")
            {
                // Preserve inputs referenced by another branch or published boundary. Private
                // sole-consumer files may be renamed through a recoverable move journal.
                var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(step.Id)))[..16] + "-rename";
                var destination = OutputDirectory(state, directory, key, step, stepPolicy);
                var transformed = ProjectRename(step, supplied, selected, destination);
                var renamed = transformed.Select(x => x.Output).ToList();
                CheckRenameProjection(step, state.Active, state.ActiveDirectories, transformed);
                var created = false;
                if (!state.Operations.TryGetValue(key, out var operation))
                {
                    CheckUntrackedDestination(step.Id, destination, directory);
                    operation = new Operation {OutputDirectory = destination};
                    var movable = transformed.Select(x => CanMovePrivateInput(plan, step, state, directory,
                        x.Source.Path, stepPolicy)).ToArray();
                    if (movable.Any(value => value))
                    {
                        var manifest = await ArtifactManifestFiles.CaptureAsync(directory,
                            transformed.Select(x => x.Source.Path).ToArray(), ct);
                        var entries = manifest.Entries.ToDictionary(e =>
                            FileProcessingFiles.Within(Path.Combine(directory, e.RelativePath), directory),
                            FileProcessingFiles.PathComparer);
                        operation.RenameFiles = transformed.Select((x, index) => new RenameFile(
                            x.Source.Path, x.Output.RelativePath, entries[x.Source.Path], movable[index])).ToList();
                        ResetOwnedDirectory(destination + ".pending", directory);
                    }
                    state.Operations[key] = operation;
                    await Save();
                    created = true;
                }
                if (operation.RenameFiles != null)
                    await ExecuteRenameJournal(operation, transformed.Select(x => (x.Source.Path, x.Output.RelativePath)).ToList(),
                        directory, MaterializationMode(state, stepPolicy), !created, ct);
                else if (!operation.Completed && !Directory.Exists(destination))
                {
                    var pending = destination + ".pending";
                    ResetOwnedDirectory(pending, directory);
                    for (var i = 0; i < transformed.Count; i++)
                    {
                        var target = FileProcessingFiles.Within(Path.Combine(pending, renamed[i].RelativePath), pending);
                        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                        await Materialize(transformed[i].Source.Path, target, MaterializationMode(state, stepPolicy), ct);
                    }
                    Directory.Move(pending, destination);
                }
                if (state.LineageVersion == 1)
                {
                    await CaptureContents(state, destination, renamed.Select(a => a.Path).ToArray(), ct);
                    foreach (var item in transformed)
                    {
                        if (!SameContent(state.FileContents[ContentKey(state, item.Source.Path)], state.FileContents[ContentKey(state, item.Output.Path)]))
                            throw new InvalidDataException("A renamed file differs from its recorded source.");
                        AddTransition(state, item.Source.Path, [item.Output.Path]);
                    }
                }
                operation.Completed = true;
                await Save();
                foreach (var source in transformed) consumed.Add(source.Source.Path);
                output.AddRange(renamed);
                outputDirectories.AddRange(suppliedDirectories);
            }
            else
            {
                // Separate declared outputs live in separate immutable directories. Join only
                // selected manifest entries in a private atomic snapshot so archive tools can
                // find companion volumes, without discovering files beside an original input.
                var extractionInputs = inputReferences.Count > 1
                    ? await StageExtractionInputs(selected, step.Id, directory, MaterializationMode(state, stepPolicy), ct) : directVolumes == null ? selected :
                    selected.Select(a => directVolumes.TrySource(a.RelativePath, out var source) ? a with {Path = source} : a).ToList();
                if (inputReferences.Count > 1)
                {
                    state.StagedFiles = state.StagedFiles.Concat(extractionInputs.Select(a => a.Path)).Distinct(FileProcessingFiles.PathComparer).ToList();
                    if (stepPolicy?.CleanupIntermediates == false)
                        state.RetainedFiles = state.RetainedFiles.Concat(extractionInputs.Select(a => a.Path)).Distinct(FileProcessingFiles.PathComparer).ToList();
                    await Save();
                }
                var originalPaths = extractionInputs.Zip(selected)
                    .ToDictionary(pair => pair.First.Path, pair => pair.Second.Path, FileProcessingFiles.PathComparer);
                if (state.LineageVersion == 1 && inputReferences.Count > 1)
                {
                    foreach (var staged in extractionInputs)
                    {
                        await CaptureContents(state, directory, [staged.Path], ct);
                        if (!SameContent(state.FileContents[ContentKey(state, staged.Path)], state.FileContents[ContentKey(state, originalPaths[staged.Path])]))
                            throw new InvalidDataException("A staged archive input differs from its source.");
                        AddTransition(state, staged.Path, [originalPaths[staged.Path]]);
                    }
                    await Save();
                }
                IReadOnlyList<DetectedArchiveGroup> groups;
                var literalSelector = !string.IsNullOrWhiteSpace(step.Selector) &&
                                      step.Selector.IndexOfAny(['*', '?']) < 0;
                // A source instruction may identify only the .exe family, not exact names. An explicit
                // bounded selector authorizes archive-tool inspection, never program execution.
                var executableSelector = step.Selector?.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) == true;
                var sfxCandidates = CompressedFileHelper.DetectSfxMultipartCandidates(extractionInputs.Select(a => a.Path).ToArray());
                var oneSelectedSfxFamily = sfxCandidates.Count == 1 &&
                    sfxCandidates[0].Files.ToHashSet(FileProcessingFiles.PathComparer).SetEquals(extractionInputs.Select(a => a.Path));
                var stepKey = StepKey(step.Id);
                if (HasUnboundArchiveResponse(state, stepKey))
                    return ArchiveGroupChanged(state, step.Id);
                // One entry or one complete SFX family has an unambiguous group-zero answer.
                // Bind header-password evidence to every member, not just the executable.
                var formatFiles = oneSelectedSfxFamily ? sfxCandidates[0].Files : extractionInputs.Count == 1
                    ? new[] {extractionInputs[0].Path} : Array.Empty<string>();
                var formatKeys = formatFiles.Select(file => ContentKey(state, originalPaths[file])).ToArray();
                var formatPasswords = new List<string?>();
                if (formatKeys.Length > 0 && state.Operations.TryGetValue(stepKey + "-0", out var formatOperation) &&
                    formatOperation.ArchiveInputs?.SequenceEqual(formatKeys, FileProcessingFiles.PathComparer) == true)
                {
                    if (password != null && state.PendingOperation == stepKey + "-0") formatPasswords.Add(password);
                    if (state.Passwords.TryGetValue(stepKey + "-0", out var formatAnswer)) formatPasswords.Add(formatAnswer);
                }
                var formatInput = formatKeys.Length == 1 ? formatKeys[0] : formatKeys.Length > 1
                    ? "archive-family:" + ArtifactManifestFiles.Hash(JsonSerializer.Serialize(formatKeys)) : null;
                if (formatInput != null && state.SourceFormatEvidence.TryGetValue(stepKey, out var priorFormat))
                {
                    if (priorFormat.Input != formatInput) return ArchiveGroupChanged(state, step.Id);
                    if (password != null) formatPasswords.Add(password);
                    if (priorFormat.VerifiedPassword != null) formatPasswords.Add(priorFormat.VerifiedPassword);
                }
                formatPasswords.Add(step.Password);
                var sourcePassword = inputReferences is ["download"] ? originalDownloadPassword : null;
                if (sourcePassword != null && !formatPasswords.Contains(sourcePassword)) formatPasswords.Add(sourcePassword);
                Task<IReadOnlyList<DetectedArchiveGroup>> DetectGroups() => AuthorizedArchiveDetection.DetectAsync(
                    extractionInputs.Select(a => a.Path).ToArray(), recursive: false, ct, extraction, detectionBudget,
                    exactExtractionSelection: selected.Count == 1 || literalSelector || executableSelector || oneSelectedSfxFamily, authorizedPasswords: formatPasswords, readOnlyVolumes: directVolumes?.Groups);
                try
                {
                    if (step.ArchiveOpenMode == ArchiveOpenMode.SevenZipParser)
                    {
                        if (extractionInputs.Count != 1)
                            return new(false, state.Active.Select(a => a.Path).ToList(), null,
                                "Parser mode requires exactly one selected input file.", step.Id, FailureCode: "ArchiveInputRequired");
                        groups = [new DetectedArchiveGroup {Files = [extractionInputs[0].Path],
                            Extension = "#", OpenOptions = ArchiveOpenOptions.Parser(step.ArchiveMemberSelector)}];
                    }
                    else
                    {
                        try { groups = await DetectGroups(); }
                        catch (ArchiveFormatProbeIncompleteException error) when (
                            error.Code == ArchiveFormatProbeFailureCodes.PasswordRequired && formatInput != null &&
                            !state.SourceFormatEvidence.ContainsKey(stepKey))
                        {
                            state.SourceFormatEvidence[stepKey] = new(formatInput, null);
                            await Save();
                            if (passwordEvidence == null || state.SourceFormatEvidence.Count > 3) throw;
                            var verified = await ProbeEvidenceAsync(formatFiles[0], formatPasswords, null, passwordEvidence, ct);
                            if (verified == null) throw;
                            state.SourceFormatEvidence[stepKey] = new(formatInput, verified.Password);
                            await Save();
                            formatPasswords.Add(verified.Password);
                            groups = await DetectGroups();
                        }
                    }
                }
                catch (ArchiveFormatProbeIncompleteException error)
                {
                    return new(false, state.Active.Select(x => x.Path).ToList(), null, error.Message, step.Id, FailureCode: error.Code);
                }
                catch (DependencyNotInstalledException error)
                {
                    return new(false, state.Active.Select(x => x.Path).ToList(), null, error.Message, step.Id,
                        FailureCode: "DEPENDENCY_REQUIRED", RequiredComponentId: error.ComponentId);
                }
                catch (ArchivePasswordEncodingException error)
                {
                    return new(false, state.Active.Select(x => x.Path).ToList(), null, error.Message, step.Id, FailureCode: error.Code);
                }
                if (groups.Count == 0)
                    return new(false, state.Active.Select(x => x.Path).ToList(), null,
                        "No supported archive matched this step. Select the intended archive by file name or an explicit archive selector before continuing.", step.Id,
                        FailureCode: "ArchiveInputRequired");
                if (!BindArchiveGroups(state, stepKey, groups, originalPaths, out var bindingsChanged))
                    return ArchiveGroupChanged(state, step.Id);
                if (password != null && state.PendingOperation?.StartsWith(stepKey + "-", StringComparison.Ordinal) == true)
                {
                    state.Passwords[state.PendingOperation] = password;
                    bindingsChanged = true;
                }
                if (bindingsChanged) await Save();
                var recoveredPaths = groups.Where(group => group.RecoveredVolumes != null)
                    .SelectMany(group => group.Files).ToHashSet(FileProcessingFiles.PathComparer);
                FileProcessingFiles.ValidateVolumes(extractionInputs.Select(x => x.Path).Where(path => !recoveredPaths.Contains(path)));
                for (var i = 0; i < groups.Count; i++)
                {
                    var group = groups[i];
                    // A per-step ZIP fallback must not become an unsupported method property
                    // on another archive format selected by the same step.
                    var opening = group.Extension?.Equals(".zip", StringComparison.OrdinalIgnoreCase) == true
                        ? ArchiveOpenOptions.WithZipPasswordCodePage(
                            ArchiveOpenOptions.WithZipFileNameCodePage(group.OpenOptions, step.ZipFileNameCodePage), step.ZipPasswordCodePage)
                        : group.OpenOptions;
                    if (step.ZipPasswordCodePage != null && (group.Extension?.Equals(".zip", StringComparison.OrdinalIgnoreCase) != true || group.Files.Count != 1))
                        return new(false, state.Active.Select(x => x.Path).ToList(), null,
                            "Explicit ZIP password encoding requires one ordinary ZIP. Use separate extraction steps for other archive formats.", step.Id,
                            FailureCode: "ArchivePasswordEncodingUnsupported");
                    if (opening?.ZipFileNameCodePage != null && group.Files.Count != 1)
                        return new(false, state.Active.Select(x => x.Path).ToList(), null,
                            "Explicit ZIP filename encoding currently requires a single-volume ZIP.", step.Id,
                            FailureCode: "ArchiveNameEncodingUnsupported");
                    var key = stepKey + "-" + i;
                    var destination = OutputDirectory(state, directory, key, step, stepPolicy);
                    if (!state.Operations.TryGetValue(key, out var operation))
                    {
                        CheckUntrackedDestination(step.Id, destination, directory);
                        operation = new Operation {OutputDirectory = destination,
                            ArchiveInputs = group.Files.Select(file => ContentKey(state, originalPaths[file])).ToList()};
                        state.Operations[key] = operation;
                        await Save();
                    }
                    if (operation.RetainedGroupToken != null)
                    {
                        if (groupScope == null || DescribeIncompleteGroup(state, key, groupScope)?.Token != operation.RetainedGroupToken)
                            return ArchiveGroupChanged(state, step.Id);
                        MarkRetainedGroup();
                        continue;
                    }
                    void MarkRetainedGroup()
                    {
                        var paths = group.Files.Select(file => originalPaths[file]).ToHashSet(FileProcessingFiles.PathComparer);
                        for (var a = 0; a < state.Active.Count; a++)
                            if (paths.Contains(state.Active[a].Path))
                                state.Active[a] = state.Active[a] with {RetainedIncompleteArchive = true};
                    }
                    var extractionPending = ExtractionStagingDirectory(destination, operation, directory);
                    if (state.LineageVersion == 1 && !Directory.Exists(destination) &&
                        operation.OutputManifest != null && Directory.Exists(extractionPending))
                    {
                        await VerifyOutputManifest(extractionPending, operation.OutputManifest, ct, group.Files);
                        Directory.Move(extractionPending, destination);
                    }
                    if (!operation.Completed && !Directory.Exists(destination))
                    {
                        if (writeGuard != null && operation.OutputManifest == null && operation.ExpandedBytes is >= 0 &&
                            (!Directory.Exists(extractionPending) || !Directory.EnumerateFileSystemEntries(extractionPending).Any()))
                        {
                            // A previously verified catalog for this bound group can rule out a retry
                            // before another full password/data test. Stale partial output is reset below.
                            await writeGuard.CheckAllocationAsync(extractionPending, operation.ExpandedBytes.Value, StorageWriteScope.Current, ct);
                        }
                        if (reportStage != null) await reportStage("checkingArchive");
                        var candidates = new List<string?>();
                        if (state.Passwords.TryGetValue(key, out var answered)) candidates.Add(answered);
                        if (formatInput != null && state.SourceFormatEvidence.TryGetValue(stepKey, out var recognized) && recognized.Input == formatInput)
                        {
                            // A manual answer can first unlock header recognition. Test it again against actual archive data.
                            if (password != null) candidates.Add(password);
                            if (recognized.VerifiedPassword != null) candidates.Add(recognized.VerifiedPassword);
                        }
                        candidates.Add(step.Password);
                        if (sourcePassword != null) candidates.Add(sourcePassword);
                        candidates = candidates.Distinct().ToList();
                        SevenZipVolumeSet.ToolInputs? mapped;
                        try { mapped = group.RecoveredVolumes is { } recovered ? new SevenZipVolumeSet.ToolInputs(recovered)
                            : directVolumes?.IsStandaloneZip(group.Files) == true ? new SevenZipVolumeSet.ToolInputs(group.Files[0]) : null; }
                        catch (ArchiveFormatProbeIncompleteException error)
                        { return new(false, state.Active.Select(x => x.Path).ToList(), null, error.Message, step.Id, FailureCode: error.Code); }
                        using var volumeInputs = mapped;
                        IReadOnlyList<string> toolFiles = mapped == null ? group.Files : mapped.Files;
                        if (mapped != null && directVolumes?.IsStandaloneZip(group.Files) == true)
                            opening = ArchiveOpenOptions.ForStandaloneZipAlias(toolFiles[0], group.Files[0]);
                        ArchivePasswordProbe probe;
                        try
                        {
                            AuthorizedArchiveDetection.ValidateCurrentSfxFamilies(group.Files, ct);
                            probe = await extraction.ProbePasswordAsync(toolFiles[0], candidates, null, ct, opening);
                        }
                        catch (ArchiveNameEncodingUnsupportedException error)
                        {
                            return new(false, state.Active.Select(x => x.Path).ToList(), null, error.Message, step.Id,
                                FailureCode: "ArchiveNameEncodingUnsupported");
                        }
                        catch (DependencyNotInstalledException error)
                        {
                            return new(false, state.Active.Select(x => x.Path).ToList(), null, error.Message, step.Id,
                                FailureCode: "DEPENDENCY_REQUIRED", RequiredComponentId: error.ComponentId);
                        }
                        catch (ArchivePasswordEncodingException error)
                        {
                            return new(false, state.Active.Select(x => x.Path).ToList(), null, error.Message, step.Id,
                                FailureCode: error.Code);
                        }
                        catch (ArchiveFormatProbeIncompleteException error)
                        {
                            return new(false, state.Active.Select(x => x.Path).ToList(), null, error.Message, step.Id,
                                FailureCode: error.Code);
                        }
                        if (!probe.Succeeded && probe.FailureCode == null && passwordEvidence != null && !operation.SourcePasswordEvidenceChecked &&
                            (probe.PasswordRejected || probe.WrongPasswords.Count > 0) &&
                            state.Operations.Values.Count(o => o.SourcePasswordEvidenceChecked) < 3)
                        {
                            // Persist before the optional call: replaying a suspended or interrupted archive must
                            // not launch another model request or repeat a rejected set of candidates.
                            operation.SourcePasswordEvidenceChecked = true;
                            await Save();
                            volumeInputs?.Validate();
                            var verified = await ProbeEvidenceAsync(toolFiles[0], candidates, opening, passwordEvidence, ct);
                            if (verified != null)
                            {
                                probe = verified;
                                state.Passwords[key] = verified.Password!;
                                await Save();
                            }
                        }
                        if (!probe.Succeeded)
                        {
                            state.PendingOperation = key;
                            await Save();
                            if (probe.FailureCode == "MissingVolumes" && groupScope != null && groupScope.Owner.StepId == step.Id &&
                                DescribeIncompleteGroup(state, key, groupScope) is {} incomplete)
                            {
                                if (groupScope.RequestedToken == incomplete.Token)
                                {
                                    // The individual archive still fails the same probe. Preserve its original
                                    // bytes in Active without inventing an extracted output or replacement edge.
                                    await VerifyContents(state, group.Files.Select(file => originalPaths[file]), ct);
                                    MarkRetainedGroup();
                                    operation.RetainedGroupToken = incomplete.Token;
                                    state.PendingOperation = null;
                                    await Save();
                                    continue;
                                }
                                return new(false, state.Active.Select(x => x.Path).ToList(), null,
                                    "A required archive volume is missing.", step.Id, FailureCode: "MissingVolumes")
                                    {IncompleteArchiveGroup = incomplete};
                            }
                            if (probe.FailureCode is "MissingVolumes" or "ArchiveInputRequired")
                                return new(false, state.Active.Select(x => x.Path).ToList(), null,
                                    probe.FailureCode == "MissingVolumes"
                                        ? FileProcessingResumeActions.LegacyGroupMissingVolumes
                                        : "The archive tool could not open the selected file. Check the actual format and multipart filenames before retrying; this does not establish a wrong password.",
                                    step.Id, FailureCode: probe.FailureCode);
                            return new(false, state.Active.Select(x => x.Path).ToList(), null,
                                $"Could not open {Path.GetFileName(group.Files[0])}. Check the password and all archive parts.",
                                step.Id, true, candidates.Where(x => x != null).Cast<string>().ToList());
                        }
                        var pending = await PrepareExtractionStagingAsync(destination, operation, state, directory, Save);
                        var request = new ArchiveExtractionRequest(toolFiles, pending, probe.Password,
                            DecompressToNewFolder: false, OpenOptions: opening);
                        ArchiveExtractionResult result;
                        try
                        {
                            var expected = operation.UnverifiedExtractionOwner;
                            using var staging = DeferredEmptyDirectoryTree.PinEmptyExtraction(pending, expected);
                            if (expected != null && staging == null)
                                throw new InvalidDataException("The prepared archive staging directory changed. Its contents were retained.");
                            if (staging != null && expected != staging.Owner)
                            {
                                operation.UnverifiedExtractionOwner = staging.Owner;
                                await Save();
                            }
                            var writerStarted = false;
                            async Task<ArchiveExtractionResult> Extract(CancellationToken token)
                            {
                                volumeInputs?.Validate();
                                AuthorizedArchiveDetection.ValidateCurrentSfxFamilies(group.Files, token);
                                if (reportStage != null) await reportStage("extractingArchive");
                                token.ThrowIfCancellationRequested();
                                if (staging != null)
                                {
                                    staging.ConfirmEmpty();
                                    operation.UnverifiedExtractionOwner = staging.Owner with {WriterStarted = true};
                                    await Save();
                                }
                                writerStarted = true;
                                var extracted = await extraction.ExtractAsync(request, progress, token);
                                if (directVolumes != null && extracted.Succeeded)
                                    await directVolumes.VerifyReadAsync(group.Files, token);
                                if (reportStage != null) await reportStage("processingFiles");
                                return extracted;
                            }
                            volumeInputs?.Validate();
                            var succeeded = false;
                            try
                            {
                                result = await ExtractWithSpaceBudgetAsync(request, operation, Save, Extract, reportStage, ct);
                                succeeded = result.Succeeded;
                            }
                            finally
                            {
                                // The guard has joined the writer and its cancellation callbacks.
                                // Cleanup never changes the extraction's original error or cancellation.
                                if (writerStarted && !succeeded && staging != null && operation.OutputManifest == null)
                                    staging.Discard();
                            }
                        }
                        catch (ArchiveNameEncodingUnsupportedException error)
                        {
                            return new(false, state.Active.Select(x => x.Path).ToList(), null, error.Message, step.Id,
                                FailureCode: "ArchiveNameEncodingUnsupported");
                        }
                        catch (DependencyNotInstalledException error)
                        {
                            return new(false, state.Active.Select(x => x.Path).ToList(), null, error.Message, step.Id,
                                FailureCode: "DEPENDENCY_REQUIRED", RequiredComponentId: error.ComponentId);
                        }
                        catch (ArchivePasswordEncodingException error)
                        {
                            return new(false, state.Active.Select(x => x.Path).ToList(), null, error.Message, step.Id,
                                FailureCode: error.Code);
                        }
                        catch (ArchiveFormatProbeIncompleteException error)
                        {
                            return new(false, state.Active.Select(x => x.Path).ToList(), null, error.Message, step.Id,
                                FailureCode: error.Code);
                        }
                        if (!result.Succeeded) throw new InvalidOperationException(result.Message);
                        // Record actual content before the atomic publication. A crash followed
                        // by edits to the committed directory must not mint new replacement proof.
                        var extractedPaths = FileProcessingFiles.Enumerate(pending);
                        // This operation already encountered an SMB deletion handle. Keep the output
                        // uncommitted if another marker appears in its replacement staging directory.
                        if (operation.ExtractionStagingId.HasValue && extractedPaths.Any(path => FileProcessingFiles.IsSmbDeleteMarker(Path.GetFileName(path))))
                            throw new IOException("The extraction workspace still contains an SMB delete marker. The files were retained for review.");
                        if (state.LineageVersion == 1)
                        {
                            await VerifyContents(state, group.Files.Where(f => directVolumes?.Contains(f) != true), ct);
                            operation.OutputManifest = extractedPaths.Count == 0
                                ? new([], ArtifactManifestFiles.Fingerprint([]))
                                : await ArtifactManifestFiles.CaptureExtractedAsync(pending, extractedPaths, ct,
                                    state.DirectoryVersion == 1 ? ArtifactManifestFiles.CaptureEmptyDirectories(pending, ct) : null);
                            operation.UnverifiedExtractionOwner = null;
                            await Save();
                        }
                        Directory.Move(pending, destination);
                    }
                    ArtifactManifest? verifiedOutput = null;
                    if (state.LineageVersion == 1)
                    {
                        await VerifyContents(state, group.Files.Where(f => directVolumes?.Contains(f) != true), ct);
                        var committedOutput = operation.OutputManifest ?? throw new InvalidDataException(
                            "The extraction output has no committed content receipt; its files were retained.");
                        await VerifyOutputManifest(destination, committedOutput, ct, operation.Completed ? null : group.Files);
                        verifiedOutput = committedOutput;
                    }
                    var entry = extractionInputs.First(a => FileProcessingFiles.PathComparer.Equals(a.Path, group.Files[0]));
                    var wrapperName = group.RecoveredVolumes == null ? CompressedFileHelper.GetExtractionDirectoryName(group.Files, group.Extension) : group.KeyName;
                    var familyEntry = group.RecoveredVolumes == null ? entry : extractionInputs.First(a =>
                        FileProcessingFiles.PathComparer.Equals(a.Path, group.Files[1]));
                    var parent = Path.GetDirectoryName(familyEntry.RelativePath) ?? "";
                    var prefix = step.ExtractionLayout == FileExtractionLayouts.CurrentDirectory ? parent :
                        state.ExtractionContentsAsRoot && step.ExtractionLayout == null ? "" : Path.Combine(parent, wrapperName);
                    var sourceArtifacts = selected.Where(a => group.Files.Any(f =>
                        FileProcessingFiles.PathComparer.Equals(originalPaths[f], a.Path))).ToList();
                    var generatedDirectories = state.GeneratedDirectoryVersion == 1 && (!state.ExtractionContentsAsRoot || step.ExtractionLayout != null) && sourceArtifacts.Count > 0
                        ? sourceArtifacts.Select(a => (a.GeneratedDirectories ?? []).AsEnumerable())
                            .Aggregate((a, b) => a.Intersect(b, StringComparer.Ordinal))
                            .Concat(step.ExtractionLayout != null || wrapperName.Length == 0 ? [] : new[] {prefix.Replace('\\', '/')}).Distinct(StringComparer.Ordinal).ToArray() : [];
                    var partialContainer = opening?.Mode == ArchiveOpenMode.SevenZipParser && opening.MemberSelector != null;
                    // A selected embedded member is not a complete replacement of its container.
                    // Do not propagate its origins: a later full extraction of that member must
                    // not accidentally authorize retirement of the unselected original bytes.
                    var origins = partialContainer ? new List<FileOrigin>() : state.LineageVersion == 1 && sourceArtifacts.All(a => a.Origins != null)
                        ? sourceArtifacts.SelectMany(a => a.Origins!).Select(o => o with {Replaced = true})
                            .Distinct().ToList() : null;
                    var extracted = FileProcessingFiles.Enumerate(destination).Select(f =>
                        new Artifact(f, Path.Combine(prefix, Path.GetRelativePath(destination, f)), true, step.Id, origins, generatedDirectories, step.ExtractionLayout)).ToList();
                    if (verifiedOutput != null)
                    {
                        // The committed receipt has already been verified against the output bytes. Use it
                        // for lineage only if every path in the later directory listing still belongs to it.
                        var receipts = verifiedOutput.Entries.ToDictionary(
                            e => FileProcessingFiles.Within(Path.Combine(destination, e.RelativePath), destination),
                            FileProcessingFiles.PathComparer);
                        const string pathsChanged = "Extraction output paths changed after verification; the files were retained.";
                        if (extracted.Count != receipts.Count) throw new InvalidDataException(pathsChanged);
                        foreach (var file in extracted)
                        {
                            ct.ThrowIfCancellationRequested();
                            if (!receipts.Remove(file.Path, out var receipt)) throw new InvalidDataException(pathsChanged);
                            extractionReceipts[file.Path] = receipt;
                            RecordFileContent(state, file.Path,
                                receipt with {RelativePath = ArtifactManifestFiles.RelativeFile(destination, file.Path)});
                        }
                    }
                    foreach (var extractedFile in extracted)
                        outputProjection[extractedFile.Path] = new(entry.RelativePath.Replace('\\', '/') + "!/" +
                            Path.GetRelativePath(destination, extractedFile.Path).Replace('\\', '/'), extractedFile.RelativePath);
                    if (state.DirectoryVersion == 1 && operation.OutputManifest?.Directories is {Count: > 0} extractedDirectories)
                        foreach (var relative in extractedDirectories)
                        {
                            var item = new DirectoryArtifact(FileProcessingFiles.Within(Path.Combine(destination, relative), destination),
                                Path.Combine(prefix, relative).Replace('\\', '/'), generatedDirectories);
                            if (!state.ActiveDirectories.Contains(item)) state.ActiveDirectories.Add(item);
                            outputDirectories.Add(item);
                        }
                    if (state.LineageVersion == 1 && extracted.Count > 0)
                    {
                        if (partialContainer)
                            state.RetainedFiles = state.RetainedFiles.Concat(group.Files)
                                .Concat(group.Files.Select(f => originalPaths[f])).Distinct(FileProcessingFiles.PathComparer).ToList();
                        else
                            foreach (var file in group.Files)
                                AddTransition(state, originalPaths[file], extracted.Select(a => a.Path).ToArray());
                    }
                    operation.Completed = true;
                    if (state.PendingOperation == key) state.PendingOperation = null;
                    await Save();
                    // An empty extraction proves no replacement payload. Keep its input rather
                    // than treating a successful tool exit as permission to discard the archive.
                    if (extracted.Count > 0)
                        foreach (var file in group.Files) consumed.Add(originalPaths[file]);
                    output.AddRange(extracted);
                    // Other selected archives may still be consumed later in this step. Only
                    // completed outputs are certain here; check retained siblings after all groups.
                    CheckProjection(step.Id, output.Select(a => outputProjection.GetValueOrDefault(a.Path) ?? PathEntry(a))
                            .Concat(outputDirectories.Select(PathEntry)), portable: step.ExtractionLayout != null);
                }
                await CoalesceVerifiedExtractionOverlapsAsync(step, state, output, consumed, outputProjection, extractionReceipts, ct);
            }
            // Keep unrelated files and siblings; following steps can only see their declared input's results.
            var next = state.Active.Where(a => !consumed.Contains(a.Path)).Concat(output).ToList();
            CheckProjection(step.Id, next.Select(a => outputProjection.GetValueOrDefault(a.Path) ?? PathEntry(a))
                    .Concat(state.ActiveDirectories.Select(PathEntry)),
                portable: step.Op == "rebaseDirectory" || step.ExtractionLayout != null);
            state.Active = next;
            state.Outputs[step.Id] = output;
            state.DirectoryOutputs[step.Id] = outputDirectories;
            state.ActiveDirectories = state.ActiveDirectories.Concat(outputDirectories).Distinct().ToList();
            if (stepPolicy?.CleanupIntermediates == false)
                state.RetainedFiles = state.RetainedFiles.Concat(output.Select(a => a.Path)).Distinct(FileProcessingFiles.PathComparer).ToList();
            await Save();
        }

        // Empty or partially selected containers may remain in the result. Only now do they
        // need a private copy. The source is never handed to move/retirement code.
        if (borrowed != null)
            foreach (var retained in state.Active.Where(a => a.StepId == null))
                if (borrowed.TrySource(retained.RelativePath, out var source) && !File.Exists(retained.Path))
                {
                    FileProcessingFiles.Within(retained.Path, inputs);
                    var staging = Path.Combine(directory, "borrowed-retained.pending");
                    ResetOwnedDirectory(staging, directory);
                    var leaf = Path.Combine(staging, "file");
                    await Materialize(source, leaf, "auto", ct);
                    var actual = await ArtifactManifestFiles.CaptureAsync(staging, [leaf], ct);
                    if (!SameContent(state.FileContents[ContentKey(state, retained.Path)], actual.Entries.Single()))
                        throw new FileProcessingInputChangedException("A retained archive changed during processing.");
                    Directory.CreateDirectory(Path.GetDirectoryName(retained.Path)!);
                    File.Move(leaf, retained.Path);
                    Directory.Delete(staging);
                }

        // Publishing a boundary pins its files against later moves. Expose the exact committed
        // output without another complete copy when its layout contains no unrelated files.
        var reusable = stopAfterStepId != null && !isFinalBoundary &&
                       MaterializationMode(state, state.StepPolicies.GetValueOrDefault(stopAfterStepId)) == "auto"
            ? ReusableBoundaryDirectory(state, directory) : null;
        if (reusable != null)
        {
            var receipt = new BoundaryResult {Directory = reusable, Files = state.Active.ToList(),
                Manifest = state.LineageVersion == 1 ? ManifestFor(state, state.Active, reusable) : null};
            receipt.Directories = state.ActiveDirectories.ToList();
            state.BoundaryResults[stopAfterStepId!] = receipt;
            await Save();
            return CompletedResult(state, receipt.Files.Select(a => a.Path).ToArray(), receipt.Directory, receipt.Files);
        }
        if (!Directory.Exists(resultDirectory) &&
            await PrepareFinalAssemblyAsync(state, plan, directory, resultDirectory,
                isFinalBoundary ? null : stopAfterStepId, Save, ct))
        {
            await AssembleFinalAsync(state, directory, resultDirectory, Save, ct);
            return await CommitResultAsync(state, resultDirectory, stopAfterStepId, isFinalBoundary, Save, ct);
        }
        if (!Directory.Exists(resultDirectory))
        {
            var pending = resultDirectory + ".pending";
            ResetOwnedDirectory(pending, directory);
            foreach (var artifact in state.Active)
            {
                var target = FileProcessingFiles.Within(Path.Combine(pending, artifact.RelativePath), pending);
                if (File.Exists(target)) throw new InvalidOperationException("Two outputs have the same relative file name.");
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                state.StepPolicies.TryGetValue(artifact.StepId ?? "", out var outputPolicy);
                await Materialize(artifact.Path, target, MaterializationMode(state, outputPolicy), ct);
            }
            if (state.DirectoryVersion == 1)
            {
                VerifyActiveDirectories(state);
                ArtifactManifestFiles.CreateDirectories(pending, ManifestFor(state, state.Active, resultDirectory));
            }
            Directory.Move(pending, resultDirectory);
        }
        return await CommitResultAsync(state, resultDirectory, stopAfterStepId, isFinalBoundary, Save, ct);
    }

    public static Task<IReadOnlyList<string>> GetReclaimableFilesAsync(string? resultRoot, CancellationToken ct = default, bool overrideRetention = false) =>
        GetReclaimableFilesCoreAsync(resultRoot, ct, overrideRetention, verifyContents: true);

    internal static Task<IReadOnlyList<string>> GetReclaimableFilesForPreviewAsync(string? resultRoot, CancellationToken ct, bool overrideRetention) =>
        GetReclaimableFilesCoreAsync(resultRoot, ct, overrideRetention, verifyContents: false);

    private static async Task<IReadOnlyList<string>> GetReclaimableFilesCoreAsync(string? resultRoot, CancellationToken ct, bool overrideRetention, bool verifyContents)
    {
        if (resultRoot == null || Path.GetFileName(resultRoot) != "result") return [];
        var directory = Path.GetDirectoryName(resultRoot)!;
        var stateFile = Path.Combine(directory, "state.json");
        ArtifactManifestFiles.RejectLinks(stateFile);
        if (!File.Exists(stateFile)) return [];
        var state = JsonSerializer.Deserialize<State>(await File.ReadAllTextAsync(stateFile, ct), Json);
        if (state != null) state.ExecutionRoot = directory;
        if (state is not {Completed: true, LineageVersion: 1} || state.FinalFiles.Count == 0 || state.FinalManifest == null) return [];
        var boundaries = state.BoundaryResults.Values.SelectMany(b => b.Files).Select(a => a.Path).ToHashSet(FileProcessingFiles.PathComparer);
        var candidates = PrivateCleanupCandidates(state, directory, boundaries, overrideRetention, ct);
        var covered = new List<string>();
        var coverage = new ReplacementCoverage(state, ct);
        foreach (var path in candidates)
        {
            ct.ThrowIfCancellationRequested();
            if (coverage.IsComplete(path) && (verifyContents ? await ContentsStillMatch(state, path, ct)
                    : state.FileContents.TryGetValue(ContentKey(state, path), out var expected) &&
                      ArtifactCleanupPreviewFiles.MatchesLength(path, expected.Length))) covered.Add(path);
        }
        return covered;
    }

    private static IReadOnlyList<string> PrivateCleanupCandidates(State state, string directory,
        IReadOnlySet<string> protectedFiles, bool overrideRetention, CancellationToken ct)
    {
        var allowedRoots = new[] {"inputs", "outputs", "merged-inputs"}.Select(n => Path.Combine(directory, n)).ToArray();
        var recorded = state.Outputs.Values.SelectMany(files => files).Select(file => file.Path).Concat(state.StagedFiles)
            .Select(Path.GetFullPath).Distinct(FileProcessingFiles.PathComparer)
            .Where(path => allowedRoots.Any(root => IsInside(root, path)) && !protectedFiles.Contains(path) &&
                (overrideRetention || !state.RetainedFiles.Contains(path, FileProcessingFiles.PathComparer)) && File.Exists(path));
        return ArtifactCleanupAbsence.ConfirmReportedFiles(directory, recorded, ct);
    }

    /// <summary>Retires only recorded private snapshots after the complete immutable result exists.
    /// Intermediate result-step artifacts and journals remain available to other workflow consumers.</summary>
    public Task ReclaimCompletedIntermediatesAsync(string? resultRoot, ArtifactManifest expected,
        IReadOnlySet<string> approvedFiles, CancellationToken ct = default, bool overrideRetention = false) =>
        ReclaimCompletedIntermediatesAsync(resultRoot, expected, approvedFiles, ct, overrideRetention, null);

    internal async Task ReclaimCompletedIntermediatesAsync(string? resultRoot, ArtifactManifest expected,
        IReadOnlySet<string> approvedFiles, CancellationToken ct, bool overrideRetention, ArtifactCleanupFileSnapshot? snapshot,
        FileProcessingRelocatedResult? relocatedResult = null)
    {
        if (resultRoot == null || Path.GetFileName(resultRoot) != "result") return;
        var directory = Path.GetDirectoryName(resultRoot)!;
        var gate = Gates.GetOrAdd(directory, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            var stateFile = Path.Combine(directory, "state.json");
            ArtifactManifestFiles.RejectLinks(stateFile);
            if (!File.Exists(stateFile)) return;
            var state = JsonSerializer.Deserialize<State>(await File.ReadAllTextAsync(stateFile, ct), Json);
            if (state != null) state.ExecutionRoot = directory;
            if (state is not {Completed: true, LineageVersion: 1} || state.FinalFiles.Count == 0 || state.FinalManifest == null) return;
            if (!SameManifestContents(state.FinalManifest, expected)) return;
            // Re-capture while holding the executor gate. Missing/modified/unreadable outputs
            // cannot authorize deletion of the recovery snapshots.
            if (!ArtifactManifestFiles.ResolveFiles(resultRoot, expected).ToHashSet(FileProcessingFiles.PathComparer)
                    .SetEquals(state.FinalFiles)) return;
            var allowedRoots = new[] {"inputs", "outputs", "merged-inputs"}
                .Select(name => Path.Combine(directory, name)).ToArray();
            var boundaries = state.BoundaryResults.Values.SelectMany(b => b.Files).Select(a => a.Path).ToHashSet(FileProcessingFiles.PathComparer);
            // The maintenance fence audited registered references for this exact set. Never
            // expand deletion if a checkpoint changed after that audit.
            var coverage = new ReplacementCoverage(state, ct);
            var files = PrivateCleanupCandidates(state, directory, boundaries, overrideRetention, ct)
                .Where(coverage.IsComplete).ToArray();
            if (files.Any(path => !approvedFiles.Contains(path))) return;
            if (relocatedResult != null && !relocatedResult.Matches(resultRoot, expected)) return;
            if (snapshot == null && !(await ArtifactManifestFiles.ValidateAsync(relocatedResult?.VerificationRoot ?? resultRoot, expected, ct)).Available) return;
            foreach (var path in files)
            {
                ct.ThrowIfCancellationRequested();
                FileProcessingFiles.Within(path, directory);
                ArtifactManifestFiles.RejectLinks(path);
                if (File.Exists(path) && (snapshot == null ? await ContentsStillMatch(state, path, ct)
                        : state.FileContents.TryGetValue(ContentKey(state, path), out var recorded) &&
                          snapshot.Matches(path, recorded.Length, ct))) File.Delete(path);
            }
            var retainedDirectories = state.ActiveDirectories.Select(d => d.Path)
                .Concat(state.BoundaryResults.Values.Where(b => b.Manifest != null).SelectMany(b =>
                    ArtifactManifestFiles.ResolveDirectories(b.Directory, b.Manifest!))).ToHashSet(FileProcessingFiles.PathComparer);
            foreach (var root in allowedRoots) RemoveEmptyDirectories(root, ct, retainedDirectories);
            // Retain the historical file paths, passwords and step receipts; completed replay
            // reads FinalFiles and never reconstructs these snapshots.
            state.IntermediatesReclaimedAt = DateTime.UtcNow;
            var pending = stateFile + ".tmp";
            await File.WriteAllTextAsync(pending, JsonSerializer.Serialize(state, Json), ct);
            File.Move(pending, stateFile, true);
        }
        finally { gate.Release(); }
    }

    internal static bool IsInside(string root, string path) => Path.GetFullPath(path).StartsWith(
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar,
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    internal static void RemoveEmptyDirectories(string root, CancellationToken ct = default,
        IReadOnlySet<string>? retainedDirectories = null)
    {
        ct.ThrowIfCancellationRequested();
        if (!Directory.Exists(root)) return;
        ArtifactManifestFiles.RejectLinks(root);
        if (retainedDirectories?.Contains(root) == true) return;
        foreach (var child in Directory.EnumerateDirectories(root)) RemoveEmptyDirectories(child, ct, retainedDirectories);
        if (!Directory.EnumerateFileSystemEntries(root).Any()) Directory.Delete(root);
    }

    private static State NewState(FileProcessingPlan plan, ArtifactStoragePolicy? policy, string? title, bool extractionContentsAsRoot,
        ArtifactDirectoryProvenance? inputDirectoryProvenance)
    {
        var state = new State {StoragePolicy = policy, StorageTitle = title, MaterializationVersion = 2, LineageVersion = 1, DirectoryVersion = 1,
            ExtractionContentsAsRoot = extractionContentsAsRoot, GeneratedDirectoryVersion = 1,
            InputDirectoryProvenance = inputDirectoryProvenance};
        if (policy == null) return state; // Legacy callers keep the old placement and cleanup behavior.
        ArtifactStoragePolicies.Validate(policy);
        foreach (var step in plan.Steps)
        {
            var parents = step.GetInputReferences().Select(id => id == "download" ? policy : state.StepPolicies[id]).ToList();
            var inherited = parents[0] with {CleanupIntermediates = parents.All(p => p.CleanupIntermediates),
                Materialization = parents.Any(p => p.Materialization == "copy") ? "copy" : "auto"};
            state.StepPolicies[step.Id] = ArtifactStoragePolicies.Resolve(inherited, step.StoragePolicy);
        }
        return state;
    }

    private static string OutputDirectory(State state, string directory, string key, FileProcessingStep step, ArtifactStoragePolicy? policy)
    {
        // Operations that started before a preference change retain their committed destinations.
        if (state.Operations.TryGetValue(key, out var found) && found.OutputDirectory != null)
            return FileProcessingFiles.Within(found.OutputDirectory, Path.Combine(directory, "outputs"));
        var relative = policy == null ? key : ArtifactStoragePolicies.RenderProcessing(policy,
            state.StorageTitle, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key))).ToLowerInvariant(), step.Op, 1);
        return FileProcessingFiles.Within(Path.Combine(directory, "outputs", relative), Path.Combine(directory, "outputs"));
    }

    private static FileProcessingPlanResult CompletedResult(State state, IReadOnlyList<string> files, string root,
        IReadOnlyList<Artifact>? boundaryFiles = null)
    {
        var policies = new Dictionary<string, ArtifactStoragePolicy>(FileProcessingFiles.PathComparer);
        foreach (var artifact in boundaryFiles ?? state.Active)
        {
            var policy = artifact.StepId != null && state.StepPolicies.TryGetValue(artifact.StepId, out var step)
                ? step : state.StoragePolicy;
            if (policy != null) policies[FileProcessingFiles.Within(Path.Combine(root, artifact.RelativePath), root)] = policy;
        }
        var extracted = boundaryFiles == null ? state.FinalExtractedFiles : boundaryFiles.Where(a => a.Extracted).Select(a => a.Path).ToList();
        var savedBoundary = state.BoundaryResults.Values.FirstOrDefault(b => FileProcessingFiles.PathComparer.Equals(b.Directory, root));
        var manifest = savedBoundary?.Manifest
            ?? (Path.GetFileName(root) == "result" ? state.FinalManifest : null);
        var actualFiles = boundaryFiles ?? state.Active;
        if (manifest != null) ArtifactManifestFiles.VerifyDirectories(root, manifest);
        var lineage = state.LineageVersion == 1 && state.InputManifest != null && manifest != null && actualFiles.All(a => a.Origins != null)
            ? new FileLineage(state.InputManifest.Entries.Select(input =>
            {
                var matches = actualFiles.Where(a => a.Origins!.Any(o => o.InputRelativePath == input.RelativePath)).ToList();
                return new FileLineageEntry(input.RelativePath, matches.Select(a => a.RelativePath.Replace('\\', '/')).Distinct(StringComparer.Ordinal).ToArray(),
                    matches.Any(a => a.Origins!.Any(o => o.InputRelativePath == input.RelativePath && o.Replaced)));
            }).ToArray(), state.InputManifest, manifest) : null;
        return new(true, files, root, ExtractedFiles: extracted, StoragePolicy: state.StoragePolicy, FilePolicies: policies,
            ExtractionLayouts: actualFiles.Where(a => a.ExtractionLayout != null).ToDictionary(a => Path.Combine(root, a.RelativePath), a => a.ExtractionLayout!, FileProcessingFiles.PathComparer),
            StorageTitle: state.StorageTitle, FileLineage: lineage,
            Directories: manifest == null ? null : ArtifactManifestFiles.ResolveDirectories(root, manifest),
            DirectoryProvenance: state.GeneratedDirectoryVersion != 1 || manifest == null ? null :
                FileArtifactDeliveryLayout.Bind(manifest, actualFiles.Select(a =>
                        new ArtifactDirectoryEntry(a.RelativePath, false, a.GeneratedDirectories ?? []))
                    .Concat((savedBoundary?.Directories ?? (savedBoundary == null ? state.ActiveDirectories : []))
                        .Where(d => (manifest.Directories ?? []).Contains(d.RelativePath.Replace('\\', '/'), StringComparer.Ordinal))
                        .Select(d => new ArtifactDirectoryEntry(d.RelativePath, true, d.GeneratedDirectories ?? [])))))
            {RetainedGroupTokens = state.Operations.Values.Any(o => o.RetainedGroupToken != null)
                ? state.Operations.Values.Select(o => o.RetainedGroupToken).OfType<string>().ToArray() : null,
             RetainedArchiveFiles = actualFiles.Where(a => a.RetainedIncompleteArchive).Select(a => a.RelativePath.Replace('\\', '/')).ToArray()};
    }

    private static async Task VerifyOutputManifest(string root, ArtifactManifest expected, CancellationToken ct,
        IReadOnlyList<string>? archiveVolumes = null)
    {
        var actual = FileProcessingFiles.Enumerate(root);
        var recorded = expected.Entries.Select(e => FileProcessingFiles.Within(Path.Combine(root, e.RelativePath), root));
        var samePaths = actual.ToHashSet(FileProcessingFiles.PathComparer).SetEquals(recorded);
        if (!samePaths && archiveVolumes != null)
            samePaths = await ExtractedZipMemberPaths.TryRestoreAsync(root, actual, expected, archiveVolumes, ct);
        if (!samePaths ||
            expected.Entries.Count > 0 && !(await ArtifactManifestFiles.ValidateAsync(root, expected, ct)).Available)
            throw new InvalidDataException("An extraction output differs from its committed content receipt; it was retained.");
    }

    private static bool SameContent(ArtifactManifestEntry first, ArtifactManifestEntry second) =>
        first.Length == second.Length && string.Equals(first.Sha256, second.Sha256, StringComparison.OrdinalIgnoreCase);

    private static bool SameManifestContents(ArtifactManifest first, ArtifactManifest second) =>
        first.Fingerprint == ArtifactManifestFiles.Fingerprint(first.Entries, first.Directories) &&
        second.Fingerprint == ArtifactManifestFiles.Fingerprint(second.Entries, second.Directories) && first.Fingerprint == second.Fingerprint &&
        first.Entries.OrderBy(e => e.RelativePath, StringComparer.Ordinal)
            .SequenceEqual(second.Entries.OrderBy(e => e.RelativePath, StringComparer.Ordinal));

    private static async Task<ArtifactManifest> CaptureContents(State state, string root,
        IReadOnlyCollection<string> paths, CancellationToken ct, IReadOnlyCollection<string>? directories = null)
    {
        var manifest = await ArtifactManifestFiles.CaptureAsync(root, paths, ct, directories);
        foreach (var entry in manifest.Entries)
            RecordFileContent(state, FileProcessingFiles.Within(Path.Combine(root, entry.RelativePath), root), entry);
        return manifest;
    }

    private static void RecordFileContent(State state, string path, ArtifactManifestEntry entry)
    {
        var key = ContentKey(state, path);
        if (state.FileContents.TryGetValue(key, out var prior) && !SameContent(prior, entry))
            throw new InvalidDataException("A processing file changed after its execution receipt was recorded.");
        state.FileContents[key] = entry;
    }

    private static async Task VerifyContents(State state, IEnumerable<string> paths, CancellationToken ct)
    {
        foreach (var path in paths.Distinct(FileProcessingFiles.PathComparer))
        {
            FileProcessingFiles.Within(path, state.ExecutionRoot!);
            if (!await ContentsStillMatch(state, path, ct))
                throw new InvalidDataException("A processing input changed or has no verified execution receipt; it was retained.");
        }
    }

    private static async Task<bool> ContentsStillMatch(State state, string path, CancellationToken ct)
    {
        if (!state.FileContents.TryGetValue(ContentKey(state, path), out var expected)) return false;
        try
        {
            var actual = await ArtifactManifestFiles.CaptureAsync(Path.GetDirectoryName(path)!, [path], ct);
            return SameContent(expected, actual.Entries.Single());
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException)
        { return false; }
    }

    private static ArtifactManifest ManifestFor(State state, IReadOnlyList<Artifact> artifacts, string resultRoot)
    {
        var entries = artifacts.Select(a => state.FileContents.TryGetValue(ContentKey(state, a.Path), out var contents)
            ? contents with {RelativePath = a.RelativePath.Replace('\\', '/')}
            : throw new InvalidDataException("A processing output has no recorded content identity."))
            .OrderBy(e => e.RelativePath, StringComparer.Ordinal).ToArray();
        var directories = state.DirectoryVersion == 1 ? state.ActiveDirectories.Select(d => d.RelativePath.Replace('\\', '/'))
            .Distinct(FileProcessingFiles.PathComparer).OrderBy(path => path, StringComparer.Ordinal).ToArray() : null;
        return new(entries, ArtifactManifestFiles.Fingerprint(entries, directories), directories);
    }

    private static void VerifyActiveDirectories(State state)
    {
        foreach (var directory in state.ActiveDirectories)
        {
            ArtifactManifestFiles.RejectLinks(directory.Path);
            if (!File.GetAttributes(directory.Path).HasFlag(FileAttributes.Directory))
                throw new InvalidDataException("A recorded processing directory is unavailable or replaced.");
        }
    }

    internal static string StepKey(string stepId) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(stepId)))[..16];

    private static string ContentKey(State state, string path) =>
        ArtifactManifestFiles.RelativeFile(state.ExecutionRoot ?? throw new InvalidDataException("The processing checkpoint root is missing."), path);

    private static void AddTransition(State state, string source, IReadOnlyList<string> outputs)
    {
        if (outputs.Count == 0) return;
        var key = ContentKey(state, source);
        if (!state.FileTransitions.TryGetValue(key, out var recorded)) state.FileTransitions[key] = recorded = [];
        foreach (var output in outputs.Select(path => ContentKey(state, path)))
            if (!recorded.Contains(output, FileProcessingFiles.PathComparer)) recorded.Add(output);
    }

    internal static bool Matches(Artifact artifact, string? selector) => string.IsNullOrWhiteSpace(selector) ||
        FileSystemName.MatchesSimpleExpression(selector.Replace('\\', '/'),
            selector.Contains('/') || selector.Contains('\\') ? artifact.RelativePath.Replace('\\', '/') : Path.GetFileName(artifact.Path),
            ignoreCase: OperatingSystem.IsWindows());

    private static string TargetRelativePath(string path, FileProcessingStep step) => step.Op switch
    {
        "renameExtension" => ChangeExtension(path, step.Extension!),
        "renameFile" => Path.Combine(Path.GetDirectoryName(path) ?? "", step.TargetName!),
        "renameLiteral" => ReplaceLiteralName(path, step),
        "moveFile" => Path.Combine(step.TargetDirectory == "." ? "" :
            step.TargetDirectory!.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar),
            Path.GetFileName(path)),
        _ => throw new InvalidOperationException("Unsupported file operation.")
    };

    private static string ReplaceLiteralName(string path, FileProcessingStep step)
    {
        var name = Path.GetFileName(path).Replace(step.FindText!, step.ReplaceText!, StringComparison.Ordinal);
        if (!FileProcessingPlanValidator.IsPortableName(name))
            throw new InvalidOperationException("The literal replacement would produce an unsafe or empty file name.");
        return Path.Combine(Path.GetDirectoryName(path) ?? "", name);
    }

    private async Task<List<Artifact>> StageExtractionInputs(List<Artifact> selected,
        string stepId, string directory, string materialization, CancellationToken ct)
    {
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(stepId)))[..16];
        var destination = FileProcessingFiles.Within(Path.Combine(directory, "merged-inputs", key), directory);
        var staged = selected.Select(a => a with
        {
            Path = FileProcessingFiles.Within(Path.Combine(destination, a.RelativePath), destination)
        }).ToList();
        if (!Directory.Exists(destination))
        {
            var pending = destination + ".pending";
            ResetOwnedDirectory(pending, directory);
            foreach (var source in selected)
            {
                ct.ThrowIfCancellationRequested();
                FileProcessingFiles.Within(source.Path, directory);
                var target = FileProcessingFiles.Within(Path.Combine(pending, source.RelativePath), pending);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                await Materialize(source.Path, target, materialization, ct);
            }
            Directory.Move(pending, destination);
        }
        // A resumed snapshot must still contain exactly the selected files. Never adopt an
        // unexpected companion volume that was placed beside them after a password wait.
        var actual = FileProcessingFiles.Enumerate(destination);
        if (!actual.ToHashSet(FileProcessingFiles.PathComparer).SetEquals(staged.Select(a => a.Path)))
            throw new InvalidOperationException("The merged input snapshot no longer matches its declared files.");
        return staged;
    }

    private static string ChangeExtension(string path, string extension)
    {
        var volume = Regex.Match(path, @"\.\d{3,}$");
        var basis = volume.Success ? path[..volume.Index] : path;
        return Path.ChangeExtension(basis, extension.TrimStart('.')) + (volume.Success ? volume.Value : "");
    }

    private static void ResetOwnedDirectory(string path, string root)
    {
        FileProcessingFiles.Within(path, root);
        if (Directory.Exists(path)) Directory.Delete(path, true);
        Directory.CreateDirectory(path);
    }

    private async Task Materialize(string source, string target, string mode, CancellationToken ct) =>
        await materializer.MaterializeAsync(source, target, mode, ct);

    private static bool CanMovePrivateInput(FileProcessingPlan plan, FileProcessingStep step, State state,
        string directory, string source, ArtifactStoragePolicy? policy)
    {
        if (state.MaterializationVersion < 2 || MaterializationMode(state, policy) != "auto" ||
            state.StoragePolicy?.CleanupIntermediates == false || policy?.CleanupIntermediates == false ||
            state.RetainedFiles.Contains(source, FileProcessingFiles.PathComparer) ||
            state.BoundaryResults.Values.SelectMany(b => b.Files).Any(a => FileProcessingFiles.PathComparer.Equals(a.Path, source)) ||
            !new[] {"inputs", "outputs"}.Any(name => IsInside(Path.Combine(directory, name), source))) return false;
        var owners = state.Outputs.Where(pair => pair.Value.Any(a => FileProcessingFiles.PathComparer.Equals(a.Path, source)))
            .Select(pair => pair.Key).ToHashSet(StringComparer.Ordinal);
        // Check the entire plan, including steps beyond the requested boundary. A selector
        // may change which archive companions are needed, so references are conservative.
        return owners.Count > 0 && !plan.Steps.Any(other => other.Id != step.Id &&
            other.GetInputReferences().Any(owners.Contains));
    }

    private async Task ExecuteRenameJournal(Operation operation, List<(string Source, string Relative)> projected,
        string directory, string mode, bool recovering, CancellationToken ct)
    {
        var destination = FileProcessingFiles.Within(operation.OutputDirectory!, Path.Combine(directory, "outputs"));
        var pending = destination + ".pending";
        var entries = operation.RenameFiles!;
        if (entries.Count != projected.Count || entries.Where((entry, i) =>
                !FileProcessingFiles.PathComparer.Equals(entry.Source, projected[i].Source) ||
                !FileProcessingFiles.PathComparer.Equals(entry.RelativePath, projected[i].Relative)).Any())
            throw new InvalidDataException("The rename journal no longer matches its inputs.");
        foreach (var entry in entries)
        {
            FileProcessingFiles.Within(entry.Source, directory);
            ArtifactManifestFiles.RejectLinks(entry.Source);
        }

        async Task Verify(string path, RenameFile entry)
        {
            var actual = await ArtifactManifestFiles.CaptureAsync(directory, new[] {path}, ct);
            if (actual.Entries[0].Length != entry.Content.Length || actual.Entries[0].Sha256 != entry.Content.Sha256)
                throw new InvalidDataException("A journaled rename file changed. Restore it before resuming.");
        }

        void CheckContents(string root, bool complete)
        {
            var expected = entries.Select(e => FileProcessingFiles.Within(Path.Combine(root, e.RelativePath), root))
                .ToHashSet(FileProcessingFiles.PathComparer);
            var actual = FileProcessingFiles.Enumerate(root).ToHashSet(FileProcessingFiles.PathComparer);
            if (complete ? !actual.SetEquals(expected) : !actual.IsSubsetOf(expected))
                throw new InvalidDataException("The journaled rename directory contains unexpected or missing files.");
            CheckProjectedDirectories(root, operation, complete);
        }

        if (Directory.Exists(destination))
        {
            CheckContents(destination, complete: true);
            foreach (var entry in entries)
                await Verify(FileProcessingFiles.Within(Path.Combine(destination, entry.RelativePath), destination), entry);
            return;
        }
        if (operation.Completed) throw new InvalidDataException("A completed rename directory is missing.");
        FileProcessingFiles.Within(pending, directory);
        ArtifactManifestFiles.RejectLinks(pending);
        Directory.CreateDirectory(pending);
        CheckContents(pending, complete: false);
        if (recovering)
        {
            // Validate every survivor before moving anything else. A crash can leave some
            // sources at their old names and others in pending; neither is disposable.
            foreach (var entry in entries)
            {
                var target = FileProcessingFiles.Within(Path.Combine(pending, entry.RelativePath), pending);
                if (File.Exists(target)) await Verify(target, entry);
                else await Verify(entry.Source, entry);
            }
        }
        CreateProjectedDirectories(pending, operation);
        for (var index = 0; index < entries.Count; index++)
        {
            ct.ThrowIfCancellationRequested();
            var entry = entries[index];
            var target = FileProcessingFiles.Within(Path.Combine(pending, entry.RelativePath), pending);
            if (File.Exists(target))
            {
                if (!recovering) await Verify(target, entry);
                continue;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            if (entry.Move && await materializer.TryMoveAsync(entry.Source, target, ct))
            {
                await Verify(target, entry);
                continue;
            }
            // Unsupported/cross-volume renames leave their source intact. Copy into a
            // separate disposable location so a cancelled fallback is never adopted.
            var copying = destination + ".copying";
            ResetOwnedDirectory(copying, directory);
            var staged = Path.Combine(copying, "file");
            await Materialize(entry.Source, staged, mode, ct);
            await Verify(staged, entry);
            File.Move(staged, target);
            Directory.Delete(copying);
        }
        CheckContents(pending, complete: true);
        Directory.Move(pending, destination);
    }

    private static string MaterializationMode(State state, ArtifactStoragePolicy? policy) =>
        state.MaterializationVersion == 0 ? "copy" : policy?.Materialization ?? state.StoragePolicy?.Materialization ?? "auto";

    private static string? ReusableBoundaryDirectory(State state, string directory)
    {
        if (state.Active.Count == 0) return null;
        foreach (var operation in state.Operations.Values.Where(o => o.Completed && o.OutputDirectory != null))
        {
            var candidate = FileProcessingFiles.Within(operation.OutputDirectory!, Path.Combine(directory, "outputs"));
            if (state.Active.Any(a => !IsInside(candidate, a.Path) ||
                    !FileProcessingFiles.PathComparer.Equals(Path.GetRelativePath(candidate, a.Path), a.RelativePath))) continue;
            if (state.ActiveDirectories.Any(a => !IsInside(candidate, a.Path) ||
                    !FileProcessingFiles.PathComparer.Equals(Path.GetRelativePath(candidate, a.Path).Replace('\\', '/'), a.RelativePath.Replace('\\', '/')))) continue;
            VerifyActiveDirectories(state);
            if (FileProcessingFiles.Enumerate(candidate).ToHashSet(FileProcessingFiles.PathComparer)
                .SetEquals(state.Active.Select(a => a.Path))) return candidate;
        }
        return null;
    }

    public sealed record FileOrigin(string InputRelativePath, bool Replaced = false);
    public sealed record Artifact(string Path, string RelativePath, bool Extracted = false, string? StepId = null,
        IReadOnlyList<FileOrigin>? Origins = null, IReadOnlyList<string>? GeneratedDirectories = null, string? ExtractionLayout = null)
    {
        [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
        public bool RetainedIncompleteArchive { get; init; }
    }
    public sealed record DirectoryArtifact(string Path, string RelativePath, IReadOnlyList<string>? GeneratedDirectories = null);
    public sealed record RenameFile(string Source, string RelativePath, ArtifactManifestEntry Content, bool Move);
    private async Task<ArchivePasswordProbe?> ProbeEvidenceAsync(string archive, IReadOnlyList<string?> attempted,
        ArchiveOpenOptions? opening, Func<string, CancellationToken, Task<IReadOnlyList<string>>> readEvidence, CancellationToken ct)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromMinutes(2));
        try
        {
            var evidence = await readEvidence(Path.GetFileName(archive), deadline.Token);
            if (evidence.Count is 0 or > 3) return null;
            var candidates = evidence.Where(v => !string.IsNullOrWhiteSpace(v) && v.Length <= 128 &&
                    !v.Any(char.IsControl) && !attempted.Contains(v, StringComparer.Ordinal))
                .Distinct(StringComparer.Ordinal).ToArray();
            if (candidates.Length == 0) return null;
            var probe = await extraction.ProbePasswordAsync(archive, candidates, null, deadline.Token, opening);
            return probe.Succeeded && probe.Password != null && candidates.Contains(probe.Password, StringComparer.Ordinal) ? probe : null;
        }
        catch (Exception ex) when (!ct.IsCancellationRequested && ex is not OutOfMemoryException)
        {
            // Evidence/model availability and the bounded probe are optional. Keep the ordinary
            // password wait and original file when a candidate cannot be fully verified.
            return null;
        }
    }

    public sealed record FormatPasswordEvidence(string Input, string? VerifiedPassword);
    public sealed class Operation
    {
        public bool SourcePasswordEvidenceChecked { get; set; }
        public string? OutputDirectory { get; set; }
        public bool Completed { get; set; }
        public ArtifactManifest? OutputManifest { get; set; }
        public Guid? ExtractionStagingId { get; set; }
        public List<RenameFile>? RenameFiles { get; set; }
        public List<string>? ProjectedDirectories { get; set; }
        // Entry first, then companion volumes. Paths bind group-index receipts and password
        // answers to the exact inputs; FileContents independently binds their verified bytes.
        public List<string>? ArchiveInputs { get; set; }
        [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
        public long? ExpandedBytes { get; set; }
        [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
        public ExtractionStagingIdentity? UnverifiedExtractionOwner { get; set; }
        [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
        public string? RetainedGroupToken { get; set; }
    }
    public sealed class State
    {
        public string? SnapshotSourceArtifactId { get; set; }
        public string? ReadOnlyVolumeArtifactId { get; set; }
        public IReadOnlyList<string>? ReadOnlyVolumeMembers { get; set; }
        public bool ExtractionContentsAsRoot { get; set; }
        // Missing on old persistent states: preserve their existing copy/boundary behavior.
        public int MaterializationVersion { get; set; }
        public int LineageVersion { get; set; }
        public int DirectoryVersion { get; set; }
        public int GeneratedDirectoryVersion { get; set; }
        public ArtifactDirectoryProvenance? InputDirectoryProvenance { get; set; }
        public List<DirectoryArtifact> ActiveDirectories { get; set; } = [];
        [System.Text.Json.Serialization.JsonIgnore]
        public string? ExecutionRoot { get; set; }
        public ArtifactManifest? InputManifest { get; set; }
        public ArtifactManifest? FinalManifest { get; set; }
        public Dictionary<string, ArtifactManifestEntry> FileContents { get; set; } = new(FileProcessingFiles.PathComparer);
        public Dictionary<string, List<string>> FileTransitions { get; set; } = new(FileProcessingFiles.PathComparer);
        public Dictionary<string, BoundaryResult> BoundaryResults { get; set; } = [];
        public ArtifactStoragePolicy? StoragePolicy { get; set; }
        public string? StorageTitle { get; set; }
        public Dictionary<string, ArtifactStoragePolicy> StepPolicies { get; set; } = [];
        public List<string> RetainedFiles { get; set; } = [];
        public Dictionary<string, List<Artifact>> Outputs { get; set; } = [];
        public Dictionary<string, List<DirectoryArtifact>> DirectoryOutputs { get; set; } = [];
        public List<Artifact> Active { get; set; } = [];
        public Dictionary<string, Operation> Operations { get; set; } = [];
        public Dictionary<string, string> Passwords { get; set; } = [];
        public Dictionary<string, FormatPasswordEvidence> SourceFormatEvidence { get; set; } = [];
        public string? PendingOperation { get; set; }
        public bool Completed { get; set; }
        public DateTime? IntermediatesReclaimedAt { get; set; }
        public List<string> StagedFiles { get; set; } = [];
        public List<string> FinalFiles { get; set; } = [];
        public List<string> FinalExtractedFiles { get; set; } = [];
        public FinalAssemblyJournal? FinalAssembly { get; set; }
    }
    public sealed class BoundaryResult
    {
        public string Directory { get; set; } = "";
        public ArtifactManifest? Manifest { get; set; }
        public List<Artifact> Files { get; set; } = [];
        public List<DirectoryArtifact>? Directories { get; set; }
    }
}
