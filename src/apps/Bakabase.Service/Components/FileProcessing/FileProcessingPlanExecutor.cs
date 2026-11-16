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
    string? RequiredComponentId = null, FileProcessingPathConflict? PathConflict = null);

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

    public static void Validate(FileProcessingPlan plan) => FileProcessingPlanValidator.Validate(plan);

    public async Task<FileProcessingPlanResult> ExecuteAsync(FileProcessingPlan plan, string root,
        IReadOnlyList<string> files, string stateDirectory, string? password = null,
        Action<int>? progress = null, CancellationToken ct = default, string? stopAfterStepId = null,
        Func<string, string>? identityPath = null, ArtifactStoragePolicy? storagePolicy = null, string? storageTitle = null,
        bool extractionContentsAsRoot = false, IReadOnlyList<string>? inputDirectories = null,
        FileProcessingCheckpointInput? checkpointInput = null, ArtifactDirectoryProvenance? inputDirectoryProvenance = null,
        Func<string, CancellationToken, Task<IReadOnlyList<string>>>? passwordEvidence = null,
        Func<string, Task>? reportStage = null, string? originalDownloadPassword = null)
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
            return await Run(plan, root, normalized, directory, password, progress, ct, stopAfterStepId, storagePolicy, storageTitle, extractionContentsAsRoot, directories, inputDirectoryProvenance, passwordEvidence, reportStage, originalDownloadPassword);
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
        string? originalDownloadPassword)
    {
        FileProcessingFiles.Within(directory, directory);
        var stateFile = Path.Combine(directory, "state.json");
        var state = File.Exists(stateFile)
            ? JsonSerializer.Deserialize<State>(await File.ReadAllTextAsync(stateFile, ct), Json)!
            : NewState(plan, storagePolicy, storageTitle, extractionContentsAsRoot, inputDirectoryProvenance);
        if (state.ExtractionContentsAsRoot != extractionContentsAsRoot)
            throw new InvalidOperationException("The processing checkpoint belongs to a different extraction layout.");
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
        var boundaryIndex = stopAfterStepId == null ? plan.Steps.Count - 1 : plan.Steps.FindIndex(s => s.Id == stopAfterStepId);
        var isFinalBoundary = boundaryIndex == plan.Steps.Count - 1;
        var resultDirectory = stopAfterStepId == null || isFinalBoundary ? Path.Combine(directory, "result") :
            Path.Combine(directory, "result-step-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(stopAfterStepId)))[..16]);
        if (state.FinalAssembly != null && !state.Completed)
        {
            if (!isFinalBoundary)
                throw new InvalidDataException("The final result assembly must be resumed before replaying an earlier boundary.");
            await AssembleFinalAsync(state, directory, resultDirectory, Save, ct);
            return await CommitResultAsync(state, resultDirectory, stopAfterStepId, true, Save, ct);
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
        if (!Directory.Exists(inputs) && writeGuard != null)
        {
            // An input snapshot is committed as one unit. Admit its complete known payload
            // before hashing or copying any member, instead of filling a disposable partial
            // snapshot up to the reserve. Auto materialization may clone, but its documented
            // fallback is a full copy, so a new snapshot uses that conservative upper bound.
            // Existing snapshots and completed boundaries need no second payload allocation.
            long snapshotBytes = 0;
            foreach (var file in files)
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
            state.InputManifest = await ArtifactManifestFiles.CaptureAsync(root, files, ct,
                state.DirectoryVersion == 1 ? inputDirectories : null);
            await Save();
        }

        // Commit the entire input snapshot together; a crash leaves only our disposable staging directory.
        if (!Directory.Exists(inputs))
        {
            var pending = inputs + ".pending";
            ResetOwnedDirectory(pending, directory);
            foreach (var file in files)
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
            state.Outputs["download"] = FileProcessingFiles.Enumerate(inputs)
                .Select(f => new Artifact(f, Path.GetRelativePath(inputs, f), Origins: state.LineageVersion == 1
                    ? [new FileOrigin(Path.GetRelativePath(inputs, f).Replace('\\', '/'))] : null,
                    GeneratedDirectories: provenance.GetValueOrDefault((Path.GetRelativePath(inputs, f).Replace('\\', '/'), false)))).ToList();
            if (state.LineageVersion == 1)
            {
                var captured = await CaptureContents(state, inputs, state.Outputs["download"].Select(a => a.Path).ToArray(), ct,
                    state.DirectoryVersion == 1 ? ArtifactManifestFiles.ResolveDirectories(inputs, state.InputManifest!) : null);
                if (!SameManifestContents(captured, state.InputManifest!))
                    throw new InvalidDataException("The processing snapshot differs from the recorded input files.");
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
                await VerifyContents(state, supplied.Select(a => a.Path).Where(p => !journalInputs.Contains(p)), ct);
            }
            if (selected.Count == 0 && selectedDirectories.Count == 0)
                return new(false, state.Active.Select(x => x.Path).ToList(), null,
                    NoInputMatchesMessage, step.Id, FailureCode: "ProcessingInputNotMatched");
            if (stepPolicy?.CleanupIntermediates == false)
                state.RetainedFiles = state.RetainedFiles.Concat(supplied.Select(a => a.Path)).Distinct(FileProcessingFiles.PathComparer).ToList();
            var output = new List<Artifact>();
            var outputProjection = new Dictionary<string, FileProcessingPathProjectionEntry>(FileProcessingFiles.PathComparer);
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
                    ? await StageExtractionInputs(selected, step.Id, directory, MaterializationMode(state, stepPolicy), ct) : selected;
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
                var stepKey = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(step.Id)))[..16];
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
                    exactExtractionSelection: selected.Count == 1 || literalSelector || executableSelector || oneSelectedSfxFamily, authorizedPasswords: formatPasswords);
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
                FileProcessingFiles.ValidateVolumes(extractionInputs.Select(x => x.Path));
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
                    var extractionPending = destination + ".pending";
                    if (state.LineageVersion == 1 && !Directory.Exists(destination) &&
                        operation.OutputManifest != null && Directory.Exists(extractionPending))
                    {
                        await VerifyOutputManifest(extractionPending, operation.OutputManifest, ct, group.Files);
                        Directory.Move(extractionPending, destination);
                    }
                    if (!operation.Completed && !Directory.Exists(destination))
                    {
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
                        ArchivePasswordProbe probe;
                        try
                        {
                            AuthorizedArchiveDetection.ValidateCurrentSfxFamilies(group.Files, ct);
                            probe = await extraction.ProbePasswordAsync(group.Files[0], candidates, null, ct, opening);
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
                            var verified = await ProbeEvidenceAsync(group.Files[0], candidates, opening, passwordEvidence, ct);
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
                            if (probe.FailureCode is "MissingVolumes" or "ArchiveInputRequired")
                                return new(false, state.Active.Select(x => x.Path).ToList(), null,
                                    probe.FailureCode == "MissingVolumes"
                                        ? "The archive is incomplete or a required volume could not be found. Check all volume names and parts; this does not establish a wrong password."
                                        : "The archive tool could not open the selected file. Check the actual format and multipart filenames before retrying; this does not establish a wrong password.",
                                    step.Id, FailureCode: probe.FailureCode);
                            return new(false, state.Active.Select(x => x.Path).ToList(), null,
                                $"Could not open {Path.GetFileName(group.Files[0])}. Check the password and all archive parts.",
                                step.Id, true, candidates.Where(x => x != null).Cast<string>().ToList());
                        }
                        var pending = extractionPending;
                        ResetOwnedDirectory(pending, directory);
                        var request = new ArchiveExtractionRequest(group.Files, pending, probe.Password,
                            DecompressToNewFolder: false, OpenOptions: opening);
                        ArchiveExtractionResult result;
                        try
                        {
                            async Task<ArchiveExtractionResult> Extract(CancellationToken token)
                            {
                                AuthorizedArchiveDetection.ValidateCurrentSfxFamilies(group.Files, token);
                                if (reportStage != null) await reportStage("extractingArchive");
                                var extracted = await extraction.ExtractAsync(request, progress, token);
                                if (reportStage != null) await reportStage("processingFiles");
                                return extracted;
                            }
                            result = writeGuard == null
                                ? await Extract(ct)
                                : await writeGuard.RunAsync([pending], StorageWriteScope.Current,
                                    Extract, ct);
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
                        if (state.LineageVersion == 1)
                        {
                            await VerifyContents(state, group.Files, ct);
                            operation.OutputManifest = extractedPaths.Count == 0
                                ? new([], ArtifactManifestFiles.Fingerprint([]))
                                : await ArtifactManifestFiles.CaptureAsync(pending, extractedPaths, ct,
                                    state.DirectoryVersion == 1 ? ArtifactManifestFiles.CaptureEmptyDirectories(pending, ct) : null);
                            await Save();
                        }
                        Directory.Move(pending, destination);
                    }
                    if (state.LineageVersion == 1)
                    {
                        await VerifyContents(state, group.Files, ct);
                        if (operation.OutputManifest == null)
                            throw new InvalidDataException("The extraction output has no committed content receipt; its files were retained.");
                        await VerifyOutputManifest(destination, operation.OutputManifest, ct, operation.Completed ? null : group.Files);
                    }
                    var entry = extractionInputs.First(a => FileProcessingFiles.PathComparer.Equals(a.Path, group.Files[0]));
                    var wrapperName = CompressedFileHelper.GetExtractionDirectoryName(group.Files, group.Extension);
                    var parent = Path.GetDirectoryName(entry.RelativePath) ?? "";
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
                        await CaptureContents(state, destination, extracted.Select(a => a.Path).ToArray(), ct);
                        if (partialContainer)
                            state.RetainedFiles = state.RetainedFiles.Concat(group.Files)
                                .Concat(group.Files.Select(f => originalPaths[f])).Distinct(FileProcessingFiles.PathComparer).ToList();
                        else
                            foreach (var file in group.Files)
                                AddTransition(state, originalPaths[file], extracted.Select(a => a.Path).ToArray());
                    }
                    operation.Completed = true;
                    state.PendingOperation = null;
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
        if (isFinalBoundary && !Directory.Exists(resultDirectory) &&
            await PrepareFinalAssemblyAsync(state, directory, resultDirectory, Save, ct))
        {
            await AssembleFinalAsync(state, directory, resultDirectory, Save, ct);
            return await CommitResultAsync(state, resultDirectory, stopAfterStepId, true, Save, ct);
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
        var allowedRoots = new[] {"inputs", "outputs", "merged-inputs"}.Select(n => Path.Combine(directory, n)).ToArray();
        var candidates = state.Outputs.Values.SelectMany(v => v).Select(v => v.Path).Concat(state.StagedFiles)
            .Select(Path.GetFullPath).Distinct(FileProcessingFiles.PathComparer)
            .Where(p => allowedRoots.Any(r => IsInside(r, p)) && File.Exists(p) &&
                (overrideRetention || !state.RetainedFiles.Contains(p, FileProcessingFiles.PathComparer)) && !boundaries.Contains(p)).ToArray();
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
            var files = state.Outputs.Values.SelectMany(value => value).Select(value => value.Path)
                .Concat(state.StagedFiles).Select(Path.GetFullPath).Distinct(FileProcessingFiles.PathComparer)
                .Where(path => allowedRoots.Any(root => IsInside(root, path)) && File.Exists(path) &&
                    (overrideRetention || !state.RetainedFiles.Contains(path, FileProcessingFiles.PathComparer)) && !boundaries.Contains(path)).ToArray();
            // The maintenance fence audited registered references for this exact set. Never
            // expand deletion if a checkpoint changed after that audit.
            var coverage = new ReplacementCoverage(state, ct);
            files = files.Where(coverage.IsComplete).ToArray();
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
                        .Select(d => new ArtifactDirectoryEntry(d.RelativePath, true, d.GeneratedDirectories ?? [])))));
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
        {
            var path = FileProcessingFiles.Within(Path.Combine(root, entry.RelativePath), root);
            if (state.FileContents.TryGetValue(ContentKey(state, path), out var prior) && !SameContent(prior, entry))
                throw new InvalidDataException("A processing file changed after its execution receipt was recorded.");
            state.FileContents[ContentKey(state, path)] = entry;
        }
        return manifest;
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

    private static bool Matches(Artifact artifact, string? selector) => string.IsNullOrWhiteSpace(selector) ||
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
        IReadOnlyList<FileOrigin>? Origins = null, IReadOnlyList<string>? GeneratedDirectories = null, string? ExtractionLayout = null);
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
        public List<RenameFile>? RenameFiles { get; set; }
        public List<string>? ProjectedDirectories { get; set; }
        // Entry first, then companion volumes. Paths bind group-index receipts and password
        // answers to the exact inputs; FileContents independently binds their verified bytes.
        public List<string>? ArchiveInputs { get; set; }
    }
    public sealed class State
    {
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
