using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain;
using Bakabase.Modules.Acquisition.Components.Workflow;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Workflow.Artifacts;
using Microsoft.Extensions.DependencyInjection;
using Bakabase.Service.Components.CloudDownloads;

namespace Bakabase.Service.Components.FileProcessing;

public sealed record FileArtifactAnswer(string? TargetId = null, string? Password = null,
    string? ExtractionPlanJson = null, string? ArchiveId = null, RecursiveArchiveOptions? RecursiveOptions = null,
    string? ArchivePolicy = null, string? Action = null);

/// <summary>Local/acquisition adapter to the same processor used by downloader adapters.</summary>
public sealed class ProcessLocalFileArtifactsActivity : IResumableWorkflowActivity, IWorkflowArtifactDemandActivity, IFileProcessingPlanConsumer
{
    public string Kind => "fs.processArtifacts";
    public string DisplayName => "Process file artifacts";
    public string Description => "Apply file instructions and optional recursive extraction to an exact local manifest. Verify and reuse durable results; preserve original files.";
    public string DescriptionKey => "workflow.activity.fsProcessArtifacts.description";
    public string Group => "fs";
    public WorkflowActivityCategory Category => WorkflowActivityCategory.Action;
    public IReadOnlyList<string> AcceptedInputItemTypes => [AcquisitionWorkflowKinds.ItemAcquisition];
    public string GetArchivePolicy(string configJson) =>
        (JsonSerializer.Deserialize<FileArtifactProcessingConfig>(configJson, WorkflowJson.Options) ?? new()).ArchivePolicy;
    public Task<WorkflowItemOutcome> ProcessItemAsync(WorkflowExecutionContext ctx, object item, CancellationToken ct) => Run(ctx, (AcquisitionWorkItem)item, null, ct);
    public Task<WorkflowItemOutcome> ResumeAsync(WorkflowExecutionContext ctx, object item, string signalJson, CancellationToken ct) =>
        Run(ctx, (AcquisitionWorkItem)item, JsonSerializer.Deserialize<FileArtifactAnswer>(signalJson, WorkflowJson.Options), ct);

    public async Task<IReadOnlyList<WorkflowValidationIssue>> ValidateConfigAsync(WorkflowValidationContext context, CancellationToken ct)
    {
        try
        {
            var config = JsonSerializer.Deserialize<FileArtifactProcessingConfig>(context.ConfigJson, WorkflowJson.Options) ?? new();
            FileArtifactProcessor.Validate(config);
            if (config.ArchivePolicy != "instructions" || context.Payload is not AcquisitionWorkItem item ||
                string.IsNullOrWhiteSpace(item.ExtractionPlanJson)) return [];
            var plan = JsonSerializer.Deserialize<FileProcessingPlan>(item.ExtractionPlanJson, WorkflowJson.Options);
            return await FileProcessingDependencyPreflight.InspectWorkflowAsync(context, [plan], ct);
        }
        catch (Exception error) when (error is JsonException or ArgumentException or InvalidOperationException)
        {return [new() {Code = "fileProcessingConfigInvalid", Message = "Check the file-processing plan and settings."}];}
    }

    private static FileArtifactProcessingConfig Config(string json, IReadOnlyDictionary<string, string> variables, FileArtifactAnswer? answer)
    {
        var config = JsonSerializer.Deserialize<FileArtifactProcessingConfig>(json, WorkflowJson.Options) ?? new();
        if (variables.TryGetValue("artifact.recursiveOptions", out var saved))
            config = config with {Recursive = JsonSerializer.Deserialize<RecursiveArchiveOptions>(saved, WorkflowJson.Options)!
                with {Enabled = config.Recursive.Enabled}};
        if (answer?.RecursiveOptions != null) config = config with {Recursive = answer.RecursiveOptions with {Enabled = config.Recursive.Enabled}};
        config = config with {ArchivePolicy = answer?.ArchivePolicy ?? variables.GetValueOrDefault("artifact.archivePolicy") ?? config.ArchivePolicy};
        FileArtifactProcessor.Validate(config);
        return config;
    }
    private static string? DownloadIntent(IReadOnlyDictionary<string, string> variables) =>
        variables.GetValueOrDefault("download.intentKey") ?? variables.GetValueOrDefault("artifact.downloadIntent") ?? variables.GetValueOrDefault("artifact.inputIntent");
    private static string Intent(WorkflowExecutionContext ctx, AcquisitionWorkItem item, IReadOnlyDictionary<string, string> variables,
        string inputKey, FileArtifactProcessingConfig config, FileArtifactAnswer? answer)
    {
        var previous = variables.GetValueOrDefault("artifact.processingIntent");
        return answer?.ArchivePolicy == null && previous?.StartsWith(FileArtifactProcessor.IntentVersion, StringComparison.Ordinal) == true
            ? previous : FileArtifactProcessor.Intent(inputKey, item.ExtractionPlanJson ?? "{}", item.SelectedLink?.ArchivePassword,
                config, config.ReusePolicy == "force" ? ctx.RunId.ToString() : null);
    }

