using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.Infrastructures.Components.App;
using Bakabase.InsideWorld.Business.Components.Compression;
using Bakabase.Modules.PostParser.Models.Domain;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Service.Components.FileProcessing;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Service.Components.CloudDownloads;

public sealed record CloudFileProcessingPrompt(string TargetId, string? Title, string Reason,
    string Message, string ExtractionPlanJson, string? ArchiveName = null, IReadOnlyList<string>? Tried = null,
    string? ArchiveId = null, RecursiveArchiveOptions? RecursiveOptions = null,
    IReadOnlyList<string>? AllowedActions = null, string? RequiredComponentId = null, FileProcessingPathConflict? PathConflict = null);

public sealed record CloudFileProcessingSignal
{
    public string TargetId { get; init; } = "";
    public string? Action { get; init; }
    public string? ArtifactId { get; init; }
    public string? Fingerprint { get; init; }
    public string? StepId { get; init; }
    public string? ArchiveGroupToken { get; init; }
    public string? Password { get; init; }
    public string? ExtractionPlanJson { get; init; }
    public string? ArchiveId { get; init; }
    public RecursiveArchiveOptions? RecursiveOptions { get; init; }
    public string? ArchivePolicy { get; init; }
    /// <summary>
    /// Optional human context for the bounded adaptive file-processing assessment.
    /// It is evidence only; it never grants the model permission to mutate files.
    /// </summary>
    public string? UserContext { get; init; }
    /// <summary>Server-prepared recovery fence, carried only by the opaque workflow answer.</summary>
    public FileArtifactProcessingRecovery? StorageRecovery { get; init; }
}

/// <summary>Processes each selected target's exact manifest, preserving downloads and sibling results.</summary>
public sealed class ProcessCloudFilesActivity : IResumableWorkflowActivity, IWorkflowArtifactDemandActivity, IFileProcessingPlanConsumer
{
    private readonly Func<WorkflowExecutionContext, string> _appDataDirectory;
    public ProcessCloudFilesActivity() : this(ctx => ctx.Services.GetRequiredService<AppService>().AppDataDirectory) { }
    internal ProcessCloudFilesActivity(Func<WorkflowExecutionContext, string> appDataDirectory) =>
        _appDataDirectory = appDataDirectory;

    public string Kind => "download.processFiles";
    public string DisplayName => "Process downloaded files";
    public string Description => "Apply each selected source's saved file-processing instructions to its downloaded files. Keep originals and wait for missing instructions or archive passwords. No resource is created.";
    public string DescriptionKey => "workflow.activity.cloudProcessFiles.description";
    public string Group => "downloader";
    public WorkflowActivityCategory Category => WorkflowActivityCategory.Action;
    public IReadOnlyList<string> AcceptedInputItemTypes => [CloudWorkflow.ItemType];
    public string GetArchivePolicy(string configJson) =>
        (JsonSerializer.Deserialize<FileArtifactProcessingConfig>(configJson, WorkflowJson.Options) ?? new()).ArchivePolicy;

    public async Task<IReadOnlyList<WorkflowValidationIssue>> ValidateConfigAsync(WorkflowValidationContext context, CancellationToken ct)
    {
        try
        {
            var config = JsonSerializer.Deserialize<FileArtifactProcessingConfig>(context.ConfigJson, WorkflowJson.Options) ?? new();
            Validate(config);
            if (config.ArchivePolicy != "instructions" || context.Payload is not DownloadWorkset workset) return [];
            var plans = workset.Targets.Where(t => !t.Excluded).Select(t => t.ProcessingPlanJson ?? t.SelectedSource?.ExtractionPlanJson)
                .Where(j => !string.IsNullOrWhiteSpace(j)).Select(j => JsonSerializer.Deserialize<FileProcessingPlan>(j!, WorkflowJson.Options));
            return await FileProcessingDependencyPreflight.InspectWorkflowAsync(context, plans, ct);
        }
        catch (Exception error) when (error is JsonException or ArgumentException or InvalidOperationException)
        {return [new() {Code = "fileProcessingConfigInvalid", Message = "Check the file-processing plan and settings."}];}
    }

