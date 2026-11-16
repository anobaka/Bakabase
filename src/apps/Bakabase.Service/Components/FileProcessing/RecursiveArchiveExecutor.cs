using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.Compression;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.Modules.Workflow.Artifacts;

namespace Bakabase.Service.Components.FileProcessing;

public sealed record RecursiveArchiveOptions
{
    public bool Enabled { get; init; }
    public int MaxDepth { get; init; } = 8;
    public int MaxArchiveGroups { get; init; } = 1000;
    public int MaxFiles { get; init; } = 10000;
    public long MaxExpandedBytes { get; init; } = 20L * 1024 * 1024 * 1024;
    public long MinimumFreeBytes { get; init; } = 1024L * 1024 * 1024;
}

public sealed record RecursiveArchivePasswordAnswer(string ArchiveId, string Password);
public sealed record RecursiveArchiveLineage(string ArchiveId, string? ParentArchiveId, int Depth,
    IReadOnlyList<string> InputFiles, string InputFingerprint, string OutputDirectory, IReadOnlyList<string> OutputFiles);
public sealed record RecursiveArchiveResult(bool Completed, IReadOnlyList<string> Files, string? OutputDirectory,
    string? WaitReason = null, string? ArchiveId = null, string? Message = null,
    IReadOnlyList<string>? TriedPasswords = null, IReadOnlyList<RecursiveArchiveLineage>? Lineage = null,
    IReadOnlyList<string>? SourceArchiveFiles = null, FileLineage? FileLineage = null,
    IReadOnlyList<string>? Directories = null, ArtifactDirectoryProvenance? DirectoryProvenance = null,
    FileProcessingPathConflict? PathConflict = null);

