using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Acquisition.Components.Workflow;
using Bakabase.Modules.Acquisition.Models.Domain;
using Bakabase.Modules.Downloader.Cloud;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Bakabase.Service.Components.CloudDownloads;
using Bakabase.Service.Components.CloudDownloads.Baidu;
using Bakabase.Service.Components.FileProcessing;
using Bootstrap.Components.Configuration.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Service.Components.DownloadIntents;

/// <summary>Acquisition input adapter. The existing cloud/native activities own all transfer and download operations.</summary>
public sealed class FetchSupportedSourcesActivity : IResumableWorkflowActivity, IAcquisitionLeadKindsActivity, IWorkflowCheckpointedActivity
{
    public const string WorksetVariable = "download.workset";
    public string Kind => "acquisition.fetchSupported";
    public string DisplayName => "Acquire supported sources";
    public string Description => "Use the common source adapters and download center, preserving exact manifests and extraction instructions for subsequent file processing.";
    public string DescriptionKey => "workflow.activity.acquisition.fetchSupported.description";
    public string Group => AcquisitionWorkflowKinds.ActivityGroup;
    public WorkflowActivityCategory Category => WorkflowActivityCategory.Action;
    public IReadOnlyList<string> AcceptedInputItemTypes => [AcquisitionWorkflowKinds.ItemAcquisition];
    public IReadOnlyList<AcquisitionLeadKind>? AcceptedLeadKinds =>
        [AcquisitionLeadKind.SharedPage, AcquisitionLeadKind.DirectUrl, AcquisitionLeadKind.Magnet, AcquisitionLeadKind.Torrent];

    public Task<WorkflowItemOutcome> ProcessItemAsync(WorkflowExecutionContext ctx, object item, CancellationToken ct) =>
        Run(ctx, (AcquisitionWorkItem)item, null, ct);
    public Task<WorkflowItemOutcome> ResumeAsync(WorkflowExecutionContext ctx, object item, string signalJson, CancellationToken ct) =>
        Run(ctx, (AcquisitionWorkItem)item, signalJson, ct);