    private static void Validate(FileArtifactProcessingConfig config) => FileArtifactProcessor.Validate(config);

    public async Task<object> TrySatisfyAsync(WorkflowExecutionContext context, object item, string configJson, CancellationToken ct)
    {
        var config = JsonSerializer.Deserialize<FileArtifactProcessingConfig>(configJson, WorkflowJson.Options) ?? new();
        Validate(config);
        if (config.ReusePolicy == "force") return item;
        var processor = context.Services.GetService<FileArtifactProcessor>();
        if (processor == null || item is not DownloadWorkset workset) return item;
        var targets = new List<CloudDownloadTarget>();
        foreach (var target in workset.Targets)
        {
            if (target.Excluded || target.SelectedSource == null || target.AcquisitionNonce != null) {targets.Add(target); continue;}
            var source = target.SelectedSource;
            var targetConfig = config with {ArchivePolicy = target.ProcessingArchivePolicy ?? config.ArchivePolicy};
            var intent = FileArtifactProcessor.Intent(DownloadArtifactCoordinator.ArtifactIntent(target, workset.OutputDirectory, context.Services.GetService<FileProcessingWorkspaceResolver>()),
                target.ProcessingPlanJson ?? source.ExtractionPlanJson, source.ArchivePassword, targetConfig);
            var artifact = await processor.ResolveAsync(intent, ct);
            artifact ??= await PublishFileArtifactsActivity.ResolveFollowingPublicationAsync(context, workset, target, intent, ct);
            if (artifact != null) await BindAsync(context, target, artifact, ct);
            targets.Add(artifact == null ? target with {ProcessingIntentKey = intent} : ApplyArtifact(target, artifact, intent));
        }
        return workset with {Targets = targets};
    }

    private static CloudDownloadTarget ApplyArtifact(CloudDownloadTarget target, ArtifactRecord artifact, string intent)
    {
        var publication = artifact.Kind == "publishedFiles" && artifact.MetadataJson != null
            ? JsonSerializer.Deserialize<FileArtifactPublicationMetadata>(artifact.MetadataJson, WorkflowJson.Options) : null;
        var processingMetadata = publication?.SourceMetadataJson ?? artifact.MetadataJson;
        var metadata = processingMetadata == null ? null :
            JsonSerializer.Deserialize<FileArtifactProcessingMetadata>(processingMetadata, WorkflowJson.Options);
        var input = metadata?.InputMetadataJson == null ? null :
            JsonSerializer.Deserialize<DownloadArtifactMetadata>(metadata.InputMetadataJson, WorkflowJson.Options);
        return target with {ProcessingIntentKey = intent, ProcessingOperationId = publication?.SourceOperationId ?? artifact.OperationId,
            PublicationOperationId = publication != null ? artifact.OperationId : target.PublicationOperationId,
            PublishedArtifactId = publication != null ? artifact.Id : target.PublishedArtifactId,
            PublishedDirectory = publication != null ? artifact.RootDirectory : target.PublishedDirectory,
            PublishedFiles = publication != null ? artifact.Files.ToList() : target.PublishedFiles,
            ProcessedArtifactId = artifact.Id, ProcessedDirectory = artifact.RootDirectory,
            ProcessedFiles = artifact.Files.ToList(), DownloadArtifactId = metadata?.InputArtifactId ?? target.DownloadArtifactId,
            DownloadTaskId = input?.DownloadTaskId ?? target.DownloadTaskId, TransportTaskId = input?.TransportTaskId ?? target.TransportTaskId,
            ExecutionStatus = "reused", ProcessingWaitReason = null, Problem = null};
    }

    public Task<WorkflowItemOutcome> ProcessItemAsync(WorkflowExecutionContext ctx, object item, CancellationToken ct) =>
        ProcessAsync(ctx, (DownloadWorkset)item, null, ct);

