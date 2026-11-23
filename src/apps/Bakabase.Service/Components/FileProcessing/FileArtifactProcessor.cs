using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Infrastructures.Components.App;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.Modules.Workflow.Artifacts;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Service.Components.FileProcessing;

public sealed record FileArtifactProcessingConfig
{
    public string ReusePolicy { get; init; } = "reuseVerified";
    public string ArchivePolicy { get; init; } = "instructions";
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? ExtractionLayout { get; init; }
    public RecursiveArchiveOptions Recursive { get; init; } = new();
    /// <summary>Optional placement override; never changes semantic operation identity.</summary>
    public string? WorkspaceRootDirectory { get; init; }
    // Evidence from a frozen stage binding; runtime placement does not change the persisted operation identity.
    [System.Text.Json.Serialization.JsonIgnore]
    internal string? BoundInputDirectory { get; init; }
    [System.Text.Json.Serialization.JsonIgnore]
    internal ArchiveGroupOwner? ArchiveGroupOwner { get; init; }
    [System.Text.Json.Serialization.JsonIgnore]
    internal bool BorrowSelectedProcessedArchives { get; init; }
    [System.Text.Json.Serialization.JsonIgnore]
    internal string? RetainArchiveGroupToken { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public ArtifactDirectoryScope? WorkspaceDirectoryScope { get; init; }
    public ArtifactStoragePolicy? StoragePolicy { get; init; }
    public string? StorageTitle { get; init; }

    internal FileProcessingPlan? ResolveInitialPlan(FileProcessingPlan plan)
    {
        if (ArchivePolicy == "keep") return new() {Requirement = "notRequired"};
        if (plan.Requirement != "unknown" && (plan.Requirement != "required" || plan.Steps.Count > 0)) return plan;
        return ArchivePolicy == "autoDetect" ? new() {Requirement = "notRequired"} : null;
    }
}

public sealed record FileArtifactProcessingMetadata(string? InputArtifactId, string PlanJson,
    string? InputMetadataJson, int ArchiveCount = 0, string? CompletedThroughStepId = null,
    ArtifactStoragePolicy? StoragePolicy = null, FileDispositionProof? FileDisposition = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    ArtifactDirectoryProvenance? DirectoryProvenance = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    string? InheritedExtractionLayout = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    ArtifactDirectoryScope? WorkspaceDirectoryScope = null)
{
    [System.Text.Json.Serialization.JsonInclude]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    internal Bakabase.Service.Components.Acquisition.Planning.AcquisitionProcessingStageCommitReceipt? StageCommit { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? RetainedArchiveFiles { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? RetainedGroupTokens { get; init; }
}
public sealed record FileArtifactProcessingResult(ArtifactRecord? Artifact, string? WaitReason = null,
    string? Message = null, string? ArchiveId = null, IReadOnlyList<string>? TriedPasswords = null,
    string? RequiredComponentId = null, FileProcessingPathConflict? PathConflict = null)
{
    internal IncompleteArchiveGroup? IncompleteArchiveGroup { get; init; }
    internal static FileArtifactProcessingResult InstructionsRequired() => new(null, "ExtractionPlanUnknown",
        "Provide file-processing instructions, confirm no extraction is needed, or explicitly approve automatic archive detection. Recursive extraction alone does not approve the initial files.");
}

/// <summary>Private workflow continuation constraint; never an instruction to create or retry an operation.</summary>
public sealed record FileArtifactProcessingRecovery(string OperationId, int Attempt)
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public bool RepairArchiveVolumes { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? SelectedInputArtifactId { get; init; }
}
internal sealed record FileArtifactCheckpointContinuation(string OperationKey, int Attempt, string CheckpointRoot,
    FileProcessingCheckpointInput Input, bool ResumeExistingOperation);

/// <summary>
/// Reusable file operation. It accepts exact file artifacts and an opaque logical source intent;
/// it knows nothing about posts, URLs, download tools, or workflow run histories.
/// </summary>
public sealed class FileArtifactProcessor : IFileArtifactProcessor
{
    private readonly IArtifactOperationStore store;
    private readonly FileProcessingPlanExecutor plans;
    private readonly RecursiveArchiveExecutor recursive;
    private readonly Func<string> appData;
    private readonly IServiceScopeFactory scopes;
    private readonly Func<string?> workspaceRoot;
    private readonly IUserStoragePolicy? storage;
    private readonly ArtifactWorkspaceMaintenance? maintenance;

    public FileArtifactProcessor(IArtifactOperationStore store, FileProcessingPlanExecutor plans,
        RecursiveArchiveExecutor recursive, AppService app, IServiceScopeFactory scopes,
        IUserStoragePolicy storage, ArtifactWorkspaceMaintenance maintenance)
        : this(store, plans, recursive, () => app.AppDataDirectory, scopes,
            storage: storage, maintenance: maintenance) { }

    internal FileArtifactProcessor(IArtifactOperationStore store, FileProcessingPlanExecutor plans,
        RecursiveArchiveExecutor recursive, Func<string> appData, IServiceScopeFactory scopes,
        Func<string?>? workspaceRoot = null, IUserStoragePolicy? storage = null, ArtifactWorkspaceMaintenance? maintenance = null)
    { this.store = store; this.plans = plans; this.recursive = recursive; this.appData = appData; this.scopes = scopes;
        this.workspaceRoot = workspaceRoot ?? (() => null); this.storage = storage; this.maintenance = maintenance; }

    // This namespace identifies compatible, pinned workflow checkpoints. A processing recipe
    // revision must not make an existing waiting run discard its completed steps or bindings.
    public const string IntentVersion = "files-v2:";
    private const string ProcessingRecipeVersion = "file-artifacts-v3";
    private const string ResolvedRecipeSchemaVersion = "file-artifacts-v2";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromMinutes(2);

    private sealed record BorrowedInputCheckpoint(int Version, string ArtifactId, string InputOperationId,
        string Fingerprint, string ProcessingOperationId, int Attempt);

    /// <summary>
    /// A pure outer-packaging pass can read its already immutable artifact directly. The
    /// durable caller owns that input through waits; the operation's normal lease still
    /// owns every newly produced file. Existing snapshot layouts are never reinterpreted.
    /// </summary>
    private async Task<FileProcessingPlanResult?> BorrowOuterInputAsync(string state, string checkpointDirectory,
        ArtifactOperationDbModel operation, ArtifactRecord input, FileProcessingPlan plan,
        FileArtifactProcessingConfig config, bool onlyOuterPackaging, string? stopAfterStepId,
        CancellationToken ct)
    {
        var marker = FileProcessingFiles.Within(Path.Combine(state, "borrowed-input.json"), state);
        ArtifactManifestFiles.RejectLinks(marker);
        if (Directory.Exists(marker)) throw new FileProcessingInputChangedException("The borrowed input checkpoint is not a file.");
        var exists = File.Exists(marker);
        var eligible = onlyOuterPackaging && config.ArchivePolicy == "autoDetect" &&
                       (config.StoragePolicy?.Materialization is null or "auto") &&
                       plan.Requirement == "notRequired" && plan.Steps.Count == 0 && stopAfterStepId == null;
        if (!eligible)
        {
            if (exists) throw new FileProcessingInputChangedException("The borrowed input belongs to a different processing mode.");
            return null;
        }
        // A prior attempt already tied recursive-state to its plan/result paths. Even an
        // empty or interrupted legacy checkpoint keeps that layout and all its receipts.
        if (!exists)
        {
            if (Directory.Exists(checkpointDirectory) || File.Exists(checkpointDirectory)) return null;
            if (Directory.Exists(Path.Combine(state, "recursive")) || File.Exists(Path.Combine(state, "recursive")))
                throw new FileProcessingInputChangedException("The existing recursive checkpoint has no input-layout receipt. Retain it for review.");
        }
        var expected = new BorrowedInputCheckpoint(1, input.Id, input.OperationId, input.Fingerprint, operation.Id, operation.Attempt);
        if (exists)
        {
            if (new FileInfo(marker).Length > 4096 || Directory.Exists(checkpointDirectory) || File.Exists(checkpointDirectory))
                throw new FileProcessingInputChangedException("The borrowed input checkpoint changed.");
            BorrowedInputCheckpoint? saved;
            try { saved = JsonSerializer.Deserialize<BorrowedInputCheckpoint>(await File.ReadAllTextAsync(marker, ct), Json); }
            catch (JsonException) { throw new FileProcessingInputChangedException("The borrowed input checkpoint is invalid."); }
            if (saved != expected) throw new FileProcessingInputChangedException("The borrowed input checkpoint belongs to different files or an attempt.");
        }
        // Currently the artifact store supports durable workflow owners. Unscoped callers
        // keep snapshots; a short-lived lease is not sufficient protection across a wait.
        var owner = StorageWriteScope.Current;
        if (owner is not {OwnerKind: "workflow"} || !int.TryParse(owner.OwnerId, out var runId) || runId <= 0)
        {
            if (exists) throw new FileProcessingInputChangedException("Resume the borrowed input through its durable owner.");
            return null;
        }
        var ownerId = runId.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var verified = await PinVerifiedProcessingInputAsync(input, operation, ownerId, ct);
        if (!exists)
        {
            // Publish the choice before recursion creates its path-bound state. A crash can
            // leave only an unused tiny temporary file, never an ambiguous snapshot layout.
            var pending = marker + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                var options = new FileStreamOptions {Mode = FileMode.CreateNew, Access = FileAccess.Write,
                    Share = FileShare.None, Options = FileOptions.Asynchronous | FileOptions.WriteThrough};
                if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
                await using (var stream = new FileStream(pending, options))
                {
                    await JsonSerializer.SerializeAsync(stream, expected, Json, ct);
                    await stream.FlushAsync(ct); stream.Flush(flushToDisk: true);
                }
                ct.ThrowIfCancellationRequested();
                ArtifactManifestFiles.RejectLinks(marker);
                File.Move(pending, marker, overwrite: false);
            }
            finally { if (File.Exists(pending)) File.Delete(pending); }
        }
        var lineage = new FileLineage(verified.Manifest.Entries.Select(e =>
            new FileLineageEntry(e.RelativePath, [e.RelativePath])).ToArray(), verified.Manifest, verified.Manifest);
        return new(true, verified.Files, verified.RootDirectory, ExtractedFiles: [],
            StoragePolicy: config.StoragePolicy, StorageTitle: config.StorageTitle, FileLineage: lineage,
            Directories: ArtifactManifestFiles.ResolveDirectories(verified.RootDirectory, verified.Manifest),
            DirectoryProvenance: FileArtifactDeliveryLayout.ReadProvenance(verified));
    }

    public static string Intent(string inputIntent, string? planJson, string? defaultPassword,
        FileArtifactProcessingConfig config, string? forceKey = null) =>
        CreateIntent(inputIntent, planJson, defaultPassword, config, forceKey, ProcessingRecipeVersion);

    private static string CreateIntent(string inputIntent, string? planJson, string? defaultPassword,
        FileArtifactProcessingConfig config, string? forceKey, string version)
    {
        // Evidence is explanatory text, not an instruction. Budgets affect suspension, not the
        // desired final content, so increasing a budget continues the same durable work list.
        object? instructions;
        try
        {
            var plan = FileExtractionLayouts.Apply(JsonSerializer.Deserialize<FileProcessingPlan>(planJson ?? "{}", Json) ?? new(), config.ExtractionLayout);
            // Preserve the exact legacy instruction shape for pinned checkpoints. Explicit
            // multi-input steps use their effective references; the ignored Input is not semantic.
            instructions = new {plan.Requirement, Steps = plan.Steps.Select(s => Instruction(s, defaultPassword))};
        }
        catch (JsonException) { instructions = planJson; }
        var recipe = JsonSerializer.Serialize(new {Version = version, Input = inputIntent,
            Instructions = instructions, config.ArchivePolicy, Recursive = config.Recursive.Enabled, Password = defaultPassword,
            Force = forceKey}, Json);
        return IntentVersion + Hash(config.ExtractionLayout == null ? recipe :
            JsonSerializer.Serialize(new {recipe, config.ExtractionLayout}, Json));
    }

    private static object Instruction(FileProcessingStep step, string? defaultPassword)
    {
        if (step.Op == "rebaseDirectory")
            return new {step.Id, step.Op, Inputs = step.GetInputReferences(), step.SourceDirectory, step.TargetDirectory};
        if (step.Op == "renameLiteral")
            return new {step.Id, step.Op, Inputs = step.GetInputReferences(), step.Selector, step.FindText, step.ReplaceText};
        if (step.Op == "extractArchive" && step.ArchiveOpenMode != null)
            return new {step.Id, step.Op, Inputs = step.GetInputReferences(), step.Selector, step.ExtractionLayout,
                step.ArchiveOpenMode, step.ArchiveMemberSelector, Password = step.Password ?? defaultPassword};
        if (step.Op == "extractArchive" && step.ZipPasswordCodePage != null)
            return new {step.Id, step.Op, Inputs = step.GetInputReferences(), step.Selector, step.ExtractionLayout,
                step.ZipFileNameCodePage, step.ZipPasswordCodePage, Password = step.Password ?? defaultPassword};
        if (step.Op == "extractArchive" && step.ZipFileNameCodePage != null)
            return new {step.Id, step.Op, Inputs = step.GetInputReferences(), step.Selector, step.ExtractionLayout,
                step.ZipFileNameCodePage, Password = step.Password ?? defaultPassword};
        if (step.Op == "extractArchive" && step.ExtractionLayout != null)
            return new {step.Id, step.Op, Inputs = step.GetInputReferences(), step.Selector, step.ExtractionLayout, Password = step.Password ?? defaultPassword};
        // Do not add null fields to the old serialized shape: that would abandon pinned work.
        return step.Inputs == null ? (object)new
            {step.Id, step.Op, step.Input, step.Selector, step.Extension, step.TargetName, step.TargetDirectory,
                Password = step.Password ?? (step.Op == "extractArchive" ? defaultPassword : null)} : new
            {step.Id, step.Op, step.Inputs, step.Selector, step.Extension, step.TargetName, step.TargetDirectory,
                Password = step.Password ?? (step.Op == "extractArchive" ? defaultPassword : null)};
    }

    public static void Validate(FileArtifactProcessingConfig config)
    {
        if (!FileExtractionLayouts.Valid(config.ExtractionLayout) || config.ReusePolicy is not ("reuseVerified" or "force") ||
            config.ArchivePolicy is not ("instructions" or "autoDetect" or "keep") || config.Recursive == null)
            throw new ArgumentException("Choose a valid file-processing policy and recursive limits.");
        RecursiveArchiveExecutor.ValidateOptions(config.Recursive);
        if (config.StoragePolicy != null) ArtifactStoragePolicies.Validate(config.StoragePolicy);
        if (config.WorkspaceDirectoryScope != null) FileProcessingWorkspaceResolver.ValidateDirectoryScope(config.WorkspaceDirectoryScope);
        if (!string.IsNullOrWhiteSpace(config.WorkspaceRootDirectory) && !Path.IsPathFullyQualified(config.WorkspaceRootDirectory))
            throw new ArgumentException("Choose an absolute processing workspace directory.");
    }

    // Resolving a reusable result is a read path. Its content is still fully verified,
    // but unrelated cleanup must not hold up the next workflow node. The durable
    // lifecycle worker revisits any cleanup skipped after the original completion.
    public Task<ArtifactRecord?> ResolveAsync(string intent, CancellationToken ct) =>
        store.ResolveAsync(intent, ct);

    async Task<IncompleteArchiveGroup?> IFileArtifactProcessor.InspectIncompleteGroupAsync(string intent,
        ArtifactRecord input, FileProcessingPlan plan, string? defaultPassword, FileArtifactProcessingConfig config,
        CancellationToken ct)
    {
        if (config.ArchiveGroupOwner is not {} owner || input.State != ArtifactLocationState.Available) return null;
        plan = FileExtractionLayouts.Apply(WithDefaultPasswords(plan, defaultPassword), config.ExtractionLayout);
        var key = OperationKey(intent, input, plan, defaultPassword, config);
        var operation = await store.GetOperationAsync(key, ct);
        if (operation is not {State: ArtifactOperationState.Running, Attempt: > 0, Kind: "files.process"} ||
            operation.InputFingerprint != input.Fingerprint || operation.IntentKey != intent) return null;
        var locations = new FileProcessingWorkspaceResolver(appData(), storage);
        var checkpoint = await locations.ReadCheckpointAsync(CheckpointKey(input, plan), key, operation.Attempt,
            config.WorkspaceDirectoryScope?.Directory ?? config.WorkspaceRootDirectory ?? config.StoragePolicy?.ProcessingWorkspaceDirectory, ct);
        if (checkpoint == null) return null;
        var directory = FileProcessingPlanExecutor.CheckpointInputDirectory(checkpoint, plan, input, locations.IdentityPath);
        return await FileProcessingPlanExecutor.InspectPendingGroupAsync(directory, input,
            new(owner, input.Id, input.Fingerprint, key, operation.Attempt, null), ct);
    }

    Task<FileArtifactCheckpointContinuation?> IFileArtifactProcessor.ReadCheckpointContinuationAsync(string intent,
        ArtifactRecord input, string planJson, string? defaultPassword, FileArtifactProcessingConfig config,
        ArtifactRecord previous, string previousStep, CancellationToken ct) =>
        ReadCheckpointContinuationAsync(intent, input, planJson, defaultPassword, config, previous, previousStep, ct);

    Task<FileArtifactProcessingResult> IFileArtifactProcessor.ExecuteAsync(string intent, ArtifactRecord input,
        string planJson, string? defaultPassword, FileArtifactProcessingConfig config, string consumerKey,
        RecursiveArchivePasswordAnswer? answer, string? planPasswordAnswer,
        Action<int>? progress, CancellationToken ct, bool replan, string? stopAfterStepId,
        FileArtifactProcessingRecovery? recovery, FileArtifactCheckpointContinuation? checkpointContinuation,
        Func<string, CancellationToken, Task<IReadOnlyList<string>>>? passwordEvidence, Func<string, Task>? reportStage,
        string? originalDownloadPassword) =>
        ExecuteAsync(intent, input, planJson, defaultPassword, config, consumerKey, answer, planPasswordAnswer,
            progress, ct, replan, stopAfterStepId, recovery, checkpointContinuation: checkpointContinuation,
            passwordEvidence: passwordEvidence, reportStage: reportStage, originalDownloadPassword: originalDownloadPassword);

    private async Task<ArtifactRecord> PinVerifiedProcessingInputAsync(ArtifactRecord input, ArtifactOperationDbModel operation,
        string ownerId, CancellationToken ct)
    {
        var consumer = "files.process:" + operation.Id + ":" + operation.Attempt + ":input:" + ownerId;
        if (!await store.PinConsumerAsync(input.OperationId, consumer, "workflow", ownerId, ct))
            throw new FileProcessingInputChangedException("The immutable input cannot be pinned to the active caller.");
        var verified = await store.ResolveByIdAsync(input.Id, ct);
        if (verified == null || verified.OperationId != input.OperationId || verified.Fingerprint != input.Fingerprint ||
            verified.RootDirectory != input.RootDirectory || !FileDispositionProofs.SameManifest(input.Manifest, verified.Manifest) ||
            !verified.Files.ToHashSet(ArtifactManifestFiles.PathComparer).SetEquals(input.Files))
            throw new FileProcessingInputChangedException("The immutable input is missing or changed; retain it for review.");
        return verified;
    }

    // Only a completed result in its producer's registered, installation-owned workspace is
    // eligible. Stage views and user-managed directories keep their independent snapshots.
    private async Task<bool> CanBorrowProcessedResultAsync(ArtifactRecord input, string workspace,
        FileProcessingWorkspaceResolver locations, CancellationToken ct)
    {
        if (Path.GetFileName(input.RootDirectory) != "result" ||
            Bakabase.InsideWorld.Business.Components.ResourceMove.ResourceMoveFileSystem.AreOnSameFileSystem(input.RootDirectory, workspace) != true)
            return false;
        var producer = await store.GetOperationAsync(input.OperationId, ct);
        if (producer is not {Kind: "files.process", State: ArtifactOperationState.Completed, Attempt: > 0} ||
            producer.OutputArtifactId != input.Id || input.MetadataJson == null) return false;
        try
        {
            var metadata = JsonSerializer.Deserialize<FileArtifactProcessingMetadata>(input.MetadataJson, Json);
            if (metadata == null || metadata.StageCommit != null || metadata.StoragePolicy is {Materialization: "copy"} or
                {CleanupIntermediates: false} or {SourceCleanup: "keep" or "quarantine"}) return false;
            var producerPlan = JsonSerializer.Deserialize<FileProcessingPlan>(metadata.PlanJson, Json);
            if (producerPlan == null || producerPlan.Steps.Any(step => step.StoragePolicy is {Materialization: "copy"} or
                {CleanupIntermediates: false} or {SourceCleanup: "keep" or "quarantine"})) return false;
            FileProcessingWorkspaceResolver.Location? location;
            if (metadata.CompletedThroughStepId != null)
            {
                if (producerPlan.Steps.LastOrDefault()?.Id != metadata.CompletedThroughStepId) return false;
                location = await locations.InspectCompletedCheckpointAsync(CheckpointKey(producer.InputFingerprint, producerPlan),
                    producer.Id, producer.Attempt, metadata.WorkspaceDirectoryScope?.Directory ?? metadata.StoragePolicy?.ProcessingWorkspaceDirectory, ct);
            }
            else location = await locations.InspectExistingAsync("operations", producer.Id, producer.Attempt, ct);
            return location != null && ArtifactCleanupPathReferences.StrictlyUnder(input.RootDirectory, location.Directory) &&
                await locations.HasOwnedLocationAsync(location, ct);
        }
        catch (Exception error) when (error is JsonException or IOException or FileProcessingCheckpointPlacementException)
        { return false; }
    }

    private async Task<ArtifactManifest> ReadRegisteredSnapshotManifestAsync(ArtifactRecord input, CancellationToken ct)
    {
        var source = await store.GetArtifactAsync(input.Id, ct);
        if (source is not {State: ArtifactLocationState.Available} || source.OperationId != input.OperationId ||
            source.Kind != input.Kind || source.Fingerprint != input.Fingerprint || source.Manifest.Fingerprint != input.Fingerprint ||
            !ArtifactManifestFiles.PathComparer.Equals(source.RootDirectory, input.RootDirectory) ||
            !FileDispositionProofs.SameManifest(source.Manifest, input.Manifest) ||
            !source.Files.ToHashSet(ArtifactManifestFiles.PathComparer).SetEquals(input.Files))
            throw new FileProcessingInputChangedException("The registered processing input changed before its private snapshot was created.");
        return source.Manifest;
    }

    private async Task ReclaimCachedAsync(ArtifactRecord artifact, CancellationToken ct)
    {
        if (maintenance != null) await maintenance.ReclaimAsync([artifact.Id], ct);
    }

    private static string? CommonExtractionLayout(FileProcessingPlanResult result, string? fallback)
    {
        var layouts = result.Files.Select(path => result.ExtractionLayouts?.GetValueOrDefault(path) ?? fallback).Distinct().Take(2).ToArray();
        return layouts.Length == 1 ? layouts[0] : null;
    }

    internal static FileProcessingPlan WithDefaultPasswords(FileProcessingPlan plan, string? password) =>
        plan with {Steps = plan.Steps.Select(step => step.Op == "extractArchive" && step.Password == null
            ? step with {Password = password} : step).ToList()};

    internal static string OperationKey(string intent, ArtifactRecord input, FileProcessingPlan resolvedPlan,
        string? defaultPassword, FileArtifactProcessingConfig config) => OperationKey(intent, input.Fingerprint, resolvedPlan, defaultPassword, config);

    internal static string OperationKey(string intent, string inputFingerprint, FileProcessingPlan resolvedPlan,
        string? defaultPassword, FileArtifactProcessingConfig config) => Hash(intent + "\n" + inputFingerprint + "\n" +
        CreateIntent("resolved-recipe", JsonSerializer.Serialize(resolvedPlan, Json), defaultPassword, config,
            null, ResolvedRecipeSchemaVersion));

    internal static string CheckpointKey(ArtifactRecord input, FileProcessingPlan resolvedPlan) =>
        CheckpointKey(input.Fingerprint, resolvedPlan);

    internal static string CheckpointKey(string inputFingerprint, FileProcessingPlan resolvedPlan) =>
        Hash(inputFingerprint + "\n" + FileProcessingPlanExecutor.IdentityJson(resolvedPlan));

    internal async Task<FileArtifactCheckpointContinuation?> ReadCheckpointContinuationAsync(string intent,
        ArtifactRecord input, string planJson, string? defaultPassword, FileArtifactProcessingConfig config,
        ArtifactRecord previous, string previousStep, CancellationToken ct)
    {
        if (previous.Kind != "processedFiles" || !previous.Managed || previous.State != ArtifactLocationState.Available ||
            !previous.ParentArtifactIds.Contains(input.Id) || previous.MetadataJson == null) return null;
        var plan = FileExtractionLayouts.Apply(WithDefaultPasswords(JsonSerializer.Deserialize<FileProcessingPlan>(planJson, Json)!, defaultPassword), config.ExtractionLayout);
        var metadata = JsonSerializer.Deserialize<FileArtifactProcessingMetadata>(previous.MetadataJson, Json);
        if (metadata?.InputArtifactId != input.Id || metadata.CompletedThroughStepId != previousStep ||
            FileProcessingPlanExecutor.IdentityJson(JsonSerializer.Deserialize<FileProcessingPlan>(metadata.PlanJson, Json)!) !=
            FileProcessingPlanExecutor.IdentityJson(plan)) return null;
        var priorOperation = await store.GetOperationAsync(previous.OperationId, ct);
        if (priorOperation is not {State: ArtifactOperationState.Completed, Kind: "files.process"} ||
            priorOperation.OutputArtifactId != previous.Id || priorOperation.InputFingerprint != input.Fingerprint) return null;
        var key = OperationKey(intent, input, plan, defaultPassword, config);
        var current = await store.GetOperationAsync(key, ct);
        var attempt = current == null ? 1 : current.State == ArtifactOperationState.Planned ? current.Attempt + 1 : current.Attempt;
        if (attempt != priorOperation.Attempt || current != null && current.State is not
                (ArtifactOperationState.Planned or ArtifactOperationState.Running)) return null;
        var locations = new FileProcessingWorkspaceResolver(appData(), storage);
        var checkpoint = await locations.ReadCheckpointAsync(CheckpointKey(input, plan), priorOperation.Id, attempt,
            config.WorkspaceDirectoryScope?.Directory ?? config.WorkspaceRootDirectory ?? config.StoragePolicy?.ProcessingWorkspaceDirectory, ct);
        if (checkpoint == null) return null;
        var proof = await FileProcessingPlanExecutor.ReadPinnedInputAsync(checkpoint, plan, input, previous,
            previousStep, locations.IdentityPath, ct);
        return proof == null ? null : new(key, attempt, checkpoint, proof, current?.State == ArtifactOperationState.Running);
    }

    public async Task<FileArtifactProcessingResult> ExecuteAsync(string intent, ArtifactRecord input,
        string planJson, string? defaultPassword, FileArtifactProcessingConfig config, string consumerKey,
        RecursiveArchivePasswordAnswer? answer, string? planPasswordAnswer,
        Action<int>? progress, CancellationToken ct, bool replan = false, string? stopAfterStepId = null)
        => await ExecuteAsync(intent, input, planJson, defaultPassword, config, consumerKey, answer,
            planPasswordAnswer, progress, ct, replan, stopAfterStepId, null);

    internal async Task<FileArtifactProcessingResult> ExecuteAsync(string intent, ArtifactRecord input,
        string planJson, string? defaultPassword, FileArtifactProcessingConfig config, string consumerKey,
        RecursiveArchivePasswordAnswer? answer, string? planPasswordAnswer,
        Action<int>? progress, CancellationToken ct, bool replan, string? stopAfterStepId,
        FileArtifactProcessingRecovery? recovery, bool onlyOuterPackaging = false,
        FileArtifactCheckpointContinuation? checkpointContinuation = null,
        Func<string, CancellationToken, Task<IReadOnlyList<string>>>? passwordEvidence = null,
        Func<string, Task>? reportStage = null, string? originalDownloadPassword = null)
    {
        FileArtifactProcessingResult CheckedResult(ArtifactRecord artifact)
        {
            if (config.RetainArchiveGroupToken is {} consent &&
                JsonSerializer.Deserialize<FileArtifactProcessingMetadata>(artifact.MetadataJson ?? "{}", Json)?.RetainedGroupTokens?.Contains(consent) != true)
                return new(null, "NeedsReconciliation", "This completed result has no receipt for the selected incomplete archive group.");
            return new(artifact);
        }
        Validate(config);
        if (recovery != null && (replan || answer != null || planPasswordAnswer != null || recovery.Attempt < 1))
            throw new ArgumentException("A storage continuation cannot change processing instructions or passwords.");
        var existing = replan || recovery != null ? null : await ResolveAsync(intent, ct);
        if (existing != null) return CheckedResult(existing);
        FileProcessingPlan plan;
        if (config.ArchivePolicy == "keep") plan = new() {Requirement = "notRequired"};
        else
        {
            try
            {
                plan = JsonSerializer.Deserialize<FileProcessingPlan>(planJson, Json) ?? new();
                FileProcessingPlanExecutor.Validate(plan);
            }
            catch (Exception error) when (config.ArchivePolicy == "autoDetect" && error is JsonException or InvalidOperationException)
            { plan = new() {Requirement = "notRequired"}; }
        }
        plan = FileExtractionLayouts.Apply(WithDefaultPasswords(plan, defaultPassword), config.ExtractionLayout);
        if (config.ResolveInitialPlan(plan) is not {} initialPlan)
            return FileArtifactProcessingResult.InstructionsRequired();
        plan = initialPlan;
        // New demands include the current processing semantics (including authorized signature
        // detection). Keep the resolved-plan schema stable so an older pinned demand still reaches
        // its original operation, attempt and archive/password checkpoints.
        var key = OperationKey(intent, input, plan, defaultPassword, config);
        if (checkpointContinuation != null && (checkpointContinuation.OperationKey != key || stopAfterStepId == null ||
                checkpointContinuation.Input.InputManifest.Fingerprint != input.Fingerprint))
            throw new InvalidDataException("The checkpoint continuation belongs to different processing inputs.");
        if (recovery != null && recovery.OperationId != key)
            return new(null, "NeedsReconciliation", "The processing operation changed after recovery was approved.");
        var beforeClaim = await store.GetOperationAsync(key, ct);
        var firstAttempt = beforeClaim == null || beforeClaim is {State: ArtifactOperationState.Planned, Attempt: 0};
        var request = new ArtifactOperationRequest(key, intent, "files.process", input.Fingerprint,
            // A competing claim between this read and the CAS must not turn a new
            // allocation into a takeover of an unknown, already-started workspace.
            AllowRetry: !firstAttempt && (checkpointContinuation == null || checkpointContinuation.ResumeExistingOperation));
        ArtifactOperationClaim claim;
        if (recovery is {RepairArchiveVolumes: true} && beforeClaim?.State == ArtifactOperationState.Failed)
        {
            if (beforeClaim.Error == null || !await SevenZipVolumeSet.CanRecoverAsync(beforeClaim.Error, plan, stopAfterStepId, input.Files, ct))
                return new(null, "NeedsReconciliation", "The failed archive or its registered volumes changed after recovery was approved.");
            claim = await store.ReopenFailedAttemptAsync(request, recovery.Attempt, beforeClaim.Error, consumerKey, LeaseDuration, ct);
        }
        else
            claim = recovery == null && checkpointContinuation?.ResumeExistingOperation != true
            ? await store.ClaimAsync(request, consumerKey, LeaseDuration, ct)
            : await store.ClaimSameAttemptAsync(request, recovery?.Attempt ?? checkpointContinuation!.Attempt, consumerKey, LeaseDuration, ct);
        if (claim.Artifact != null) return CheckedResult(claim.Artifact);
        if (claim.Status == ArtifactClaimStatus.StorageUnavailable)
            return new(null, "StorageUnavailable", "The stored result directory is unavailable. Reconnect its storage to reuse the completed files.");
        if (claim.Status != ArtifactClaimStatus.Acquired || claim.Lease == null)
            return new(null, claim.Status == ArtifactClaimStatus.Busy ? "WaitingShared" : "NeedsReconciliation",
                "Another operation owns these inputs. Resume to check its committed result.");
        var operation = await store.GetOperationAsync(key, ct) ?? throw new InvalidDataException("The claimed operation disappeared.");
        if (checkpointContinuation != null && operation.Attempt != checkpointContinuation.Attempt)
            return new(null, "NeedsReconciliation", "The processing attempt changed; its old checkpoint was not reused.");
        var locations = new FileProcessingWorkspaceResolver(appData(), storage);
        var lease = claim.Lease;
        using var workCancellation = CancellationTokenSource.CreateLinkedTokenSource(ct);
        using var heartbeatCancellation = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var heartbeat = RenewAsync(lease, workCancellation, heartbeatCancellation.Token);
        var operationCt = workCancellation.Token;
        ArtifactRegistration? registration = null;
        FileArtifactProcessingResult? waiting = null;
        try
        {
            var selectedRoot = config.WorkspaceRootDirectory ?? config.StoragePolicy?.ProcessingWorkspaceDirectory ?? workspaceRoot();
            var state = await locations.ResolveOperationAsync(key, operation.Attempt, input.RootDirectory, selectedRoot,
                config.WorkspaceDirectoryScope, claim.StartedNewAttempt,
                config.StoragePolicy, config.StorageTitle, operationCt);
            selectedRoot = (await locations.InspectExistingAsync("operations", key, operation.Attempt, operationCt))!.StorageRoot;
            var checkpointDirectory = stopAfterStepId == null ? Path.Combine(state, "plan") :
                await locations.ResolveCheckpointAsync(CheckpointKey(input, plan), key, operation.Attempt, input, plan,
                    config.WorkspaceDirectoryScope?.Directory ?? config.WorkspaceRootDirectory ?? config.StoragePolicy?.ProcessingWorkspaceDirectory, selectedRoot,
                    config.StoragePolicy, config.StorageTitle, operationCt, config.BoundInputDirectory);
            if (checkpointContinuation != null && !ArtifactManifestFiles.PathComparer.Equals(checkpointDirectory, checkpointContinuation.CheckpointRoot))
                throw new InvalidDataException("The pinned checkpoint location changed.");
            var borrowed = await BorrowOuterInputAsync(state, checkpointDirectory, operation, input, plan, config,
                onlyOuterPackaging, stopAfterStepId, operationCt);
            var selectedArchives = config.BorrowSelectedProcessedArchives && stopAfterStepId == null && checkpointContinuation == null &&
                ReadOnlyArchiveVolumes.SupportsProcessedSelection(plan, input, config.StoragePolicy) &&
                await CanBorrowProcessedResultAsync(input, state, locations, operationCt);
            var result = borrowed ?? await plans.ExecuteAsync(plan, input.RootDirectory, input.Files,
                checkpointDirectory, planPasswordAnswer, progress, operationCt, stopAfterStepId, locations.IdentityPath,
                config.StoragePolicy, config.StorageTitle,
                inputDirectories: ArtifactManifestFiles.ResolveDirectories(input.RootDirectory, input.Manifest),
                checkpointInput: checkpointContinuation?.Input, inputDirectoryProvenance: FileArtifactDeliveryLayout.ReadProvenance(input),
                passwordEvidence: passwordEvidence, reportStage: reportStage, originalDownloadPassword: originalDownloadPassword,
                resolveReadOnlyArchiveInput: checkpointContinuation == null && config.ArchivePolicy == "instructions" &&
                    (selectedArchives || ReadOnlyArchiveVolumes.Supports(plan, input, config.StoragePolicy)) &&
                    StorageWriteScope.Current is {OwnerKind: "workflow", OwnerId: {} workflow} &&
                    int.TryParse(workflow, out var runId) && runId > 0
                    ? token => PinVerifiedProcessingInputAsync(input, operation, workflow, token) : null,
                snapshotSource: new(input, token => ReadRegisteredSnapshotManifestAsync(input, token))
                {
                    BorrowSelectedProcessedArchives = selectedArchives,
                    ArchiveGroup = config.ArchiveGroupOwner is {} owner
                        ? new(owner, input.Id, input.Fingerprint, key, operation.Attempt, config.RetainArchiveGroupToken) : null
                });
            if (!result.Completed)
                waiting = new(null, result.NeedsPassword ? "PasswordUnknown" : result.FailureCode ?? "ExtractionPlanUnknown",
                    result.Message, TriedPasswords: result.TriedPasswords, RequiredComponentId: result.RequiredComponentId,
                    PathConflict: result.PathConflict) {IncompleteArchiveGroup = result.IncompleteArchiveGroup};
            else
            {
                var files = result.Files;
                var root = result.OutputDirectory!;
                var lineage = result.FileLineage;
                var directories = result.Directories;
                var directoryProvenance = result.DirectoryProvenance;
                var archiveCount = 0;
                var detectInitial = config.ArchivePolicy == "autoDetect";
                var candidates = detectInitial ? files : result.ExtractedFiles ?? [];
                if (stopAfterStepId == null && config.ArchivePolicy != "keep" && (detectInitial || config.Recursive.Enabled && candidates.Count > 0))
                {
                    var nested = await recursive.ExecuteAsync(root, files, Path.Combine(state, "recursive"),
                        config.Recursive, answer, defaultPassword, progress, operationCt, candidates,
                        onlyInitialLayer: !config.Recursive.Enabled, identityPath: locations.IdentityPath,
                        storagePolicy: result.StoragePolicy, storageTitle: result.StorageTitle, inputPolicies: result.FilePolicies,
                        onlyOuterPackaging: onlyOuterPackaging, inputDirectories: directories, inputDirectoryProvenance: directoryProvenance,
                        extractionLayout: config.ExtractionLayout, inputLayouts: result.ExtractionLayouts, passwordEvidence: passwordEvidence, reportStage: reportStage);
                    if (!nested.Completed)
                        waiting = new(null, nested.WaitReason, nested.Message, nested.ArchiveId, nested.TriedPasswords,
                            PathConflict: nested.PathConflict);
                    else
                    {
                        files = nested.Files; root = nested.OutputDirectory!; archiveCount = nested.Lineage?.Count ?? 0;
                        directories = nested.Directories;
                        directoryProvenance = nested.DirectoryProvenance;
                        lineage = FileDispositionProofs.Compose(lineage, nested.FileLineage);
                        if (borrowed != null && lineage == null)
                            throw new FileProcessingInputChangedException("The recursive result does not match its immutable input evidence.");
                    }
                }
                if (waiting == null)
                {
                    if (reportStage != null) await reportStage("finalizingFiles");
                    // The measured execution trace is separate from the instructions. Missing
                    // legacy traces remain unknown, rather than reconstructing destructive
                    // authority from a plan, prompt, filename, or an artifact ancestor link.
                    var outputManifest = lineage?.OutputManifest;
                    if (outputManifest != null && !ArtifactManifestFiles.ResolveFiles(root, outputManifest)
                            .ToHashSet(ArtifactManifestFiles.PathComparer).SetEquals(files))
                        throw new InvalidDataException("The execution proof does not describe the complete processing result.");
                    var disposition = outputManifest == null ? null : FileDispositionProofs.Create(input, outputManifest, lineage);
                    registration = new(root, files, "processedFiles", true, [input.Id],
                        JsonSerializer.Serialize(new FileArtifactProcessingMetadata(input.Id,
                            JsonSerializer.Serialize(plan, Json), input.MetadataJson, archiveCount, stopAfterStepId,
                            result.StoragePolicy, disposition, directoryProvenance, CommonExtractionLayout(result, config.ExtractionLayout),
                            config.WorkspaceDirectoryScope) {RetainedArchiveFiles = result.RetainedArchiveFiles is {Count: > 0} kept ? kept : null,
                                RetainedGroupTokens = result.RetainedGroupTokens}, Json), ExpectedManifestFingerprint: outputManifest?.Fingerprint,
                        Directories: directories);
                }
            }
            if (registration != null)
            {
                var published = await store.CompleteAsync(lease, registration, operationCt);
                heartbeatCancellation.Cancel();
                await StopHeartbeat(heartbeat);
                // The immutable registered result survives cleanup. Only completed executor-private
                // snapshots are retired; waits, result-step artifacts and original downloads remain.
                try
                {
                    await ReclaimCachedAsync(published, operationCt);
                }
                catch (Exception cleanupError) when (cleanupError is IOException or InvalidDataException or UnauthorizedAccessException or OperationCanceledException)
                { /* A committed result must not be reported as failed because best-effort reclamation failed. */ }
                return new(published);
            }
        }
        catch (FileProcessingInputChangedException error)
        {
            heartbeatCancellation.Cancel();
            await StopHeartbeat(heartbeat);
            return new(null, "NeedsReconciliation", error.Message);
        }
        catch (FileProcessingCheckpointPlacementException error)
        {
            heartbeatCancellation.Cancel();
            await StopHeartbeat(heartbeat);
            return new(null, "NeedsReconciliation", error.Message);
        }
        catch (InvalidDataException error) when (checkpointContinuation != null)
        {
            heartbeatCancellation.Cancel();
            await StopHeartbeat(heartbeat);
            // Invalid checkpoint bytes are not a disconnected volume. Keep this exact
            // generation pinned for review; never refill it or start a fresh attempt.
            return new(null, "NeedsReconciliation", error.Message);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            heartbeatCancellation.Cancel();
            await StopHeartbeat(heartbeat);
            // Storage disconnects must not turn a durable attempt into a new generation. The
            // current locator and successful earlier steps stay pinned for reconnect/resume.
            return new(null, "StorageUnavailable", error.Message);
        }
        catch (Exception error)
        {
            heartbeatCancellation.Cancel();
            await StopHeartbeat(heartbeat);
            // Cancellation leaves a durable restartable checkpoint. An expired running lease
            // can be taken over without discarding completed archive groups.
            if (error is not OperationCanceledException && !workCancellation.IsCancellationRequested)
                await store.FailAsync(lease, error.Message, ct: CancellationToken.None);
            throw;
        }
        heartbeatCancellation.Cancel();
        await StopHeartbeat(heartbeat);
        if (waiting != null) return waiting;
        throw new InvalidDataException("Processing produced neither a committed result nor a suspension.");
    }

    private async Task RenewAsync(ArtifactOperationLease lease, CancellationTokenSource workCancellation, CancellationToken ct)
    {
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
            while (await timer.WaitForNextTickAsync(ct))
            {
                await using var scope = scopes.CreateAsyncScope();
                var leases = scope.ServiceProvider.GetRequiredService<IArtifactOperationStore>();
                if (await leases.RenewAsync(lease, LeaseDuration, ct)) continue;
                if ((await leases.GetOperationAsync(lease.OperationId, ct))?.State == ArtifactOperationState.Completed) return;
                throw new InvalidOperationException("The processing operation lease was lost.");
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch { workCancellation.Cancel(); throw; }
    }

    private static async Task StopHeartbeat(Task heartbeat)
    {
        try { await heartbeat; } catch (OperationCanceledException) { }
    }

    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