    private static async Task<WorkflowItemOutcome> Run(WorkflowExecutionContext ctx, AcquisitionWorkItem item, string? signalJson, CancellationToken ct)
    {
        var variables = item.Variables.ToDictionary(kv => kv.Key, kv => kv.Value);
        var config = ctx.GetConfig<CloudWorkflowConfig>() ?? new();
        var account = variables.GetValueOrDefault("download.accountId");
        var output = config.OutputDirectory ?? variables.GetValueOrDefault("download.outputDirectory");
        var workset = variables.TryGetValue(WorksetVariable, out var saved)
            ? JsonSerializer.Deserialize<DownloadWorkset>(saved, WorkflowJson.Options) ?? throw new InvalidDataException("The saved download context is invalid.")
            : await CreateWorkset(ctx, item, variables, output, ct);
        if (ctx.Services.GetService<DownloadMethodRegistry>() is { } registry)
        {
            var resolved = new List<CloudDownloadTarget>();
            foreach (var pendingTarget in workset.Targets)
            {
                var sources = new List<CloudSource>();
                foreach (var pendingSource in pendingTarget.Sources) sources.Add(await registry.ResolveAsync(pendingSource, ct));
                resolved.Add(pendingTarget with {Sources = sources});
            }
            workset = workset with {Targets = resolved};
        }
        var answer = signalJson == null ? null : JsonSerializer.Deserialize<CloudSelectionSignal>(signalJson, WorkflowJson.Options);
        workset = DownloadWorksetRouting.ApplySelections(workset, answer);
        account = answer?.AccountId ?? account;
        if (!string.IsNullOrWhiteSpace(answer?.OutputDirectory))
        {
            DownloadWorksetRouting.ValidateOutputDirectory(workset, answer.OutputDirectory);
            workset = workset with {OutputDirectory = answer.OutputDirectory};
            config = config with {OutputDirectory = answer.OutputDirectory};
        }
        if (string.IsNullOrWhiteSpace(workset.OutputDirectory))
        {
            Save(variables, workset, 0);
            return WorkflowItemOutcome.Suspend(new("cloud.chooseDirectory", JsonSerializer.Serialize(new
                {workset.Targets, Reason = "chooseDirectory", workset.OutputDirectory}, WorkflowJson.Options), item with {Variables = variables}));
        }
        if (!Path.IsPathFullyQualified(workset.OutputDirectory)) throw new ArgumentException("Choose an absolute output directory.");
        ctx.Services.GetRequiredService<IUserStoragePolicy>().EnsurePathAllowed(workset.OutputDirectory);
        var source = workset.Targets.Single(t => !t.Excluded).SelectedSource ?? throw new InvalidDataException("The acquired source is missing.");
        if (source.ProviderId == "baidu" && workset.Targets[0].AccountId == null)
        {
            try {account = await ctx.Services.GetRequiredService<BaiduPcsAccountStore>().ResolveAccountIdAsync(account ?? config.AccountId, ct);}
            catch (Exception error) when (error is not OperationCanceledException)
            {
                workset = workset with {Targets = workset.Targets.Select(t => t with {Problem = error.Message}).ToList()};
                Save(variables, workset, 0);
                return WorkflowItemOutcome.Suspend(new("cloud.cloudAttention", JsonSerializer.Serialize(new
                    {workset.Targets, Reason = "cloudAttention", workset.OutputDirectory}, WorkflowJson.Options), item with {Variables = variables}));
            }
            workset = workset with {Targets = workset.Targets.Select(t => t with {AccountId = account,
                RemoteEntry = source.Kind == "remote" ? new("baidu", account, source.Url) : null}).ToList()};
        }
        var storage = ctx.Services.GetRequiredService<IUserStoragePolicy>();
        if (ctx.Services.GetService<DownloadMethodRegistry>() is { } accountRegistry)
        {
            try { workset = await DownloadWorksetRouting.BindAccountsAsync(workset, accountRegistry, account ?? config.AccountId, ct); }
            catch (Exception error) when (error is InvalidOperationException or ArgumentException or IOException)
            {
                workset = workset with {Targets = workset.Targets.Select(t => t with {Problem = error.Message}).ToList()};
                Save(variables, workset, 0);
                return WorkflowItemOutcome.Suspend(new("cloud.cloudAttention", JsonSerializer.Serialize(new
                    {workset.Targets, Reason = "cloudAttention", workset.OutputDirectory}, WorkflowJson.Options), item with {Variables = variables}));
            }
        }
        var library = ctx.Services.GetRequiredService<IBOptions<AcquisitionOptions>>().Value.LibraryRootDirectory;
        var root = string.IsNullOrWhiteSpace(library) ? workset.OutputDirectory : library;
        if (!string.IsNullOrWhiteSpace(root)) storage.EnsurePathAllowed(root);
        var targetDirectory = item.TargetDirectory ?? (string.IsNullOrWhiteSpace(root) ? null : Path.Combine(root, "resource-" + item.ResourceId));
        if (targetDirectory != null) storage.EnsurePathAllowed(targetDirectory);
        var downloadIntent = DownloadArtifactCoordinator.DownloadIntent(workset.Targets.Single(t => !t.Excluded), workset.OutputDirectory,
            ctx.Services.GetService<FileProcessingWorkspaceResolver>());
        variables["download.intentKey"] = downloadIntent;
        variables["artifact.downloadIntent"] = downloadIntent;
        if (targetDirectory != null) variables["artifact.publicationTargetDirectory"] = targetDirectory;
        item = item with {Variables = variables, TargetDirectory = targetDirectory, ExtractionPlanJson = source.ExtractionPlanJson,
            Links = [new(source.Url, source.AccessCode, source.ArchivePassword, ExtractionPlanJson: source.ExtractionPlanJson)], SelectedLinkIndex = 0};
        // Plan using the acquisition item type. A declared downstream consumer may satisfy this
        // acquisition from verified processed/published output after originals were quarantined.
        if (config.ReusePolicy != "force" && signalJson == null && !string.IsNullOrWhiteSpace(workset.OutputDirectory))
        {
            item = (AcquisitionWorkItem)await WorkflowArtifactPlanner.SatisfyDownstreamAsync(ctx, item, ct);
            if (item.AlreadyProcessed && item.Files.Count > 0)
            {
                variables = item.Variables.ToDictionary(kv => kv.Key, kv => kv.Value);
                workset = workset with {Targets = workset.Targets.Select(t => t with
                    {ProcessedArtifactId = variables.GetValueOrDefault("artifact.outputId"),
                        ProcessingOperationId = variables.GetValueOrDefault("artifact.operationId"),
                        ProcessedDirectory = item.ExtractedDirectory, ProcessedFiles = item.Files.ToList(), ExecutionStatus = "reused"}).ToList()};
                Save(variables, workset, 3);
                return WorkflowItemOutcome.ReplaceWith(item with {Variables = variables});
            }
        }
        // Re-entering a phase is idempotent through the same durable operation/binding services.
        var phase = int.TryParse(variables.GetValueOrDefault("download.phase"), out var cursor) ? cursor : 0;
        if (phase is < 0 or > 3) throw new InvalidDataException("The saved download phase is invalid.");
        IResumableWorkflowActivity[] activities = [new BaiduTransferActivity(), new EnqueueCloudSourcesActivity(), new AwaitCloudDownloadsActivity()];
        for (; phase < activities.Length; phase++)
        {
            var nestedConfig = config with {AccountId = string.IsNullOrWhiteSpace(account) ? config.AccountId : account};
            var nested = ctx.ForPlannedActivity(JsonSerializer.Serialize(nestedConfig, WorkflowJson.Options), []);
            var outcome = signalJson == null
                ? await activities[phase].ProcessItemAsync(nested, workset, ct)
                : await activities[phase].ResumeAsync(nested, workset, signalJson, ct);
            signalJson = null;
            if (outcome.Suspension is { } waiting)
            {
                workset = (DownloadWorkset)waiting.Item;
                Save(variables, workset, phase);
                return WorkflowItemOutcome.Suspend(waiting with {Item = item with {Variables = variables}});
            }
            workset = outcome.Replacement as DownloadWorkset ?? workset;
            Save(variables, workset, phase + 1);
        }
        var target = workset.Targets.Single(t => !t.Excluded);
        if (target.Files.Count == 0 || string.IsNullOrWhiteSpace(target.DownloadDirectory))
            throw new InvalidDataException("The acquisition did not produce a complete file manifest.");
        source = target.SelectedSource ?? throw new InvalidDataException("The acquired source is missing.");
        root = string.IsNullOrWhiteSpace(library) ? workset.OutputDirectory : library;
        storage.EnsurePathAllowed(root);
        // A stable resource directory prevents two placeholders with equal titles sharing publication output.
        targetDirectory = item.TargetDirectory ?? Path.Combine(root, "resource-" + item.ResourceId);
        storage.EnsurePathAllowed(targetDirectory);
        variables["artifact.publicationTargetDirectory"] = targetDirectory;
        if (target.DownloadArtifactId != null) variables["artifact.inputId"] = target.DownloadArtifactId;
        variables["download.intentKey"] = DownloadArtifactCoordinator.DownloadIntent(target, workset.OutputDirectory,
            ctx.Services.GetService<FileProcessingWorkspaceResolver>());
        variables["artifact.downloadIntent"] = variables["download.intentKey"];
        return WorkflowItemOutcome.ReplaceWith(item with
        {
            Variables = variables, WorkingDirectory = target.DownloadDirectory, Files = target.Files,
            TargetDirectory = targetDirectory, ExtractionPlanJson = source.ExtractionPlanJson,
            Links = [new(source.Url, source.AccessCode, source.ArchivePassword, ExtractionPlanJson: source.ExtractionPlanJson)],
            SelectedLinkIndex = 0, PreserveDirectoryStructure = true
        });
    }