    public Task<WorkflowItemOutcome> ResumeAsync(WorkflowExecutionContext ctx, object item, string signalJson,
        CancellationToken ct)
    {
        CloudFileProcessingSignal signal;
        try
        {
            signal = JsonSerializer.Deserialize<CloudFileProcessingSignal>(signalJson, WorkflowJson.Options)
                     ?? throw new ArgumentException("Provide a file-processing answer for the waiting target.");
        }
        catch (JsonException error) { throw new ArgumentException("The file-processing answer is not valid JSON.", error); }
        var workset = (DownloadWorkset)item;
        var waiting = workset.Targets.Where(t => !t.Excluded && t.ProcessingWaitReason != null).ToList();
        if (waiting.Count != 1 || signal.TargetId != waiting[0].Id || waiting[0].ProcessedDirectory != null)
            throw new ArgumentException("The file-processing answer does not belong to the waiting target.");
        if (signal.Password?.Length > 2048 || signal.ExtractionPlanJson?.Length > 131072 || signal.UserContext?.Length > 16384)
            throw new ArgumentException("The password or file-processing plan is too long.");
        FileProcessingResumeActions.Validate(ctx, waiting[0], signal);
        return ProcessAsync(ctx, workset, signal, ct);
    }

    private async Task<WorkflowItemOutcome> ProcessAsync(WorkflowExecutionContext ctx, DownloadWorkset workset,
        CloudFileProcessingSignal? signal, CancellationToken ct)
    {
        using var writeScope = StorageWriteScope.Enter(new("workflow", ctx.RunId.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        var storage = ctx.Services.GetRequiredService<IUserStoragePolicy>();
        storage.EnsurePathAllowed(workset.OutputDirectory);
        if (!Path.IsPathFullyQualified(workset.OutputDirectory))
            throw new InvalidDataException("The download directory must be an absolute path.");
        if (workset.Targets.Select(t => t.Id).Distinct(StringComparer.Ordinal).Count() != workset.Targets.Count ||
            workset.Targets.Any(t => string.IsNullOrWhiteSpace(t.Id)))
            throw new InvalidDataException("Every downloaded target needs a unique identity.");
        if (ctx.Services.GetService<FileArtifactProcessor>() is { } processor)
            return await ProcessDurableAsync(ctx, workset, signal, processor, ct);
        var appData = Path.GetFullPath(_appDataDirectory(ctx));
        var executor = ctx.Services.GetService<FileProcessingPlanExecutor>() ??
            new FileProcessingPlanExecutor(ctx.Services.GetRequiredService<IArchiveExtractionService>());
        var targets = workset.Targets.ToList();
        for (var index = 0; index < targets.Count; index++)
        {
            ct.ThrowIfCancellationRequested();
            var target = targets[index];
            if (target.Excluded) continue;
            var source = target.SelectedSource ?? throw new InvalidOperationException("Choose a download source before processing its files.");
            CloudDownloadManifest.Validate(workset.OutputDirectory, target.Files);
            foreach (var file in target.Files) storage.EnsurePathAllowed(file);
            if (target.DownloadDirectory != null && !Path.IsPathFullyQualified(target.DownloadDirectory))
                throw new InvalidDataException("The target's download directory must be an absolute path.");
            // New downloads retain their root so a single nested folder keeps its relative layout.
            // Older snapshots only contain exact files; derive a root without scanning siblings.
            var root = target.DownloadDirectory == null ? CommonDirectory(target.Files, workset.OutputDirectory)
                : FileProcessingFiles.Within(target.DownloadDirectory, workset.OutputDirectory);
            storage.EnsurePathAllowed(root);
            CloudDownloadManifest.Validate(root, target.Files);
            var stateDirectory = Path.Combine(appData, "file-processing", "cloud",
                ctx.RunId.ToString(CultureInfo.InvariantCulture), CloudOperationService.Key(target.Id));
            EnsureInternalDirectory(stateDirectory, appData);
            if (target.ProcessedDirectory != null)
            {
                FileProcessingFiles.Within(target.ProcessedDirectory, stateDirectory);
                CloudDownloadManifest.Validate(target.ProcessedDirectory, target.ProcessedFiles);
                continue;
            }

            var answer = signal?.TargetId == target.Id ? signal : null;
            var planJson = answer?.ExtractionPlanJson ?? target.ProcessingPlanJson ?? source.ExtractionPlanJson ??
                JsonSerializer.Serialize(new PostExtractionPlan(), WorkflowJson.Options);
            FileProcessingPlan plan;
            try
            {
                plan = FileExtractionLayouts.Apply(JsonSerializer.Deserialize<PostExtractionPlan>(planJson, WorkflowJson.Options)
                       ?? throw new InvalidOperationException("Provide file-processing instructions for this download."),
                    ctx.GetConfig<FileArtifactProcessingConfig>()?.ExtractionLayout);
                FileProcessingPlanExecutor.Validate(plan);
                // An instruction-specific password takes precedence over the source's general archive password.
                plan = plan with {Steps = plan.Steps.Select(step => step.Op == "extractArchive" && step.Password == null
                    ? step with {Password = source.ArchivePassword} : step).ToList()};
                FileProcessingPlanExecutor.Validate(plan);
                planJson = JsonSerializer.Serialize(plan, WorkflowJson.Options);
            }
            catch (Exception error) when (error is JsonException or InvalidOperationException)
            {
                targets[index] = target with {ProcessingPlanJson = planJson};
                return Wait(workset, targets, index, "ExtractionPlanUnknown", error.Message, planJson);
            }
            target = target with {ProcessingPlanJson = planJson, ProcessingWaitReason = null, Problem = null};
            targets[index] = target;
            await ctx.ReportStage("processingFiles");
            var result = await executor.ExecuteAsync(plan, root, target.Files, stateDirectory, answer?.Password,
                percentage => ctx.ReportProgress(percentage, target.Title).GetAwaiter().GetResult(), ct);
            if (!result.Completed)
                return Wait(workset, targets, index, result.NeedsPassword ? "PasswordUnknown" : result.FailureCode ?? "ExtractionPlanUnknown",
                    result.Message ?? "File-processing input is required.", planJson, tried: result.TriedPasswords,
                    requiredComponentId: result.RequiredComponentId, pathConflict: result.PathConflict);
            if (result.OutputDirectory == null)
                throw new InvalidDataException("The completed processing operation has no output directory.");
            FileProcessingFiles.Within(result.OutputDirectory, stateDirectory);
            CloudDownloadManifest.Validate(result.OutputDirectory, result.Files);
            targets[index] = target with {ProcessedDirectory = result.OutputDirectory,
                ProcessedFiles = result.Files.ToList(), ProcessingWaitReason = null, Problem = null};
        }
        return WorkflowItemOutcome.ReplaceWith(workset with {Targets = targets});
    }

    private async Task<WorkflowItemOutcome> ProcessDurableAsync(WorkflowExecutionContext ctx, DownloadWorkset workset,
        CloudFileProcessingSignal? signal, FileArtifactProcessor processor, CancellationToken ct)
    {
        var config = ctx.GetConfig<FileArtifactProcessingConfig>() ?? new();
        Validate(config);
        var store = ctx.Services.GetRequiredService<IArtifactOperationStore>();
        var coordinator = ctx.Services.GetRequiredService<DownloadArtifactCoordinator>();
        var storage = ctx.Services.GetRequiredService<IUserStoragePolicy>();
        var targets = workset.Targets.ToList();
        for (var index = 0; index < targets.Count; index++)
        {
            var target = targets[index];
            if (target.Excluded) continue;
            var source = target.SelectedSource ?? throw new InvalidOperationException("Choose a source before processing files.");
            var answer = signal?.TargetId == target.Id ? signal : null;
            var recipe = target.ProcessingPlanJson ?? source.ExtractionPlanJson ?? "{}";
            var targetPolicy = answer?.ArchivePolicy ?? target.ProcessingArchivePolicy ?? config.ArchivePolicy;
            var policyConfig = config with {ArchivePolicy = targetPolicy,
                StoragePolicy = config.StoragePolicy ?? target.StoragePolicy, StorageTitle = target.Title ?? config.StorageTitle};
            Validate(policyConfig);
            var intent = answer?.ArchivePolicy == null && target.ProcessingIntentKey?.StartsWith(FileArtifactProcessor.IntentVersion, StringComparison.Ordinal) == true
                ? target.ProcessingIntentKey : FileArtifactProcessor.Intent(
                DownloadArtifactCoordinator.ArtifactIntent(target, workset.OutputDirectory, ctx.Services.GetService<FileProcessingWorkspaceResolver>()), recipe, source.ArchivePassword,
                policyConfig, config.ReusePolicy == "force" || target.AcquisitionNonce != null ? ctx.RunId.ToString() : null);
            // This check precedes original-manifest validation: intentionally removed inputs are
            // irrelevant when the exact requested final artifact is still available.
            if (answer?.ExtractionPlanJson == null && await processor.ResolveAsync(intent, ct) is { } complete)
            {
                await BindAsync(ctx, target, complete, ct);
                targets[index] = ApplyArtifact(target, complete, intent);
                await coordinator.ReleaseAsync(ctx, target, ct);
                continue;
            }
            target = target with {ProcessedArtifactId = null, ProcessedDirectory = null, ProcessedFiles = [],
                ProcessingIntentKey = intent, ExecutionStatus = "executing"};
            CloudDownloadManifest.Validate(workset.OutputDirectory, target.Files);
            foreach (var file in target.Files) storage.EnsurePathAllowed(file);
            target = await coordinator.RecordAsync(ctx, target, workset.OutputDirectory, ct);
            var input = await store.ResolveByIdAsync(target.DownloadArtifactId!, ct)
                ?? throw new InvalidOperationException("The original file artifact is missing or modified. Reacquire it before processing.");
            var savedOptions = target.RecursiveOptionsJson == null ? config.Recursive :
                JsonSerializer.Deserialize<RecursiveArchiveOptions>(target.RecursiveOptionsJson, WorkflowJson.Options)!;
            var options = answer?.RecursiveOptions is { } revised ? revised with {Enabled = savedOptions.Enabled} : savedOptions;
            var targetConfig = policyConfig with {Recursive = options};
            Validate(targetConfig);
            recipe = answer?.ExtractionPlanJson ?? recipe;
            if (targetPolicy == "keep") recipe = "{\"requirement\":\"notRequired\",\"steps\":[]}";
            try
            {
                var plan = JsonSerializer.Deserialize<PostExtractionPlan>(recipe, WorkflowJson.Options) ?? new();
                FileProcessingPlanExecutor.Validate(plan);
                plan = plan with {Steps = plan.Steps.Select(s => s.Op == "extractArchive" && s.Password == null
                    ? s with {Password = source.ArchivePassword} : s).ToList()};
                recipe = JsonSerializer.Serialize(plan, WorkflowJson.Options);
            }
            catch (Exception error) when (error is JsonException or InvalidOperationException)
            {
                targets[index] = target;
                if (targetPolicy != "autoDetect")
                    return Wait(workset, targets, index, "ExtractionPlanUnknown", error.Message, recipe);
                recipe = "{\"requirement\":\"notRequired\",\"steps\":[]}";
            }
            if (answer?.ExtractionPlanJson != null)
            {
                var previous = JsonSerializer.Deserialize<PostExtractionPlan>(target.ProcessingPlanJson ?? source.ExtractionPlanJson ?? "{}", WorkflowJson.Options);
                if (previous is {Steps.Count: > 0} || previous?.Requirement == "notRequired")
                    intent = FileArtifactProcessor.Intent(DownloadArtifactCoordinator.ArtifactIntent(target, workset.OutputDirectory, ctx.Services.GetService<FileProcessingWorkspaceResolver>()),
                        recipe, source.ArchivePassword, targetConfig,
                        config.ReusePolicy == "force" || target.AcquisitionNonce != null ? ctx.RunId.ToString() : null);
            }
            target = target with {ProcessingPlanJson = recipe, ProcessingArchivePolicy = targetPolicy,
                RecursiveOptionsJson = JsonSerializer.Serialize(options, WorkflowJson.Options)};
            target = target with {ProcessingIntentKey = intent};
            targets[index] = target;
            await ctx.ReportStage("processingFiles");
            var result = await processor.ExecuteAsync(intent, input, recipe, source.ArchivePassword, targetConfig,
                DownloadArtifactCoordinator.Consumer(ctx, target), answer?.ArchiveId != null && answer.Password != null
                    ? new RecursiveArchivePasswordAnswer(answer.ArchiveId, answer.Password) : null,
                answer?.ArchiveId == null ? answer?.Password : null,
                p => ctx.ReportProgress(p, target.Title).GetAwaiter().GetResult(), ct, replan: answer?.ExtractionPlanJson != null || answer?.ArchivePolicy != null);
            if (result.Artifact == null)
            {
                if (result.WaitReason == "WaitingShared") targets[index] = target with {ExecutionStatus = "waitingShared"};
                return Wait(workset, targets, index, result.WaitReason ?? "ExtractionPlanUnknown",
                    result.Message ?? "File-processing input is required.", recipe, tried: result.TriedPasswords,
                    archiveId: result.ArchiveId, recursiveOptions: options, requiredComponentId: result.RequiredComponentId,
                    pathConflict: result.PathConflict);
            }
            await BindAsync(ctx, target, result.Artifact, ct);
            targets[index] = ApplyArtifact(target, result.Artifact, intent) with {ExecutionStatus = "completed"};
            await coordinator.ReleaseAsync(ctx, target, ct);
        }
        return WorkflowItemOutcome.ReplaceWith(workset with {Targets = targets});
    }

    private static async Task BindAsync(WorkflowExecutionContext ctx, CloudDownloadTarget target, ArtifactRecord artifact, CancellationToken ct)
    {
        var publication = artifact.Kind == "publishedFiles" && artifact.MetadataJson != null
            ? JsonSerializer.Deserialize<FileArtifactPublicationMetadata>(artifact.MetadataJson, WorkflowJson.Options) : null;
        await ArtifactWorkflowBindings.BindAsync(ctx, target.Id + ":artifact.process", publication?.SourceOperationId ?? artifact.OperationId, ct);
        if (publication != null) await ArtifactWorkflowBindings.BindAsync(ctx, target.Id + ":artifact.publish", artifact.OperationId, ct);
    }

    private static WorkflowItemOutcome Wait(DownloadWorkset item, List<CloudDownloadTarget> targets, int index,
        string reason, string message, string planJson, string? archiveName = null, IReadOnlyList<string>? tried = null,
        string? archiveId = null, RecursiveArchiveOptions? recursiveOptions = null, string? requiredComponentId = null,
        FileProcessingPathConflict? pathConflict = null)
    {
        var target = targets[index] with {ProcessingWaitReason = reason, Problem = message};
        targets[index] = target;
        var prompt = new CloudFileProcessingPrompt(target.Id, target.Title, reason, message, planJson, archiveName, tried, archiveId, recursiveOptions,
            FileProcessingResumeActions.Allowed(reason), requiredComponentId, pathConflict);
        return WorkflowItemOutcome.Suspend(new WorkflowSuspension("cloud.fileProcessing",
            JsonSerializer.Serialize(prompt, WorkflowJson.Options), item with {Targets = targets}));
    }

    private static string CommonDirectory(IReadOnlyList<string> files, string outputRoot)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(files[0]))!;
        var comparison = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        foreach (var file in files)
            while (!Path.GetFullPath(file).StartsWith(Path.TrimEndingDirectorySeparator(directory) + Path.DirectorySeparatorChar, comparison))
                directory = Path.GetDirectoryName(directory) ?? throw new InvalidDataException("The file manifest has no shared download directory.");
        return FileProcessingFiles.Within(directory, outputRoot);
    }

    private static void EnsureInternalDirectory(string directory, string appData)
    {
        FileProcessingFiles.Within(directory, appData);
        for (var entry = new DirectoryInfo(directory); entry != null; entry = entry.Parent)
            if (entry.LinkTarget != null) throw new InvalidDataException("Symbolic links cannot be used as processing state directories.");
    }
}