    public async Task<object> TrySatisfyAsync(WorkflowExecutionContext context, object item, string configJson, CancellationToken ct)
    {
        if (item is not AcquisitionWorkItem local) return item;
        var variables = local.Variables.ToDictionary(kv => kv.Key, kv => kv.Value);
        var inputKey = DownloadIntent(variables);
        var config = Config(configJson, variables, null);
        if (inputKey == null || config.ReusePolicy == "force") return item;
        var intent = Intent(context, local, variables, inputKey, config, null);
        var processor = context.Services.GetRequiredService<FileArtifactProcessor>();
        var artifact = await processor.ResolveAsync(intent, ct) ??
            await PublishFileArtifactsActivity.ResolveFollowingLocalPublicationAsync(context, local, intent, ct);
        return artifact == null ? local : await Complete(context, local, variables, artifact, intent, ct);
    }

    private static async Task<AcquisitionWorkItem> Complete(WorkflowExecutionContext ctx, AcquisitionWorkItem item,
        Dictionary<string, string> variables, ArtifactRecord artifact, string intent, CancellationToken ct)
    {
        var publication = artifact.Kind == "publishedFiles" && artifact.MetadataJson != null
            ? JsonSerializer.Deserialize<FileArtifactPublicationMetadata>(artifact.MetadataJson, WorkflowJson.Options) : null;
        var operation = publication?.SourceOperationId ?? artifact.OperationId;
        await ArtifactWorkflowBindings.BindAsync(ctx, "files:artifact.process", operation, ct);
        variables["artifact.processingIntent"] = intent;
        variables["artifact.outputId"] = artifact.Id;
        variables["artifact.operationId"] = operation;
        variables["execution.status"] = "completed";
        variables.Remove("artifact.processingWaitReason");
        if (publication != null)
        {
            await ArtifactWorkflowBindings.BindAsync(ctx, "files:artifact.publish", artifact.OperationId, ct);
            variables["artifact.publishedId"] = artifact.Id;
            variables["artifact.publicationId"] = artifact.OperationId;
        }
        return item with {Variables = variables, ExtractedDirectory = artifact.RootDirectory,
            Files = artifact.Files, AlreadyProcessed = true, PreserveDirectoryStructure = true};
    }