    private static async Task<DownloadWorkset> CreateWorkset(WorkflowExecutionContext ctx, AcquisitionWorkItem item,
        IReadOnlyDictionary<string, string> variables, string? output, CancellationToken ct)
    {
        var link = item.SelectedLink ?? item.Links.FirstOrDefault(l => l.Url == item.LeadValue);
        var candidate = variables.TryGetValue("download.candidate", out var json)
            ? JsonSerializer.Deserialize<DownloadCandidate>(json, WorkflowJson.Options) ?? throw new InvalidDataException("The saved download candidate is invalid.")
            : new DownloadCandidate {Id = "lead", Url = link?.Url ?? item.LeadValue,
                Kind = item.LeadKind == AcquisitionLeadKind.DirectUrl ? "http" : item.LeadKind == AcquisitionLeadKind.Torrent ? "torrent" : "auto",
                Title = item.Title, AccessCode = link?.AccessCode, ArchivePassword = link?.ArchivePassword,
                ExtractionPlanJson = item.ExtractionPlanJson ?? link?.ExtractionPlanJson};
        var registry = ctx.Services.GetService<DownloadMethodRegistry>();
        CloudSource source;
        if (variables.TryGetValue("download.resolvedSource", out var resolvedJson))
        {
            var prepared = JsonSerializer.Deserialize<CloudSource>(resolvedJson, WorkflowJson.Options)
                ?? throw new InvalidDataException("The saved resolved source is invalid.");
            if (prepared.Id != candidate.Id || prepared.Url != candidate.Url)
                throw new InvalidDataException("The prepared source does not match the acquisition candidate.");
            source = registry == null ? DownloadMethodRegistry.Normalize(candidate) with
                {OriginalUrl = prepared.OriginalUrl, ResolutionChain = prepared.ResolutionChain}
                : await registry.ResolveAsync(prepared, ct);
        }
        else source = registry == null ? DownloadMethodRegistry.Normalize(candidate) : await registry.ResolveAsync(candidate, ct);
        var set = CloudWorkflow.FromSources([source], item.Title, "acquisition:" + ctx.RunId, output ?? "") with
            {SourceRefs = (ctx.Payload as IWorkflowSourcePayload)?.SourceRefs.ToList() ?? []};
        return set with {Targets = set.Targets.Select(t => t with {SelectedSourceId = source.Id}).ToList()};
    }

    private static void Save(Dictionary<string, string> variables, DownloadWorkset workset, int phase)
    {
        variables[WorksetVariable] = JsonSerializer.Serialize(workset, WorkflowJson.Options);
        variables["download.phase"] = phase.ToString(System.Globalization.CultureInfo.InvariantCulture);
        variables["execution.summary"] = JsonSerializer.Serialize(workset.ToWorkflowOutputSummary(), WorkflowJson.Options);
    }
}