/// <summary>Durable, exact-file recursion over newly produced archives. The caller owns the state directory.</summary>
public sealed partial class RecursiveArchiveExecutor(IArchiveExtractionService extraction, IFileArtifactMaterializer? materializer = null,
    IStorageWriteGuard? writeGuard = null)
{
    private readonly IFileArtifactMaterializer materializer = materializer ?? (writeGuard == null ? new FileArtifactMaterializer() : new FileArtifactMaterializer(writeGuard));
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Gates = new(
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

    public async Task<RecursiveArchiveResult> ExecuteAsync(string root, IReadOnlyList<string> files,
        string stateDirectory, RecursiveArchiveOptions options, RecursiveArchivePasswordAnswer? answer = null,
        string? defaultPassword = null, Action<int>? progress = null, CancellationToken ct = default,
        IReadOnlyList<string>? archiveCandidates = null, bool onlyInitialLayer = false,
        Func<string, string>? identityPath = null, ArtifactStoragePolicy? storagePolicy = null, string? storageTitle = null,
        IReadOnlyDictionary<string, ArtifactStoragePolicy>? inputPolicies = null, bool onlyOuterPackaging = false,
        IReadOnlyList<string>? inputDirectories = null, ArtifactDirectoryProvenance? inputDirectoryProvenance = null,
        string? extractionLayout = null, IReadOnlyDictionary<string, string>? inputLayouts = null,
        Func<CancellationToken, Task<IReadOnlyList<string>>>? passwordEvidence = null)
    {
        ValidateOptions(options);
        if (!FileExtractionLayouts.Valid(extractionLayout) || inputLayouts?.Values.Any(v => !FileExtractionLayouts.Valid(v)) == true)
            throw new ArgumentException("Invalid recursive extraction layout.");
        if (defaultPassword?.Length > 2048 || answer?.Password?.Length > 2048)
            throw new ArgumentException("The archive password is too long.");
        root = Path.GetFullPath(root);
        stateDirectory = Path.GetFullPath(stateDirectory);
        FileProcessingFiles.Within(root, root);
        var inputPaths = files.Select(f => FileProcessingFiles.Within(f, root)).Distinct(FileProcessingFiles.PathComparer)
            .OrderBy(x => x, FileProcessingFiles.PathComparer).ToList();
        var candidates = archiveCandidates?.Select(f => FileProcessingFiles.Within(f, root))
            .ToHashSet(FileProcessingFiles.PathComparer);
        if (candidates?.Any(f => !inputPaths.Contains(f, FileProcessingFiles.PathComparer)) == true)
            throw new ArgumentException("Archive candidates must belong to the exact input manifest.");
        if (inputPaths.Count == 0) throw new ArgumentException("Provide an exact nonempty file manifest.");
        var gate = Gates.GetOrAdd(stateDirectory, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            CreatePrivate(stateDirectory);
            FileProcessingFiles.Within(stateDirectory, stateDirectory);
            var file = Path.Combine(stateDirectory, "recursive-state.json");
            var state = File.Exists(file) ? JsonSerializer.Deserialize<State>(await File.ReadAllTextAsync(file, ct), Json)
                ?? throw new InvalidDataException("The recursive archive state is invalid.") : null;
            if (state != null && state.OnlyOuterPackaging != onlyOuterPackaging)
                throw new InvalidOperationException("The recursive archive operation belongs to a different outer-packaging policy.");
            if (state == null)
            {
                state = new State {StoragePolicy = storagePolicy, StorageTitle = storageTitle, MaterializationVersion = 1, LineageVersion = 1,
                    OnlyOuterPackaging = onlyOuterPackaging, GeneratedDirectoryVersion = 1};
                foreach (var path in inputPaths)
                {
                    var fact = await Capture(path, root, false, Path.GetRelativePath(root, path), ct);
                    state.Inputs.Add(fact with {ExtractionLayout = inputLayouts?.GetValueOrDefault(path) ?? extractionLayout,
                        StoragePolicy = inputPolicies?.GetValueOrDefault(path) ?? storagePolicy,
                        Origins = [new FileProcessingPlanExecutor.FileOrigin(fact.Path.Replace('\\', '/'))]});
                }
                state.Active = state.Inputs.ToList();
                state.InputDirectories = (inputDirectories ?? []).Select(path => new DirectoryFact(
                    Path.GetRelativePath(root, FileProcessingFiles.Within(path, root)), false,
                    Path.GetRelativePath(root, path))).ToList();
                VerifyDirectories(state.InputDirectories, root, stateDirectory);
                var inputManifest = Manifest(state.Inputs, f => f.LogicalPath, state.InputDirectories.Select(d => d.LogicalPath).ToArray());
                var provenance = FileArtifactDeliveryLayout.Index(inputDirectoryProvenance, inputManifest);
                state.Inputs = state.Inputs.Select(f => f with {GeneratedDirectories =
                    provenance.GetValueOrDefault((f.LogicalPath.Replace('\\', '/'), false))}).ToList();
                state.Active = state.Inputs.ToList();
                state.InputDirectories = state.InputDirectories.Select(d => d with {GeneratedDirectories =
                    provenance.GetValueOrDefault((d.LogicalPath.Replace('\\', '/'), true))}).ToList();
                state.ActiveDirectories = state.InputDirectories.ToList();
                // Outer-packaging admission always examines the entire layer. A candidate
                // subset cannot hide a key, installer, or other unknown sibling from it.
                await Discover(state, onlyOuterPackaging ? state.Inputs : state.Inputs.Where(f => candidates == null || candidates.Contains(Resolve(f, root, stateDirectory))).ToList(),
                    null, 0, root, stateDirectory, defaultPassword, ct);
                await Save(file, state, ct);
            }
            if (!state.Inputs.Select(f => Resolve(f, root, stateDirectory))
                    .SequenceEqual(inputPaths, FileProcessingFiles.PathComparer))
                throw new InvalidOperationException("The recursive archive operation belongs to different inputs.");
            if (state.Inputs.Any(f => f.ExtractionLayout != (inputLayouts?.GetValueOrDefault(Resolve(f, root, stateDirectory)) ?? extractionLayout)))
                throw new InvalidOperationException("The recursive archive operation belongs to a different extraction layout.");
            if (!state.InputDirectories.Select(d => ResolveDirectory(d, root, stateDirectory))
                    .OrderBy(path => path, FileProcessingFiles.PathComparer)
                    .SequenceEqual((inputDirectories ?? []).Select(path => FileProcessingFiles.Within(path, root))
                        .OrderBy(path => path, FileProcessingFiles.PathComparer), FileProcessingFiles.PathComparer))
                throw new InvalidOperationException("The recursive archive operation belongs to different input directories.");
            if (state.Completed)
            {
                await Verify(state.FinalFiles, root, stateDirectory, ct);
                VerifyDirectories(state.FinalDirectories, root, stateDirectory);
                return Result(state, true, root, stateDirectory);
            }
            await Verify(state.Inputs, root, stateDirectory, ct);
            VerifyDirectories(state.InputDirectories, root, stateDirectory);
            if (answer != null)
            {
                if (state.PendingArchiveId != answer.ArchiveId || state.PendingReason != "PasswordUnknown")
                    throw new ArgumentException("The password answer does not belong to the waiting archive.");
                state.Nodes.Single(n => n.Id == answer.ArchiveId).Password = answer.Password;
                await Save(file, state, ct);
            }
            async Task<RecursiveArchiveResult> Wait(string reason, Node? node, string message, IReadOnlyList<string>? tried = null,
                FileProcessingPathConflict? pathConflict = null)
            {
                state.PendingArchiveId = node?.Id; state.PendingReason = reason; state.Message = message;
                state.Tried = tried?.ToList();
                await Save(file, state, ct);
                return Result(state, false, root, stateDirectory) with {PathConflict = pathConflict};
            }
            if (state.Inputs.Count > options.MaxFiles) return await Wait("FileCountLimit", null, "The input manifest exceeds the file limit.");
            for (var index = 0; index < state.Nodes.Count; index++)
            {
                ct.ThrowIfCancellationRequested();
                var node = state.Nodes[index];
                if (node.Completed)
                {
                    await Verify(node.Outputs, root, stateDirectory, ct);
                    VerifyDirectories(node.OutputDirectories, root, stateDirectory);
                    continue;
                }
                if (state.Nodes.Count > options.MaxArchiveGroups) return await Wait("ArchiveCountLimit", node, "The archive-group limit was reached.");
                if (node.Depth >= options.MaxDepth) return await Wait("DepthLimit", node, "More archive layers remain. Increase the depth limit to continue.");
                for (var parent = node.ParentId; parent != null; parent = state.Nodes.Single(n => n.Id == parent).ParentId)
                    if (state.Nodes.Single(n => n.Id == parent).Fingerprint == node.Fingerprint)
                        return await Wait("RepeatedContent", node, "An archive repeats an ancestor's content. Review this branch before continuing.");
                await Verify(node.Inputs, root, stateDirectory, ct);
                try
                {
                    FileProcessingFiles.ValidateVolumes(node.Inputs.Select(f => Resolve(f, root, stateDirectory)),
                        inspectSiblingDownloads: false);
                }
                catch (InvalidOperationException error)
                {
                    return await Wait("ArchiveInputRequired", node,
                        error.Message + " Complete the volume set and submit this exact archive group again. " +
                        "This operation cannot add unselected files; completed parent layers remain saved.");
                }
                var legacyOuter = onlyOuterPackaging && node.Inputs[0].ExtractionLayout == null;
                var same = state.Nodes.FirstOrDefault(n => n.Completed && n.Fingerprint == node.Fingerprint &&
                    n.Inputs[0].ExtractionLayout == node.Inputs[0].ExtractionLayout);
                if (node.Outputs.Count == 0 && same != null)
                {
                    node.OutputRoot = same.OutputRoot;
                    node.OutputLinks = same.OutputLinks?.ToList();
                    node.Extracted = true;
                    node.Outputs = same.Outputs.Select(f => f with
                    {
                        StoragePolicy = node.StoragePolicy,
                        LogicalPath = Path.Combine(node.LogicalPrefix,
                            same.LogicalPrefix.Length == 0 ? f.LogicalPath : Path.GetRelativePath(same.LogicalPrefix, f.LogicalPath)),
                        Origins = OutputOrigins(node, f.Path),
                        GeneratedDirectories = ReusedAncestors(same, node, f.GeneratedDirectories, legacyOuter)
                    }).ToList();
                    node.OutputDirectories = same.OutputDirectories.Select(d => d with {LogicalPath = Path.Combine(
                        node.LogicalPrefix, same.LogicalPrefix.Length == 0 ? d.LogicalPath : Path.GetRelativePath(same.LogicalPrefix, d.LogicalPath)),
                        GeneratedDirectories = ReusedAncestors(same, node, d.GeneratedDirectories, legacyOuter)}).ToList();
                    // Reuse skips decompression, but these are distinct logical files that will
                    // be copied into the published result and must still consume its budget.
                    state.ExpandedBytes = checked(state.ExpandedBytes + node.Outputs.Sum(f => f.Length));
                    state.GeneratedFiles = checked(state.GeneratedFiles + node.Outputs.Count);
                    await Save(file, state, ct);
                }
                if (node.Outputs.Count == 0 && !node.Extracted)
                {
                    var free = FreeBytes(stateDirectory);
                    if (free < options.MinimumFreeBytes || free - options.MinimumFreeBytes < node.Inputs.Sum(f => f.Length))
                        return await Wait("DiskSpaceLimit", node, "There is not enough free space to stage this archive.");
                    if (state.ExpandedBytes >= options.MaxExpandedBytes) return await Wait("ExpandedBytesLimit", node, "The expanded-byte limit was reached.");
                    if (state.GeneratedFiles >= options.MaxFiles) return await Wait("FileCountLimit", node, "The generated-file limit was reached.");
                    var paths = node.Inputs.Select(f => Resolve(f, root, stateDirectory)).ToList();
                    var archiveRoot = Path.GetDirectoryName(paths[0])!;
                    var operationDirectory = Path.Combine(stateDirectory, "operations", node.Id);
                    var bounded = new BudgetedExtraction(extraction, options, state.ExpandedBytes, state.GeneratedFiles);
                    var executor = new FileProcessingPlanExecutor(bounded, materializer, writeGuard);
                    FileProcessingPlanResult result;
                    try
                    {
                        result = await executor.ExecuteAsync(node.Plan!, archiveRoot, paths, operationDirectory,
                            node.Password, progress, ct, identityPath: identityPath, storagePolicy: node.StoragePolicy, storageTitle: state.StorageTitle,
                            extractionContentsAsRoot: legacyOuter, passwordEvidence: passwordEvidence);
                    }
                    catch (OperationCanceledException) when (!ct.IsCancellationRequested && bounded.LimitReason != null)
                    { return await Wait(bounded.LimitReason, node, "The extraction budget was reached. Increase the limit to retry this archive."); }
                    if (!result.Completed)
                        return await Wait(result.NeedsPassword ? "PasswordUnknown" : result.FailureCode ?? "ArchiveInputRequired", node,
                            result.Message ?? "Archive input is required.", result.TriedPasswords, result.PathConflict);
                    await Verify(node.Inputs, root, stateDirectory, ct);
                    var outputRoot = result.OutputDirectory ?? throw new InvalidDataException("The archive produced no output directory.");
                    FileProcessingFiles.Within(outputRoot, stateDirectory);
                    node.OutputRoot = Path.GetRelativePath(stateDirectory, outputRoot);
                    var outputEvidence = result.FileLineage?.OutputManifest is { } measuredOutput
                        ? FileArtifactDeliveryLayout.Index(result.DirectoryProvenance, measuredOutput) :
                            new Dictionary<(string, bool), IReadOnlyList<string>>();
                    foreach (var path in result.Files)
                        node.Outputs.Add((await Capture(path, stateDirectory, true,
                            legacyOuter ? Path.GetRelativePath(outputRoot, path) :
                            Path.Combine(Path.GetDirectoryName(node.Inputs[0].LogicalPath) ?? "", Path.GetRelativePath(outputRoot, path)), ct))
                            with {StoragePolicy = node.StoragePolicy, ExtractionLayout = node.Inputs[0].ExtractionLayout,
                                GeneratedDirectories = NestedAncestors(node, outputEvidence.GetValueOrDefault(
                                    (Path.GetRelativePath(outputRoot, path).Replace('\\', '/'), false)), legacyOuter)});
                    node.OutputDirectories = (result.Directories ?? []).Select(path => new DirectoryFact(
                        Path.GetRelativePath(stateDirectory, FileProcessingFiles.Within(path, stateDirectory)), true,
                        legacyOuter ? Path.GetRelativePath(outputRoot, path) :
                            Path.Combine(Path.GetDirectoryName(node.Inputs[0].LogicalPath) ?? "", Path.GetRelativePath(outputRoot, path)),
                        NestedAncestors(node, outputEvidence.GetValueOrDefault(
                            (Path.GetRelativePath(outputRoot, path).Replace('\\', '/'), true)), legacyOuter))).ToList();
                    if (state.LineageVersion == 1 && result.FileLineage is {InputManifest: not null, OutputManifest: not null} evidence)
                    {
                        var capturedInputs = Manifest(node.Inputs, f => Path.GetRelativePath(archiveRoot, Resolve(f, root, stateDirectory)));
                        var capturedOutputs = Manifest(node.Outputs, f => Path.GetRelativePath(outputRoot, Resolve(f, root, stateDirectory)),
                            node.OutputDirectories.Select(d => Path.GetRelativePath(outputRoot, ResolveDirectory(d, root, stateDirectory))).ToArray());
                        if (!SameManifest(capturedInputs, evidence.InputManifest) || !SameManifest(capturedOutputs, evidence.OutputManifest))
                            throw new InvalidDataException("A recursive node's files differ from its recorded processing evidence.");
                        node.OutputLinks = evidence.Entries.SelectMany(e => e.OutputRelativePaths.Select(output =>
                            new NodeFileLink(node.Inputs.FindIndex(f => Path.GetRelativePath(archiveRoot, Resolve(f, root, stateDirectory)).Replace('\\', '/') == e.InputRelativePath),
                                Path.GetRelativePath(stateDirectory, Path.Combine(outputRoot, output)), e.Replaced))).ToList();
                        if (node.OutputLinks.Any(link => link.InputIndex < 0 || !node.Outputs.Any(f => f.Path == link.OutputPath)))
                            throw new InvalidDataException("A recursive node's processing evidence names an unknown file.");
                        node.Outputs = node.Outputs.Select(f => f with {Origins = OutputOrigins(node, f.Path)}).ToList();
                    }
                    node.Extracted = true;
                    state.ExpandedBytes = checked(state.ExpandedBytes + node.Outputs.Sum(f => f.Length));
                    state.GeneratedFiles = checked(state.GeneratedFiles + node.Outputs.Count);
                    await Save(file, state, ct);
                }
                if (state.ExpandedBytes > options.MaxExpandedBytes) return await Wait("ExpandedBytesLimit", node, "The expanded-byte limit was reached.");
                if (state.GeneratedFiles > options.MaxFiles) return await Wait("FileCountLimit", node, "The generated-file limit was reached.");
                var consumed = node.Inputs.Select(Key).ToHashSet(StringComparer.Ordinal);
                var nextActive = state.Active.Where(f => !consumed.Contains(Key(f))).Concat(node.Outputs).ToList();
                if (FileProcessingPathProjection.Inspect(node.Plan?.Steps.LastOrDefault()?.Id ?? node.Id,
                        nextActive.Select(f => new FileProcessingPathProjectionEntry(f.LogicalPath, f.LogicalPath))
                            .Concat(state.ActiveDirectories.Concat(node.OutputDirectories)
                                .Select(d => new FileProcessingPathProjectionEntry(d.LogicalPath, d.LogicalPath, true))),
                        portable: node.Inputs[0].ExtractionLayout != null) is { } conflict)
                    return await Wait(FileProcessingPathProjection.FailureCode, node, conflict.Message, pathConflict: conflict);
                state.Active = nextActive;
                state.ActiveDirectories = state.ActiveDirectories.Concat(node.OutputDirectories).Distinct().ToList();
                if (!onlyInitialLayer)
                    await Discover(state, onlyOuterPackaging ? node.Outputs : node.Outputs.Where(f => node.OutputLinks == null ||
                        node.OutputLinks.Any(link => link.OutputPath == f.Path && link.Replaced)).ToList(),
                        node.Id, node.Depth + 1, root, stateDirectory, defaultPassword, ct);
                node.Completed = true;
                state.PendingArchiveId = null; state.PendingReason = null; state.Message = null; state.Tried = null;
                await Save(file, state, ct);
                progress?.Invoke(Math.Min(99, (index + 1) * 100 / Math.Max(state.Nodes.Count, 1)));
            }
            if (state.Active.Count > options.MaxFiles) return await Wait("FileCountLimit", null, "The result exceeds the file limit.");
            if (FreeBytes(stateDirectory) - options.MinimumFreeBytes < state.Active.Sum(f => f.Length))
                return await Wait("DiskSpaceLimit", null, "There is not enough free space to publish the final files.");
            await Verify(state.Active, root, stateDirectory, ct);
            VerifyDirectories(state.ActiveDirectories, root, stateDirectory);
            var pending = Path.Combine(stateDirectory, "result.pending");
            if (Directory.Exists(pending)) Directory.Delete(pending, true);
            CreatePrivate(pending);
            foreach (var input in state.Active)
            {
                var target = FileProcessingFiles.Within(Path.Combine(pending, input.LogicalPath), pending);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                await materializer.MaterializeAsync(Resolve(input, root, stateDirectory), target,
                    state.MaterializationVersion == 0 ? "copy" : input.StoragePolicy?.Materialization ?? state.StoragePolicy?.Materialization ?? "auto", ct);
            }
            var finalManifest = Manifest(state.Active, f => f.LogicalPath,
                state.ActiveDirectories.Select(d => d.LogicalPath).ToArray());
            ArtifactManifestFiles.CreateDirectories(pending, finalManifest);
            var resultDirectory = Path.Combine(stateDirectory, "result");
            if (!Directory.Exists(resultDirectory)) Directory.Move(pending, resultDirectory);
            else Directory.Delete(pending, true); // A crash can leave the committed result before its receipt.
            ArtifactManifestFiles.VerifyDirectories(resultDirectory, finalManifest);
            state.FinalDirectories = state.ActiveDirectories.Select(d => d with {
                Path = Path.GetRelativePath(stateDirectory, Path.Combine(resultDirectory, d.LogicalPath)), InState = true}).ToList();
            state.FinalFiles = [];
            foreach (var input in state.Active)
            {
                var actual = await Capture(Path.Combine(resultDirectory, input.LogicalPath), stateDirectory, true, input.LogicalPath, ct);
                if (actual.Length != input.Length || actual.Sha256 != input.Sha256)
                    throw new InvalidDataException("A published recursive output differs from its committed input.");
                state.FinalFiles.Add(actual with {Origins = input.Origins, GeneratedDirectories = input.GeneratedDirectories});
            }
            state.Completed = true;
            await Save(file, state, ct);
            progress?.Invoke(100);
            return Result(state, true, root, stateDirectory);
        }
        finally { gate.Release(); }
    }

    public static Task<IReadOnlyList<string>> GetReclaimableFilesAsync(string stateDirectory, CancellationToken ct = default, bool overrideRetention = false) =>
        GetReclaimableFilesCoreAsync(stateDirectory, ct, overrideRetention, verifyContents: true);

    internal static Task<IReadOnlyList<string>> GetReclaimableFilesForPreviewAsync(string stateDirectory, CancellationToken ct, bool overrideRetention) =>
        GetReclaimableFilesCoreAsync(stateDirectory, ct, overrideRetention, verifyContents: false);

    private static async Task<IReadOnlyList<string>> GetReclaimableFilesCoreAsync(string stateDirectory, CancellationToken ct, bool overrideRetention, bool verifyContents)
    {
        var stateFile = Path.Combine(stateDirectory, "recursive-state.json");
        ArtifactManifestFiles.RejectLinks(stateFile);
        if (!File.Exists(stateFile)) return [];
        var state = JsonSerializer.Deserialize<State>(await File.ReadAllTextAsync(stateFile, ct), Json);
        if (state is not {Completed: true, LineageVersion: 1} || state.PendingArchiveId != null || state.Nodes.Any(n => !n.Completed)) return [];
        var paths = new HashSet<string>(FileProcessingFiles.PathComparer);
        var operationsRoot = Path.Combine(stateDirectory, "operations");
        var retainedRoots = state.Nodes.Where(n => !overrideRetention && n.StoragePolicy?.CleanupIntermediates == false && n.OutputRoot != null)
            .Select(n => n.OutputRoot!).ToHashSet(FileProcessingFiles.PathComparer);
        foreach (var node in state.Nodes)
        {
            ct.ThrowIfCancellationRequested();
            if (node.OutputRoot == null || node.OutputLinks == null || !overrideRetention && node.StoragePolicy?.CleanupIntermediates == false ||
                retainedRoots.Contains(node.OutputRoot) || node.Outputs.Any(f => !HasCompleteReplacement(state, f))) continue;
            var nodeRoot = FileProcessingFiles.Within(Path.Combine(stateDirectory, node.OutputRoot), operationsRoot);
            if (Path.GetFileName(nodeRoot) != "result") throw new InvalidDataException("A recursive checkpoint has an invalid result directory.");
            var nestedStateFile = Path.Combine(Path.GetDirectoryName(nodeRoot)!, "state.json");
            ArtifactManifestFiles.RejectLinks(nestedStateFile);
            if (!File.Exists(nestedStateFile)) continue;
            var nested = JsonSerializer.Deserialize<FileProcessingPlanExecutor.State>(await File.ReadAllTextAsync(nestedStateFile, ct), Json);
            if (nested is not {Completed: true, LineageVersion: 1} || nested.FinalManifest == null) continue;
            // Every private node result must be represented by this node, with matching content,
            // and must reach the complete final manifest. A partial registration cannot erase it.
            if (nested.FinalManifest.Entries.Any(entry => !node.Outputs.Any(f =>
                    FileProcessingFiles.PathComparer.Equals(Resolve(f, "", stateDirectory), Path.Combine(nodeRoot, entry.RelativePath)) &&
                    f.Length == entry.Length && f.Sha256.Equals(entry.Sha256, StringComparison.OrdinalIgnoreCase)))) continue;
            foreach (var path in await (verifyContents
                         ? FileProcessingPlanExecutor.GetReclaimableFilesAsync(nodeRoot, ct, overrideRetention)
                         : FileProcessingPlanExecutor.GetReclaimableFilesForPreviewAsync(nodeRoot, ct, overrideRetention))) paths.Add(path);
            // A recursive node's result is private to this recursive executor. Audit all of its
            // registered final files, even when lineage contains only a selected subset.
            foreach (var file in nested.FinalFiles)
            {
                ct.ThrowIfCancellationRequested();
                var path = FileProcessingFiles.Within(file, nodeRoot);
                var fact = node.Outputs.First(f => FileProcessingFiles.PathComparer.Equals(Resolve(f, "", stateDirectory), path));
                if (File.Exists(path) && (verifyContents ? await StillMatches(path, fact.Length, fact.Sha256, ct)
                        : ArtifactCleanupPreviewFiles.MatchesLength(path, fact.Length))) paths.Add(path);
            }
        }
        return paths.ToArray();
    }

    /// <summary>Completed recursive outputs replace only internal node payloads, never user originals.
    /// Relative lineage and checkpoint metadata remain for inspection and completed-result reuse.</summary>
    public Task ReclaimCompletedIntermediatesAsync(string stateDirectory, ArtifactRecord result,
        IReadOnlySet<string> approvedFiles, CancellationToken ct = default, bool overrideRetention = false) =>
        ReclaimCompletedIntermediatesAsync(stateDirectory, result, approvedFiles, ct, overrideRetention, null);

    internal async Task ReclaimCompletedIntermediatesAsync(string stateDirectory, ArtifactRecord result,
        IReadOnlySet<string> approvedFiles, CancellationToken ct, bool overrideRetention, ArtifactCleanupFileSnapshot? snapshot,
        FileProcessingRelocatedResult? relocatedResult = null)
    {
        var expectedRoot = Path.Combine(stateDirectory, "result");
        if (!ArtifactManifestFiles.PathComparer.Equals(expectedRoot, result.RootDirectory)) return;
        var gate = Gates.GetOrAdd(stateDirectory, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            var stateFile = Path.Combine(stateDirectory, "recursive-state.json");
            ArtifactManifestFiles.RejectLinks(stateFile);
            if (!File.Exists(stateFile)) return;
            var state = JsonSerializer.Deserialize<State>(await File.ReadAllTextAsync(stateFile, ct), Json);
            if (state is not {Completed: true, LineageVersion: 1} || state.PendingArchiveId != null || state.Nodes.Any(n => !n.Completed)) return;
            if (!SameManifest(Manifest(state.FinalFiles, f => f.LogicalPath,
                    state.FinalDirectories.Select(d => d.LogicalPath).ToArray()), result.Manifest)) return;
            if (relocatedResult != null && !relocatedResult.Matches(result.RootDirectory, result.Manifest)) return;
            if (snapshot == null && !(await ArtifactManifestFiles.ValidateAsync(relocatedResult?.VerificationRoot ?? result.RootDirectory, result.Manifest, ct)).Available) return;
            var operationsRoot = Path.Combine(stateDirectory, "operations");
            var paths = await GetReclaimableFilesCoreAsync(stateDirectory, ct, overrideRetention, verifyContents: snapshot == null);
            if (paths.Any(path => !approvedFiles.Contains(path))) return;
            var expectedContents = new Dictionary<string, ArtifactManifestEntry>(FileProcessingFiles.PathComparer);
            foreach (var nodeRoot in state.Nodes.Where(n => n.OutputRoot != null).Select(n =>
                         FileProcessingFiles.Within(Path.Combine(stateDirectory, n.OutputRoot!), operationsRoot)).Distinct(FileProcessingFiles.PathComparer))
            {
                var nested = JsonSerializer.Deserialize<FileProcessingPlanExecutor.State>(await File.ReadAllTextAsync(
                    Path.Combine(Path.GetDirectoryName(nodeRoot)!, "state.json"), ct), Json);
                if (nested?.LineageVersion != 1) continue;
                foreach (var pair in nested.FileContents)
                    expectedContents[FileProcessingFiles.Within(Path.Combine(Path.GetDirectoryName(nodeRoot)!, pair.Key), Path.GetDirectoryName(nodeRoot)!)] = pair.Value;
            }
            foreach (var path in paths)
            {
                ct.ThrowIfCancellationRequested();
                ArtifactManifestFiles.RejectLinks(path);
                if (File.Exists(path) && expectedContents.TryGetValue(path, out var expected) &&
                    (snapshot == null ? await StillMatches(path, expected.Length, expected.Sha256, ct) : snapshot.Matches(path, expected.Length, ct))) File.Delete(path);
            }
            FileProcessingPlanExecutor.RemoveEmptyDirectories(operationsRoot, ct,
                state.Nodes.SelectMany(n => n.OutputDirectories).Select(d => ResolveDirectory(d, "", stateDirectory))
                    .ToHashSet(FileProcessingFiles.PathComparer));
            state.IntermediatesReclaimedAt = DateTime.UtcNow;
            await Save(stateFile, state, ct);
        }
        finally { gate.Release(); }
    }

    public static async Task<string?> GetPendingArchiveIdAsync(string stateDirectory, CancellationToken ct = default)
    {
        var file = Path.Combine(stateDirectory, "recursive-state.json");
        return File.Exists(file) ? JsonSerializer.Deserialize<State>(await File.ReadAllTextAsync(file, ct), Json)?.PendingArchiveId : null;
    }

    public static void ValidateOptions(RecursiveArchiveOptions options)
    {
        if (options.MaxDepth is < 1 or > 128 || options.MaxArchiveGroups is < 1 or > 100000 ||
            options.MaxFiles is < 1 or > 1000000 || options.MaxExpandedBytes < 1 || options.MinimumFreeBytes < 0)
            throw new ArgumentException("The recursive archive limits are invalid.");
    }

    private static async Task Discover(State state, IReadOnlyList<FileFact> files, string? parentId, int depth,
        string root, string directory, string? password, CancellationToken ct)
    {
        var paths = files.Select(f => Resolve(f, root, directory)).ToArray();
        var groups = state.OnlyOuterPackaging
            ? await FileArtifactDeliveryAssessment.ConfirmedSingleOuterArchiveAsync(paths, ct)
            : await AuthorizedArchiveDetection.DetectAsync(paths, recursive: true, ct);
        foreach (var group in groups)
        {
            var facts = group.Files.Select(path => files.First(f => FileProcessingFiles.PathComparer.Equals(Resolve(f, root, directory), path))).ToList();
            var fingerprint = Hash(string.Join("\n", facts.Select(f => f.Length + ":" + f.Sha256)));
            var id = Hash((parentId ?? "input") + "\n" + fingerprint + "\n" + string.Join("\n", facts.Select(f => f.LogicalPath)));
            if (state.Nodes.Any(n => n.Id == id)) continue;
            var layout = facts[0].ExtractionLayout;
            var containerDirectory = Path.GetDirectoryName(facts[0].LogicalPath) ?? "";
            var logicalPrefix = layout == FileExtractionLayouts.CurrentDirectory ? containerDirectory : state.OnlyOuterPackaging && layout == null ? "" :
                Path.Combine(containerDirectory, Path.GetFileNameWithoutExtension(facts[0].LogicalPath));
            state.Nodes.Add(new Node { Id = id, ParentId = parentId, Depth = depth, Fingerprint = fingerprint,
                Inputs = facts, LogicalPrefix = logicalPrefix, StoragePolicy = InheritedPolicy(facts, state.StoragePolicy), Plan = new() { Requirement = "required",
                    Steps = [new() { Id = "extract", Op = "extractArchive", Password = password, ExtractionLayout = layout }] } });
        }
        await Task.CompletedTask;
    }

    private static ArtifactStoragePolicy? InheritedPolicy(IReadOnlyList<FileFact> facts, ArtifactStoragePolicy? fallback)
    {
        var policies = facts.Select(f => f.StoragePolicy ?? fallback).Where(p => p != null).Cast<ArtifactStoragePolicy>().ToList();
        return policies.Count == 0 ? null : policies[0] with {CleanupIntermediates = policies.All(p => p.CleanupIntermediates),
            Materialization = policies.Any(p => p.Materialization == "copy") ? "copy" : "auto"};
    }

    private static RecursiveArchiveResult Result(State state, bool complete, string root, string directory) => new(
        complete, (complete ? state.FinalFiles : state.Active).Select(f => Resolve(f, root, directory)).ToList(),
        complete ? Path.Combine(directory, "result") : null, state.PendingReason, state.PendingArchiveId, state.Message,
        state.Tried, state.Nodes.Where(n => n.Completed).Select(n => new RecursiveArchiveLineage(n.Id, n.ParentId, n.Depth,
            n.Inputs.Select(f => Resolve(f, root, directory)).ToList(), n.Fingerprint,
            Path.Combine(directory, n.OutputRoot!), n.Outputs.Select(f => Resolve(f, root, directory)).ToList())).ToList(),
        state.Nodes.Where(n => n.Completed && n.ParentId == null).SelectMany(n => n.Inputs)
            .Select(f => Resolve(f, root, directory)).Distinct(FileProcessingFiles.PathComparer).ToList(),
        complete ? CreateFileLineage(state) : null,
        complete ? state.FinalDirectories.Select(d => ResolveDirectory(d, root, directory)).ToArray() : null,
        complete && state.GeneratedDirectoryVersion == 1 ? FileArtifactDeliveryLayout.Bind(
            Manifest(state.FinalFiles, f => f.LogicalPath, state.FinalDirectories.Select(d => d.LogicalPath).ToArray()),
            state.FinalFiles.Select(f => new ArtifactDirectoryEntry(f.LogicalPath, false, f.GeneratedDirectories ?? []))
                .Concat(state.FinalDirectories.Select(d => new ArtifactDirectoryEntry(d.LogicalPath, true, d.GeneratedDirectories ?? [])))) : null);

    private static IReadOnlyList<string> ReusedAncestors(Node previous, Node next, IReadOnlyList<string>? ancestors, bool outer)
    {
        var from = previous.LogicalPrefix.Replace('\\', '/'); var to = next.LogicalPrefix.Replace('\\', '/');
        var relocated = from.Length == 0 ? ancestors ?? [] : (ancestors ?? [])
            .Where(p => FileArtifactDeliveryLayout.Within(p, from, true)).Select(p => to + p[from.Length..]);
        return NestedAncestors(next, [], outer).Concat(relocated).Distinct(StringComparer.Ordinal).ToArray();
    }

    private static IReadOnlyList<string> NestedAncestors(Node node, IReadOnlyList<string>? local, bool outer)
    {
        var parent = outer ? "" : (Path.GetDirectoryName(node.Inputs[0].LogicalPath) ?? "").Replace('\\', '/');
        var inherited = outer || parent.Length == 0 ? [] : (node.Inputs[0].GeneratedDirectories ?? [])
            .Where(p => FileArtifactDeliveryLayout.Within(parent, p, true) &&
                node.Inputs.All(f => (f.GeneratedDirectories ?? []).Contains(p, StringComparer.Ordinal)));
        return inherited.Concat((local ?? []).Select(p => parent.Length == 0 ? p : parent + "/" + p))
            .Distinct(StringComparer.Ordinal).ToArray();
    }

    private static IReadOnlyList<FileProcessingPlanExecutor.FileOrigin>? OutputOrigins(Node node, string outputPath)
    {
        var links = node.OutputLinks?.Where(link => link.OutputPath == outputPath).ToList();
        if (links is not {Count: > 0} || links.Any(link => link.InputIndex < 0 || link.InputIndex >= node.Inputs.Count ||
                node.Inputs[link.InputIndex].Origins == null)) return null;
        return links.SelectMany(link => node.Inputs[link.InputIndex].Origins!
                .Select(origin => origin with {Replaced = origin.Replaced || link.Replaced}))
            .Distinct().ToArray();
    }

    private static ArtifactManifest Manifest(IEnumerable<FileFact> files, Func<FileFact, string> relativePath,
        IReadOnlyList<string>? directories = null)
    {
        var entries = files.Select(f => new ArtifactManifestEntry(relativePath(f).Replace('\\', '/'), f.Length, f.Sha256))
            .OrderBy(e => e.RelativePath, StringComparer.Ordinal).ToArray();
        var normalized = directories?.Select(path => path.Replace('\\', '/')).Distinct(FileProcessingFiles.PathComparer)
            .OrderBy(path => path, StringComparer.Ordinal).ToArray();
        return new(entries, ArtifactManifestFiles.Fingerprint(entries, normalized), normalized);
    }

    private static bool SameManifest(ArtifactManifest first, ArtifactManifest second) =>
        first.Fingerprint == ArtifactManifestFiles.Fingerprint(first.Entries, first.Directories) &&
        second.Fingerprint == ArtifactManifestFiles.Fingerprint(second.Entries, second.Directories) && first.Fingerprint == second.Fingerprint &&
        first.Entries.OrderBy(e => e.RelativePath, StringComparer.Ordinal)
            .SequenceEqual(second.Entries.OrderBy(e => e.RelativePath, StringComparer.Ordinal));

    private static FileLineage? CreateFileLineage(State state)
    {
        if (state.LineageVersion != 1 || state.FinalFiles.Count == 0 || state.FinalFiles.Any(f => f.Origins == null)) return null;
        var input = Manifest(state.Inputs, f => f.Path, state.InputDirectories.Select(d => d.Path).ToArray());
        var output = Manifest(state.FinalFiles, f => f.LogicalPath, state.FinalDirectories.Select(d => d.LogicalPath).ToArray());
        return new(input.Entries.Select(entry =>
        {
            var files = state.FinalFiles.Where(f => f.Origins!.Any(o => o.InputRelativePath == entry.RelativePath)).ToArray();
            return new FileLineageEntry(entry.RelativePath, files.Select(f => f.LogicalPath.Replace('\\', '/')).Distinct(StringComparer.Ordinal).ToArray(),
                files.Any(f => f.Origins!.Any(o => o.InputRelativePath == entry.RelativePath && o.Replaced)));
        }).ToArray(), input, output);
    }

    private static bool HasCompleteReplacement(State state, FileFact source)
    {
        var visiting = new HashSet<string>(StringComparer.Ordinal);
        var answers = new Dictionary<string, bool>(StringComparer.Ordinal);
        bool Visit(FileFact file)
        {
            var key = Key(file);
            if (answers.TryGetValue(key, out var answer)) return answer;
            if (!visiting.Add(key)) return false;
            var covered = state.FinalFiles.Any(f => f.LogicalPath == file.LogicalPath && f.Length == file.Length &&
                f.Sha256.Equals(file.Sha256, StringComparison.OrdinalIgnoreCase));
            if (!covered)
            {
                var consumers = state.Nodes.Where(n => n.Completed && n.Inputs.Any(input => Key(input) == key &&
                    input.Length == file.Length && input.Sha256.Equals(file.Sha256, StringComparison.OrdinalIgnoreCase))).ToList();
                covered = consumers.Count > 0 && consumers.All(node =>
                {
                    var index = node.Inputs.FindIndex(input => Key(input) == key);
                    var links = node.OutputLinks?.Where(link => link.InputIndex == index).ToArray();
                    return links is {Length: > 0} && links.All(link =>
                        node.Outputs.SingleOrDefault(output => output.Path == link.OutputPath) is { } output && Visit(output));
                });
            }
            visiting.Remove(key);
            return answers[key] = covered;
        }
        return Visit(source);
    }

    private static async Task<bool> StillMatches(string path, long length, string hash, CancellationToken ct)
    {
        try
        {
            var manifest = await ArtifactManifestFiles.CaptureAsync(Path.GetDirectoryName(path)!, [path], ct);
            var actual = manifest.Entries.Single();
            return actual.Length == length && actual.Sha256.Equals(hash, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException)
        { return false; }
    }

    private static string Resolve(FileFact fact, string root, string directory) => FileProcessingFiles.Within(
        Path.Combine(fact.InState ? directory : root, fact.Path), fact.InState ? directory : root);
    private static string ResolveDirectory(DirectoryFact fact, string root, string directory) => FileProcessingFiles.Within(
        Path.Combine(fact.InState ? directory : root, fact.Path), fact.InState ? directory : root);
    private static void VerifyDirectories(IEnumerable<DirectoryFact> directories, string root, string stateDirectory)
    {
        foreach (var fact in directories)
        {
            var path = ResolveDirectory(fact, root, stateDirectory);
            ArtifactManifestFiles.RejectLinks(path);
            if (!File.GetAttributes(path).HasFlag(FileAttributes.Directory))
                throw new InvalidDataException("A recorded recursive output directory was replaced.");
        }
    }
    private static string Key(FileFact file) => (file.InState ? "state:" : "input:") + file.Path + "\n" + file.LogicalPath;
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    private static async Task<FileFact> Capture(string path, string root, bool inState, string logicalPath, CancellationToken ct)
    {
        path = FileProcessingFiles.Within(path, root);
        await using var file = File.OpenRead(path);
        var length = file.Length;
        var hash = Convert.ToHexString(await SHA256.HashDataAsync(file, ct)).ToLowerInvariant();
        if (file.Length != length) throw new InvalidDataException("A file changed while its manifest was captured.");
        return new(Path.GetRelativePath(root, path), inState, logicalPath, length, hash);
    }
    private static async Task Verify(IEnumerable<FileFact> files, string root, string directory, CancellationToken ct)
    {
        foreach (var expected in files)
        {
            var path = Resolve(expected, root, directory);
            var actual = await Capture(path, expected.InState ? directory : root, expected.InState, expected.LogicalPath, ct);
            if (actual.Length != expected.Length || actual.Sha256 != expected.Sha256)
                throw new InvalidDataException("A recursive archive input or committed output was modified.");
        }
    }
    private static void CreatePrivate(string path)
    {
        if (OperatingSystem.IsWindows()) Directory.CreateDirectory(path);
        else Directory.CreateDirectory(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }
    private static async Task Save(string path, State state, CancellationToken ct)
    {
        var temporary = path + ".tmp";
        await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(state, Json), ct);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(temporary, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        File.Move(temporary, path, true);
    }
    private static long FreeBytes(string directory) => new DriveInfo(directory).AvailableFreeSpace;

    public sealed record FileFact(string Path, bool InState, string LogicalPath, long Length, string Sha256,
        ArtifactStoragePolicy? StoragePolicy = null, IReadOnlyList<FileProcessingPlanExecutor.FileOrigin>? Origins = null,
        IReadOnlyList<string>? GeneratedDirectories = null, string? ExtractionLayout = null);
    public sealed record DirectoryFact(string Path, bool InState, string LogicalPath, IReadOnlyList<string>? GeneratedDirectories = null);
    public sealed record NodeFileLink(int InputIndex, string OutputPath, bool Replaced);
    public sealed class Node
    {
        public ArtifactStoragePolicy? StoragePolicy { get; set; }
        public List<NodeFileLink>? OutputLinks { get; set; }
        public string Id { get; set; } = "";
        public string? ParentId { get; set; }
        public int Depth { get; set; }
        public string Fingerprint { get; set; } = "";
        public string LogicalPrefix { get; set; } = "";
        public List<FileFact> Inputs { get; set; } = [];
        public FileProcessingPlan? Plan { get; set; }
        public string? Password { get; set; }
        public bool Extracted { get; set; }
        public bool Completed { get; set; }
        public string? OutputRoot { get; set; }
        public List<FileFact> Outputs { get; set; } = [];
        public List<DirectoryFact> OutputDirectories { get; set; } = [];
    }
    public sealed class State
    {
        public int MaterializationVersion { get; set; }
        public int LineageVersion { get; set; }
        public int GeneratedDirectoryVersion { get; set; }
        public bool OnlyOuterPackaging { get; set; }
        public ArtifactStoragePolicy? StoragePolicy { get; set; }
        public string? StorageTitle { get; set; }
        public List<FileFact> Inputs { get; set; } = [];
        public List<DirectoryFact> InputDirectories { get; set; } = [];
        public List<DirectoryFact> ActiveDirectories { get; set; } = [];
        public List<DirectoryFact> FinalDirectories { get; set; } = [];
        public List<FileFact> Active { get; set; } = [];
        public List<Node> Nodes { get; set; } = [];
        public long ExpandedBytes { get; set; }
        public int GeneratedFiles { get; set; }
        public string? PendingArchiveId { get; set; }
        public string? PendingReason { get; set; }
        public string? Message { get; set; }
        public List<string>? Tried { get; set; }
        public bool Completed { get; set; }
        public DateTime? IntermediatesReclaimedAt { get; set; }
        public List<FileFact> FinalFiles { get; set; } = [];
    }

    private sealed class BudgetedExtraction(IArchiveExtractionService inner, RecursiveArchiveOptions options,
        long previousBytes, int previousFiles) : IArchiveExtractionService
    {
        public string? LimitReason { get; private set; }
        public Task<ArchivePasswordProbe> ProbePasswordAsync(string entry, IReadOnlyList<string?> candidates,
            Action<string>? output, CancellationToken ct) => inner.ProbePasswordAsync(entry, candidates, output, ct);
        public Task<ArchivePasswordProbe> ProbePasswordAsync(string entry, IReadOnlyList<string?> candidates,
            Action<string>? output, CancellationToken ct, ArchiveOpenOptions? opening) =>
            inner.ProbePasswordAsync(entry, candidates, output, ct, opening);
        public async Task<ArchiveExtractionResult> ExtractAsync(ArchiveExtractionRequest request, Action<int>? progress, CancellationToken ct)
        {
            using var stop = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var sync = new object();
            Exception? checkError = null;
            void Check()
            {
                lock (sync)
                {
                    if (LimitReason != null || checkError != null) return;
                    try
                    {
                        if (FreeBytes(Path.GetDirectoryName(request.Directory)!) < options.MinimumFreeBytes)
                            LimitReason = "DiskSpaceLimit";
                        if (LimitReason == null && Directory.Exists(request.Directory))
                        {
                            // Stop at the first exceeded bound instead of materializing an arbitrarily
                            // large archive's directory tree before noticing the limit.
                            var directories = new Stack<string>(); directories.Push(request.Directory);
                            long bytes = previousBytes;
                            var count = previousFiles;
                            while (directories.Count > 0 && LimitReason == null)
                            {
                                var directory = FileProcessingFiles.Within(directories.Pop(), request.Directory);
                                foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
                                {
                                    FileProcessingFiles.Within(entry, request.Directory);
                                    if (Directory.Exists(entry)) { directories.Push(entry); continue; }
                                    if (++count > options.MaxFiles) { LimitReason = "FileCountLimit"; break; }
                                    var size = new FileInfo(entry).Length;
                                    if (size > options.MaxExpandedBytes - bytes) { LimitReason = "ExpandedBytesLimit"; break; }
                                    bytes += size;
                                }
                            }
                        }
                    }
                    catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException)
                    { checkError = error; }
                    if (LimitReason != null || checkError != null) stop.Cancel();
                }
            }
            var monitor = Task.Run(async () =>
            {
                try { while (true) { await Task.Delay(200, stop.Token); Check(); } }
                catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
            }, CancellationToken.None);
            try
            {
                ArchiveExtractionResult result;
                try { result = await inner.ExtractAsync(request, p => { Check(); progress?.Invoke(p); }, stop.Token); }
                catch (OperationCanceledException) when (checkError != null)
                { throw new InvalidDataException("An archive output could not be safely inspected.", checkError); }
                Check();
                if (checkError != null) throw new InvalidDataException("An archive output could not be safely inspected.", checkError);
                stop.Token.ThrowIfCancellationRequested();
                return result;
            }
            finally { stop.Cancel(); await monitor; }
        }
    }
}