    public async Task<WorkflowItemOutcome> Run(WorkflowExecutionContext ctx, AcquisitionWorkItem item, FileArtifactAnswer? answer, CancellationToken ct)
    {
        using var writeScope = StorageWriteScope.Enter(new("workflow", ctx.RunId.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        if (answer?.Action != null)
            FileProcessingResumeActions.Validate(ctx,
                item.Variables.GetValueOrDefault("artifact.processingIntent") ?? "",
                item.Variables.GetValueOrDefault("artifact.processingWaitReason"),
                new CloudFileProcessingSignal {TargetId = answer.TargetId ?? "", Action = answer.Action,
                    Password = answer.Password, ExtractionPlanJson = answer.ExtractionPlanJson,
                    ArchiveId = answer.ArchiveId, ArchivePolicy = answer.ArchivePolicy, RecursiveOptions = answer.RecursiveOptions});
        var store = ctx.Services.GetRequiredService<IArtifactOperationStore>();
        var processor = ctx.Services.GetRequiredService<FileArtifactProcessor>();
        var variables = item.Variables.ToDictionary(kv => kv.Key, kv => kv.Value);
        var config = Config(ctx.ActivityConfigJson, variables, answer) with {StorageTitle = item.Title};
        var originalPlan = item.ExtractionPlanJson ?? "{}";
        var inputKey = DownloadIntent(variables);
        var consumer = "workflow:" + ctx.RunId + ":files";
        ArtifactRecord? input = null;
        if (inputKey == null)
        {
            var storage = ctx.Services.GetRequiredService<IUserStoragePolicy>();
            storage.EnsurePathAllowed(item.WorkingDirectory);
            foreach (var path in item.Files) storage.EnsurePathAllowed(path);
            var manifest = await ArtifactManifestFiles.CaptureAsync(item.WorkingDirectory, item.Files, ct);
            inputKey = ArtifactManifestFiles.Hash("local-files-v1:" + manifest.Fingerprint + ":" + Path.GetFullPath(item.WorkingDirectory));
            input = await store.AdoptAsync(new(inputKey, inputKey, "files.local", manifest.Fingerprint),
                new(item.WorkingDirectory, item.Files, "localFiles", false), consumer, ct);
        }
        var intent = Intent(ctx, item, variables, inputKey, config, answer);
        var expectedAnswerId = variables.GetValueOrDefault("artifact.processingIntent") ?? intent;
        if (answer?.TargetId != null && answer.TargetId != expectedAnswerId)
            throw new ArgumentException("The answer belongs to another file operation.");
        if (answer?.ExtractionPlanJson == null && answer?.ArchivePolicy == null)
        {
            var reusable = await processor.ResolveAsync(intent, ct) ??
                await PublishFileArtifactsActivity.ResolveFollowingLocalPublicationAsync(ctx, item, intent, ct);
            if (reusable != null) return WorkflowItemOutcome.ReplaceWith(await Complete(ctx, item, variables, reusable, intent, ct));
        }
        if (input == null)
        {
            var id = variables.GetValueOrDefault("artifact.inputId") ?? throw new InvalidOperationException("The downloaded input artifact was not registered.");
            input = await store.ResolveByIdAsync(id, ct) ?? throw new InvalidOperationException("The input artifact is missing or modified. Reacquire it before processing.");
            var actual = await ArtifactManifestFiles.CaptureAsync(item.WorkingDirectory, item.Files, ct,
                ArtifactManifestFiles.ResolveDirectories(input.RootDirectory, input.Manifest));
            if (!ArtifactManifestFiles.PathComparer.Equals(Path.GetFullPath(item.WorkingDirectory), input.RootDirectory) || actual.Fingerprint != input.Fingerprint)
                throw new InvalidDataException("The acquisition manifest differs from its registered download artifact.");
        }
        if (!await store.PinConsumerAsync(input.OperationId, consumer, "workflow", ctx.RunId.ToString(), ct))
            throw new InvalidOperationException("The input files are being cleaned.");
        var recipe = answer?.ExtractionPlanJson ?? originalPlan;
        if (answer?.ExtractionPlanJson != null)
        {
            var previous = JsonSerializer.Deserialize<FileProcessingPlan>(originalPlan, WorkflowJson.Options);
            if (previous is {Steps.Count: > 0} || previous?.Requirement == "notRequired")
                intent = FileArtifactProcessor.Intent(inputKey, recipe, item.SelectedLink?.ArchivePassword, config,
                    config.ReusePolicy == "force" ? ctx.RunId.ToString() : null);
        }
        var result = await processor.ExecuteAsync(intent, input, recipe, item.SelectedLink?.ArchivePassword, config, consumer,
            answer?.ArchiveId != null && answer.Password != null ? new(answer.ArchiveId, answer.Password) : null,
            answer?.ArchiveId == null ? answer?.Password : null,
            p => ctx.ReportProgress(p, item.Title).GetAwaiter().GetResult(), ct, replan: answer?.ExtractionPlanJson != null || answer?.ArchivePolicy != null);
        variables["artifact.processingIntent"] = intent;
        variables["artifact.inputIntent"] = inputKey;
        variables["artifact.archivePolicy"] = config.ArchivePolicy;
        variables["artifact.recursiveOptions"] = JsonSerializer.Serialize(config.Recursive, WorkflowJson.Options);
        variables["artifact.inputId"] = input.Id;
        if (result.Artifact == null)
        {
            variables["artifact.processingWaitReason"] = result.WaitReason ?? "ExtractionPlanUnknown";
            return WorkflowItemOutcome.Suspend(new("files.processing", JsonSerializer.Serialize(new
                {TargetId = intent, item.Title, Reason = result.WaitReason, result.Message, ExtractionPlanJson = recipe,
                    result.ArchiveId, Tried = result.TriedPasswords, RecursiveOptions = config.Recursive, result.RequiredComponentId, result.PathConflict,
                    AllowedActions = FileProcessingResumeActions.Allowed(result.WaitReason)}, WorkflowJson.Options),
                item with {Variables = variables, ExtractionPlanJson = recipe}));
        }
        await store.ReleaseConsumerAsync(input.OperationId, consumer, ct);
        return WorkflowItemOutcome.ReplaceWith(await Complete(ctx, item, variables, result.Artifact, intent, ct));
    }
}
