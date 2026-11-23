using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.InsideWorld.Business;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models.Constants;
using Bakabase.InsideWorld.Business.Components.Downloader.Services;
using Bakabase.InsideWorld.Business.Components.PostParser.Workflow;
using Bakabase.Modules.Bittorrent.Abstractions;
using Bakabase.Modules.Bittorrent.Models;
using Bakabase.Modules.Downloader.Cloud;
using Bakabase.Modules.HttpDownloads.Abstractions;
using Bakabase.Modules.HttpDownloads.Models;
using Bakabase.Modules.PostParser.Models.Domain;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Service.Components.DownloadIntents;
using Bakabase.Service.Components.FileProcessing;
using Bakabase.Abstractions.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Service.Components.CloudDownloads;

public static class CloudWorkflow
{
    private static readonly ICloudTransferActivity BuiltinTransfer = new BaiduTransferActivity();
    internal static ICloudTransferActivity? TransferActivity(string? kind, IWorkflowActivityRegistry? registry = null) =>
        kind == null ? null : registry == null ? kind == BuiltinTransfer.Kind ? BuiltinTransfer : null :
        registry.Get(kind) as ICloudTransferActivity;

    public const string Trigger = "download.sources";
    public const string ItemType = "item.download.workset";
    public const string BuiltinName = "Download selected sources (v1)";
    public static readonly string[] Nodes = ["download.selectSources", "baidu.transfer", "download.enqueueSupported", "download.awaitCompletion"];

    public static DownloadWorkset Prepare(PostDownloadInfo info, string origin, string outputDirectory = "")
    {
        var sources = info.Resources.Select((r, index) => new CloudSource
        {
            Id = CloudOperationService.Key(origin, index.ToString(), r.Link, r.Code ?? "", r.Password ?? ""),
            Url = r.Link, ProviderId = CloudSourceClassifier.Classify(r.Link),
            Kind = CloudSourceClassifier.Classify(r.Link) is "http" or "torrent" ? CloudSourceClassifier.Classify(r.Link) : "share", AccessCode = r.Code,
            ArchivePassword = r.Password, GroupId = r.GroupId,
            ExtractionPlanJson = r.Extraction == null ? null : JsonSerializer.Serialize(r.Extraction, WorkflowJson.Options)
        }).ToList();
        return FromSources(sources, info.Title, origin, outputDirectory);
    }

    public static DownloadWorkset FromSources(IReadOnlyList<CloudSource> sources, string? title, string id, string outputDirectory)
    {
        if (sources.Count is 0 or > 500) throw new ArgumentException("Provide between one and 500 download sources.");
        if (sources.Select(s => s.Id).Distinct().Count() != sources.Count || sources.Any(s => string.IsNullOrWhiteSpace(s.Id)))
            throw new ArgumentException("Every download source needs a unique identity.");
        var targets = sources.GroupBy(s => string.IsNullOrWhiteSpace(s.GroupId) ? "source:" + s.Id : "group:" + s.GroupId)
            .Select(g => new CloudDownloadTarget {Id = CloudOperationService.Key(id, g.Key), Title = title, Sources = g.ToList()}).ToList();
        return new() {Id = id, Title = title, OutputDirectory = outputDirectory, Targets = targets};
    }
}

public sealed class CloudSourcesTrigger : IWorkflowTrigger
{
    public string Kind => CloudWorkflow.Trigger;
    public string DisplayName => "Download selected sources";
    public string SourceModule => "downloader";
    public WorkflowActivationMode ActivationMode => WorkflowActivationMode.Module;
    public bool SupportsManualRun => false;
    public string Description => "Starts from selected saved post results or the cloud download form. Carries sources and access information without parsing the post again. Parsing alone never starts downloads.";
    public string DescriptionKey => "workflow.trigger.cloudSources.description";
    public Type PayloadType => typeof(DownloadWorkset);
    public bool Matches(object payload, string? triggerFilterJson) => false;
    public string ResolveOutputItemType(string? triggerFilterJson) => CloudWorkflow.ItemType;
    public IReadOnlyList<object> ExtractItems(object payload) => payload is DownloadWorkset workset && workset.Targets.Count > 0
        ? [workset] : throw new ArgumentException("Choose download sources from the source form.");
}
public sealed class CloudWorksetDescriptor : IWorkflowItemTypeDescriptor
{
    public string ItemType => CloudWorkflow.ItemType;
    public string DisplayName => "Download sources";
    public Type ClrType => typeof(DownloadWorkset);
}
public sealed record CloudWorkflowConfig
{
    public List<string> SourceIds { get; init; } = [];
    public List<string> PreferProviders { get; init; } = CloudSites.Sources.Select(s => s.ProviderId).ToList();
    public bool AllowUngrouped { get; init; }
    public string? AccountId { get; init; }
    public string RemoteDirectory { get; init; } = "/Bakabase";
    public string? DownloadLayout { get; init; }
    public string? OutputDirectory { get; init; }
    public string ReusePolicy { get; init; } = "reuseVerified";
    public ArtifactStoragePolicy? StoragePolicy { get; init; }
    public Dictionary<string, ArtifactStoragePolicyOverride>? TargetStorageOverrides { get; init; }
}

public sealed class PrepareCloudSourcesActivity : IWorkflowActivity
{
    public string Kind => "download.prepareSources";
    public string DisplayName => "Prepare download sources";
    public string Description => "Preserve the parsed links, content groups, access codes and extraction instructions for provider routing.";
    public string DescriptionKey => "workflow.activity.cloudPrepare.description";
    public string Group => "downloader";
    public WorkflowActivityCategory Category => WorkflowActivityCategory.Transform;
    public IReadOnlyList<string> AcceptedInputItemTypes => [PostParserWorkflow.ResultType];
    public WorkflowItemTypeBehavior OutputBehavior => WorkflowItemTypeBehavior.Fixed;
    public string FixedOutputItemType => CloudWorkflow.ItemType;
    public Task<WorkflowItemOutcome> ProcessItemAsync(WorkflowExecutionContext ctx, object item, CancellationToken ct)
    {
        var parsed = (PostParserResultItem)item;
        if (!parsed.Result.IsComplete) throw new InvalidOperationException("Complete parsing before preparing downloads.");
        return Task.FromResult(WorkflowItemOutcome.ReplaceWith(CloudWorkflow.Prepare(parsed.Result,
            "run:" + ctx.RunId, ctx.GetConfig<CloudWorkflowConfig>()?.OutputDirectory ?? "") with
            {PostTaskId = parsed.Input.TaskId, PostRevision = parsed.Input.Revision,
                SourceRefs = ((Bakabase.Modules.Workflow.Abstractions.Models.Domain.IWorkflowSourcePayload)parsed.Input).SourceRefs.ToList()}));
    }
}

public abstract class CloudActivity(string kind, string name, string descriptionKey) : IResumableWorkflowActivity, IWorkflowArtifactTransparentActivity
{
    public bool PreservesArtifactDemand(string configJson)
    {
        var config = JsonSerializer.Deserialize<CloudWorkflowConfig>(configJson, WorkflowJson.Options) ?? new();
        return kind != "download.selectSources" && config.ReusePolicy != "force" &&
            config.AccountId == null && config.OutputDirectory == null &&
            (kind != "baidu.transfer" || config.DownloadLayout != "separateDirectory");
    }
    public string Kind => kind;
    public string DisplayName => name;
    public string Description => "Processes matching selected sources and preserves other providers. Missing configuration waits for user input; completed operations and downloads are reused on retry.";
    public string DescriptionKey => descriptionKey;
    public string Group => "downloader";
    public WorkflowActivityCategory Category => WorkflowActivityCategory.Action;
    public IReadOnlyList<string> AcceptedInputItemTypes => [CloudWorkflow.ItemType];
    public Task<IReadOnlyList<WorkflowValidationIssue>> ValidateConfigAsync(WorkflowValidationContext context, CancellationToken ct)
    {
        var issues = new List<WorkflowValidationIssue>();
        CloudWorkflowConfig? config;
        try {config = JsonSerializer.Deserialize<CloudWorkflowConfig>(context.ConfigJson, WorkflowJson.Options);}
        catch (JsonException)
        {
            issues.Add(new() {Code = "cloudConfigInvalid", Message = "The cloud node configuration is not valid JSON."});
            return Task.FromResult<IReadOnlyList<WorkflowValidationIssue>>(issues);
        }
        if (config?.AccountId is {Length: > 0} accountId && !Guid.TryParseExact(accountId, "N", out _))
            issues.Add(new() {Code = "cloudAccountInvalid", Message = "Choose a saved Baidu account.", MessageKey = "cloudDownload.validation.account"});
        if (config?.OutputDirectory is {Length: > 0} output && !Path.IsPathFullyQualified(output))
            issues.Add(new() {Code = "cloudDirectoryInvalid", Message = "Choose an absolute download directory.", MessageKey = "cloudDownload.validation.directory"});
        if (kind == "baidu.transfer" && config?.DownloadLayout is not (null or "currentDirectory" or "separateDirectory"))
            issues.Add(new() {Code = "downloadLayoutInvalid", Message = "Choose a valid download layout."});
        if (config?.ReusePolicy is not (null or "reuseVerified" or "force"))
            issues.Add(new() {Code = "reusePolicyInvalid", Message = "Choose verified reuse or force execution."});
        try { CloudStoragePolicyBinding.Validate(config?.StoragePolicy, config?.TargetStorageOverrides); }
        catch (ArgumentException)
        { issues.Add(new() {Code = "storagePolicyInvalid", Message = "Choose valid storage templates and cleanup preferences."}); }
        if (config != null && (config.SourceIds == null || config.PreferProviders == null))
            issues.Add(new() {Code = "cloudConfigInvalid", Message = "Source selections and provider preferences must be arrays."});
        if (kind == "baidu.transfer" && config != null &&
            (string.IsNullOrEmpty(config.RemoteDirectory) || !config.RemoteDirectory.StartsWith('/') || config.RemoteDirectory.Contains('\0') || config.RemoteDirectory.Split('/').Contains("..")))
            issues.Add(new() {Code = "cloudRemoteDirectoryInvalid", Message = "Choose an absolute directory on the cloud drive.", MessageKey = "cloudDownload.validation.remoteDirectory"});
        return Task.FromResult<IReadOnlyList<WorkflowValidationIssue>>(issues);
    }
    public Task<WorkflowItemOutcome> ProcessItemAsync(WorkflowExecutionContext ctx, object item, CancellationToken ct) => Run(ctx, (DownloadWorkset)item, null, ct);
    public Task<WorkflowItemOutcome> ResumeAsync(WorkflowExecutionContext ctx, object item, string signalJson, CancellationToken ct) =>
        Run(ctx, (DownloadWorkset)item, JsonSerializer.Deserialize<CloudSelectionSignal>(signalJson, WorkflowJson.Options), ct);

    private async Task<WorkflowItemOutcome> Run(WorkflowExecutionContext ctx, DownloadWorkset workset, CloudSelectionSignal? signal, CancellationToken ct)
    {
        if (signal?.TransferReplay is {} replay)
        {
            var targets = workset.Targets.Where(t => !t.Excluded).ToArray();
            if (kind != "download.enqueueSupported" || replay.Attempts == null || targets.Length == 0 ||
                !targets.Select(t => t.Id).ToHashSet(StringComparer.Ordinal).SetEquals(replay.Attempts.Keys) ||
                JsonSerializer.Serialize(signal, WorkflowJson.Options) != JsonSerializer.Serialize(
                    new CloudSelectionSignal {TransferReplay = replay}, WorkflowJson.Options))
                throw new InvalidOperationException("RECOVERY_DOWNLOAD_IDENTITY_CHANGED");
            var replayOperations = ctx.Services.GetRequiredService<CloudOperationService>();
            var refreshed = workset.Targets.ToList();
            foreach (var target in targets)
            {
                var receipt = await replayOperations.ExecutePreparedTransferReplayAsync(checked((int)ctx.RunId), target,
                    workset.OutputDirectory, replay.RemoteDirectory, replay.Attempts[target.Id], ct);
                refreshed[refreshed.IndexOf(target)] = target with {RemoteEntry = receipt.Entry, ReviewedTransferId = receipt.OperationId};
            }
            workset = workset with {Targets = refreshed, NoRepeatExistingDownloads = true};
        }
        var completedDownloadReceipts = signal?.CompletedDownloadReceipts;
        if (completedDownloadReceipts != null)
        {
            var nativeIds = workset.Targets.Where(t => !t.Excluded).Select(t => t.DownloadTaskId).Distinct().ToArray();
            var expected = new CloudSelectionSignal {ObserveExistingOnly = true, CompletedDownloadReceipts = completedDownloadReceipts};
            if (kind != "download.awaitCompletion" || nativeIds.Length == 0 || nativeIds.Length != completedDownloadReceipts.Count ||
                nativeIds.Any(id => id == null || !completedDownloadReceipts.ContainsKey(id.Value)) ||
                JsonSerializer.Serialize(expected, WorkflowJson.Options) != JsonSerializer.Serialize(signal, WorkflowJson.Options))
                throw new InvalidOperationException("RECOVERY_DOWNLOAD_IDENTITY_CHANGED");
        }
        if (signal?.TransferredDownloadPlacement is {} placement)
        {
            // The public workflow resume endpoint also accepts JSON. The receipt alone cannot
            // authorize combining a new source/account, an arbitrary path, or a second download.
            var expected = new CloudSelectionSignal {TransferredDownloadPlacement = placement};
            if (kind != "download.enqueueSupported" ||
                JsonSerializer.Serialize(expected, WorkflowJson.Options) != JsonSerializer.Serialize(signal, WorkflowJson.Options))
                throw new InvalidOperationException("RECOVERY_DOWNLOAD_IDENTITY_CHANGED");
            workset = await ctx.Services.GetRequiredService<Bakabase.Service.Components.Acquisition.Planning.AcquisitionTransferredDownloadRecovery>()
                .RestoreAsync(checked((int)ctx.RunId), workset, placement, ct);
        }
        if (signal?.ObserveExistingOnly == true || signal?.RevalidateExistingDownloads == true || signal?.VerifyExistingFilesOnly == true)
            workset = workset with {NoRepeatExistingDownloads = true};
        if (signal?.VerifyExistingFilesOnly == true && (signal.ResumeTargetIds == null || signal.RevalidateExistingDownloads == true))
            throw new ArgumentException("Existing-file verification must select original targets and cannot authorize download retries.");
        if (signal?.RevalidateExistingDownloads == true && signal.ResumeTargetIds == null)
            throw new ArgumentException("Select existing download targets for diagnostic revalidation.");
        if (signal?.ResumeTargetIds is { } resumeTargetIds)
        {
            if (kind != "download.awaitCompletion" || resumeTargetIds.Count == 0 ||
                resumeTargetIds.Count > workset.Targets.Count || resumeTargetIds.Distinct(StringComparer.Ordinal).Count() != resumeTargetIds.Count ||
                resumeTargetIds.Any(id => string.IsNullOrWhiteSpace(id) || !workset.Targets.Any(t => t.Id == id && !t.Excluded)) ||
                signal.SourceIds is not {Count: 0} || signal.SourceMethods is not {Count: 0} || signal.SourceAccounts is not {Count: 0} ||
                signal.SourceCredentialCorrections is not {Count: 0} || signal.AccountId != null || signal.OutputDirectory != null)
                throw new ArgumentException("Choose existing download targets only at the download wait step; do not combine this with source or configuration changes.");
            var selected = resumeTargetIds.ToHashSet(StringComparer.Ordinal);
            if (workset.Targets.Any(t => selected.Contains(t.Id) && t.DownloadTaskId == null && t.TransportTaskId == null &&
                t.DownloadArtifactId == null && t.ProcessedArtifactId == null && t.PublishedArtifactId == null))
                throw new ArgumentException("Selected targets must already have a download or a saved artifact.");
            return await ResumeSelectedDownloads(ctx, workset, selected, ct, signal.RevalidateExistingDownloads == true, signal.VerifyExistingFilesOnly == true);
        }
        if (kind == "download.awaitCompletion" && signal != null && signal.ObserveExistingOnly != true &&
            workset.Targets.Count > 1)
            return Wait(workset, "cloudAttention");
        var config = ctx.GetConfig<CloudWorkflowConfig>() ?? new();
        try
        {
            workset = await ctx.Services.GetRequiredService<CloudOperationService>().RecoverCredentialCorrectionsAsync(
                checked((int)ctx.RunId), kind, workset, ct);
        }
        catch (Exception error) when (error is InvalidOperationException or JsonException)
        {
            return Wait(workset with {Targets = workset.Targets.Select(t => t with {Problem = error.Message}).ToList()}, "cloudAttention");
        }
        if (signal?.SourceCredentialCorrections == null && signal != null)
            throw new ArgumentException("Access-code corrections must be an array.");
        if (signal?.SourceCredentialCorrections.Count > 0)
        {
            if (signal.SourceIds.Count != 0 || signal.SourceMethods.Count != 0 || signal.SourceAccounts.Count != 0 ||
                signal.AccountId != null || signal.OutputDirectory != null)
                throw new ArgumentException("Correct access codes separately from changing sources, accounts or output directories.");
            workset = await ctx.Services.GetRequiredService<CloudOperationService>().ApplyCredentialCorrectionsAsync(
                checked((int)ctx.RunId), kind, workset, signal.SourceCredentialCorrections, ct);
        }
        workset = DownloadWorksetRouting.ApplySelections(workset, signal);
        var requestedOutput = signal?.OutputDirectory ?? config.OutputDirectory;
        try { DownloadWorksetRouting.ValidateOutputDirectory(workset, requestedOutput); }
        catch (InvalidOperationException error)
        { return Wait(workset with {Targets = workset.Targets.Select(t => t with {Problem = error.Message}).ToList()}, "cloudAttention"); }
        if (!string.IsNullOrWhiteSpace(requestedOutput))
            workset = workset with {OutputDirectory = requestedOutput};
        var account = signal?.AccountId ?? config.AccountId;
        if (workset.NoRepeatExistingDownloads == true && config.ReusePolicy == "force")
            return Wait(workset with {Targets = workset.Targets.Select(t => t with {Problem = "RECOVERY_DOWNLOAD_REVIEW_REQUIRED"}).ToList()}, "cloudAttention");
        if (!string.IsNullOrWhiteSpace(account))
        {
            if (workset.Targets.Any(t => (t.RemoteEntry != null || t.DownloadTaskId != null || t.TransportTaskId != null ||
                    t.DownloadArtifactId != null || t.ProcessedArtifactId != null || t.PublishedArtifactId != null) &&
                t.SelectedSource?.ProviderId == "baidu" && t.AccountId != account))
                return Wait(workset with {Targets = workset.Targets.Select(t => t with
                    {Problem = "The transferred files and download task belong to the original account. Start another run to use a different account."}).ToList()}, "cloudAttention");
            workset = workset with {Targets = workset.Targets.Select(t => t with
                {AccountId = (t.SelectedSource?.ProviderId == "baidu" || t.SelectedSource == null && t.Sources.Any(s => s.ProviderId == "baidu")) ? account : t.AccountId}).ToList()};
        }
        if (kind != "download.selectSources" && signal?.SourceIds.Count > 0)
        {
            var ids = signal.SourceIds;
            if (ids.Distinct().Count() != ids.Count || ids.Any(id => !workset.Targets.Any(t => t.Sources.Any(s => s.Id == id))) ||
                workset.Targets.Any(t => t.Sources.Count(s => ids.Contains(s.Id)) > 1))
                throw new ArgumentException("Choose valid sources, at most one mirror for each content group.");
            var revised = new List<CloudDownloadTarget>();
            foreach (var target in workset.Targets)
            {
                var selected = target.Sources.FirstOrDefault(s => ids.Contains(s.Id));
                if (selected != null && selected.Id != target.SelectedSourceId &&
                    (target.RemoteEntry != null || target.DownloadTaskId != null || target.TransportTaskId != null ||
                     target.DownloadArtifactId != null || target.ProcessedArtifactId != null || target.PublishedArtifactId != null))
                    return Wait(workset with {Targets = workset.Targets.Select(t => t with
                        {Problem = "This target already has transferred files or a download task. Start another run to use a different source."}).ToList()}, "cloudAttention");
                revised.Add(target with {SelectedSourceId = selected?.Id, Excluded = selected == null});
            }
            var excluded = workset.Targets.Where(t => revised.Any(r => r.Id == t.Id && r.Excluded)).ToList();
            if (excluded.Count > 0) await StopTargets(ctx, excluded, ct);
            workset = workset with {Targets = revised};
        }
        if (kind == "download.selectSources")
        {
            workset = workset with {Targets = CloudStoragePolicyBinding.Apply(workset.Targets,
                config.StoragePolicy, config.TargetStorageOverrides)};
            var ids = signal?.SourceIds ?? config.SourceIds;
            if (ids.Count > 0)
            {
                if (ids.Distinct().Count() != ids.Count || ids.Any(id => !workset.Targets.Any(t => t.Sources.Any(s => s.Id == id))))
                    throw new ArgumentException("The selection contains sources outside this saved workset.");
                if (workset.Targets.Any(t => t.Sources.Count(s => ids.Contains(s.Id)) > 1))
                    throw new ArgumentException("Choose at most one mirror for each content group.");
                if (workset.Targets.Any(t => t.Sources.FirstOrDefault(s => ids.Contains(s.Id)) is { } selected &&
                    selected.Id != t.SelectedSourceId && (t.RemoteEntry != null || DownloadWorksetRouting.HasDownloadCheckpoint(t))))
                    return Wait(workset with {Targets = workset.Targets.Select(t => t with
                        {Problem = "This target already has transferred files or a download task. Start another run to use a different source."}).ToList()}, "cloudAttention");
                workset = workset with {Targets = workset.Targets.Select(t => t with
                    {SelectedSourceId = t.Sources.FirstOrDefault(s => ids.Contains(s.Id))?.Id,
                        Excluded = !t.Sources.Any(s => ids.Contains(s.Id))}).ToList()};
            }
            else if (config.AllowUngrouped)
                workset = workset with {Targets = workset.Targets.Select(t => t with
                {SelectedSourceId = t.SelectedSourceId ?? t.Sources.OrderBy(s =>
                    {var i = config.PreferProviders.IndexOf(s.ProviderId); return i < 0 ? int.MaxValue : i;}).First().Id}).ToList()};
            workset = AssignSelectedAccounts(workset, account);
            if (workset.Targets.Any(t => !t.Excluded && t.SelectedSource == null)) return Wait(workset, "chooseSources");
            return WorkflowItemOutcome.ReplaceWith(workset);
        }
        workset = AssignSelectedAccounts(workset, account);
        // Resolve source indirection before making durable acquisition keys. The frozen source
        // keeps the original URL for history while all engines and reuse use the final locator.
        if (kind != "download.awaitCompletion" && ctx.Services.GetService<DownloadSourceResolver>() is { } resolver)
        {
            var resolved = new List<CloudDownloadTarget>();
            var resolutionFailed = false;
            foreach (var target in workset.Targets)
            {
                if (target.Excluded || target.SelectedSource == null || target.DownloadTaskId != null || target.TransportTaskId != null)
                { resolved.Add(target); continue; }
                try
                {
                    var source = await resolver.ResolveAsync(target.SelectedSource, ct);
                    resolved.Add(target with {Sources = target.Sources.Select(s => s.Id == source.Id ? source : s).ToList(), Problem = null});
                }
                catch (Exception error) when (error is ArgumentException or InvalidOperationException or IOException or System.Net.Http.HttpRequestException)
                {
                    resolutionFailed = true;
                    resolved.Add(target with {Problem = error.Message});
                }
            }
            workset = AssignSelectedAccounts(workset with {Targets = resolved}, account);
            if (resolutionFailed) return Wait(workset, "cloudAttention");
        }
        if (string.IsNullOrWhiteSpace(workset.OutputDirectory) && kind != "baidu.transfer") return Wait(workset, "chooseDirectory");
        if (!string.IsNullOrWhiteSpace(workset.OutputDirectory))
            foreach (var target in workset.Targets.Where(t => !t.Excluded))
            {
                try
                {
                    ctx.Services.GetRequiredService<IUserStoragePolicy>().EnsurePathAllowed(
                        CloudStageDirectoryPath.Download(target) ?? workset.OutputDirectory);
                }
                catch (UserStoragePathException error) when (kind == "download.awaitCompletion" && error.MountUnavailable)
                {
                    return Wait(workset with {Targets = workset.Targets.Select(t => ReferenceEquals(t, target)
                        ? t with {Problem = TargetProblem(error)} : t).ToList()}, "cloudAttention");
                }
            }
        if (kind != "download.awaitCompletion" && workset.Targets.Any(t => !t.Excluded && t.SelectedSource is { } selected &&
                DownloadMethodCatalog.Select(selected) is {EngineId: "rclone", AccountProvider: not null}) &&
            ctx.Services.GetService<DownloadMethodRegistry>() is { } accountRegistry)
        {
            try { workset = await DownloadWorksetRouting.BindAccountsAsync(workset, accountRegistry, account, ct); }
            catch (Exception error) when (error is InvalidOperationException or ArgumentException or IOException)
            { return Wait(workset with {Targets = workset.Targets.Select(t => t with {Problem = error.Message}).ToList()}, "cloudAttention"); }
        }
        if (kind is "download.enqueueSupported" or "baidu.download" && config.ReusePolicy == "force")
            workset = workset with {Targets = workset.Targets.Select(t => t with
                {AcquisitionNonce = ctx.RunId.ToString(),
                    DownloadTaskId = t.AcquisitionNonce == ctx.RunId.ToString() ? t.DownloadTaskId : null,
                    TransportTaskId = t.AcquisitionNonce == ctx.RunId.ToString() ? t.TransportTaskId : null,
                    DownloadArtifactId = t.AcquisitionNonce == ctx.RunId.ToString() ? t.DownloadArtifactId : null,
                    DownloadOperationId = t.AcquisitionNonce == ctx.RunId.ToString() ? t.DownloadOperationId : null,
                    Files = t.AcquisitionNonce == ctx.RunId.ToString() ? t.Files : [],
                    ProcessedArtifactId = null, ProcessedDirectory = null, ProcessedFiles = [], ProcessingIntentKey = null,
                    PublicationOperationId = null, PublishedArtifactId = null, PublishedDirectory = null, PublishedFiles = []}).ToList()};
        if (kind == "baidu.transfer")
        {
            var separate = config.DownloadLayout == "separateDirectory";
            if (workset.Targets.Any(t => t.SelectedSource?.ProviderId == "baidu" &&
                (t.DownloadLayout == "separateDirectory") != separate &&
                (t.DownloadArtifactId != null || t.ProcessedArtifactId != null || t.PublishedArtifactId != null)))
                return Wait(workset with {Targets = workset.Targets.Select(t => t with
                    {Problem = "This run already recorded a different download layout. Start a new run to reuse the download with the selected layout."}).ToList()}, "cloudAttention");
            workset = workset with {Targets = workset.Targets.Select(t => t with {DownloadLayout = config.DownloadLayout}).ToList()};
        }
        if (kind is "baidu.transfer" or "download.enqueueSupported" or "baidu.download" && config.ReusePolicy != "force")
            workset = (DownloadWorkset)await WorkflowArtifactPlanner.SatisfyDownstreamAsync(ctx, workset, ct);
        var artifacts = ctx.Services.GetService<DownloadArtifactCoordinator>();
        var operations = ctx.Services.GetRequiredService<CloudOperationService>();
        var updated = new List<CloudDownloadTarget>();
        foreach (var target in workset.Targets)
        {
            var next = target with {Problem = null, TransferDiagnostic = null};
            if (target.Excluded) {updated.Add(next); continue;}
            try
            {
                ct.ThrowIfCancellationRequested();
                if (next.ProcessedArtifactId != null || next.PublishedArtifactId != null) {updated.Add(next); continue;}
                if (artifacts != null && next.Files.Count == 0 && next.AcquisitionNonce == null &&
                    kind is "baidu.transfer" or "download.enqueueSupported" or "baidu.download")
                    next = await artifacts.TryReuseAsync(ctx, next, workset.OutputDirectory, ct);
                if (next.DownloadArtifactId != null && kind != "download.awaitCompletion")
                {updated.Add(next); continue;}
                var source = next.SelectedSource;
                if (source == null) throw new InvalidOperationException("Choose a download source.");
                if (source.ProviderId == "baidu" && string.IsNullOrWhiteSpace(next.AccountId))
                    next = next with {AccountId = await ctx.Services.GetRequiredService<Baidu.BaiduPcsAccountStore>().ResolveAccountIdAsync(null, ct)};
                if (this is ICloudTransferActivity transfer && transfer.Transfers(source) && target.RemoteEntry == null)
                {
                    if (string.IsNullOrWhiteSpace(next.AccountId)) throw new InvalidOperationException("Choose a Baidu account.");
                    var ready = await operations.Provider(source).CheckReadyAsync(next.AccountId, ct);
                    if (!ready.Available) throw new InvalidOperationException(ready.Problem);
                    var receipt = await operations.TransferAsync(checked((int)ctx.RunId), next, next.AccountId, config.RemoteDirectory, ct,
                        noRepeat: workset.NoRepeatExistingDownloads == true);
                    next = next with {RemoteEntry = receipt.Entry};
                }
                if (kind is "download.enqueueSupported" or "baidu.download" &&
                    (kind != "baidu.download" || source.ProviderId == "baidu"))
                {
                    if (source.ProviderId == "baidu" && source.Kind != "remote" && target.RemoteEntry == null)
                        throw new InvalidOperationException("Transfer this Baidu share before downloading it.");
                    if (source.ProviderId == "magnet" || source.Kind is "http" or "torrent")
                    {
                        if (signal?.ObserveExistingOnly == true) throw new InvalidOperationException("RECOVERY_DOWNLOAD_REVIEW_REQUIRED");
                        next = await QueueNative(ctx, workset, next, ct);
                    }
                    else next = next with {DownloadTaskId = await operations.EnqueueAsync(checked((int)ctx.RunId), next, workset.OutputDirectory, ct,
                        existingOnly: signal?.ObserveExistingOnly == true, noRepeat: workset.NoRepeatExistingDownloads == true)};
                }
                if (kind == "download.awaitCompletion")
                {
                    CompletedCloudDownloadReceipt? receipt = null;
                    var observed = next;
                    if (completedDownloadReceipts != null)
                    {
                        var db = ctx.Services.GetRequiredService<BakabaseDbContext>();
                        var id = next.DownloadTaskId!.Value;
                        receipt = await CompletedCloudDownloadReceipt.ObserveAsync(db, id, completedDownloadReceipts[id], ct);
                        if (next.DownloadArtifactId == null)
                            observed = next with {Files = receipt.Files.ToList(), DownloadDirectory = CloudDownloadPayloadRoot.SelectLayout(
                                receipt.Destination, receipt.Result.DownloadDirectory, receipt.Files, next)};
                    }
                    else
                    {
                        next = await Await(ctx, next, signal != null && signal.ObserveExistingOnly != true && workset.NoRepeatExistingDownloads != true, ct);
                        observed = next;
                    }
                    if (artifacts != null)
                    {
                        await ctx.ReportStage("verifyingFiles");
                        next = await artifacts.RecordAsync(ctx, observed, workset.OutputDirectory, ct, receipt);
                    }
                    else if (receipt != null) throw new InvalidOperationException("RECOVERY_FILES_UNVERIFIED");
                }
            }
            catch (OperationCanceledException)
            {
                var pending = updated.Concat(workset.Targets).Append(next).ToList();
                var db = ctx.Services.GetRequiredService<BakabaseDbContext>();
                var owned = await db.CloudDownloadOperations.AsNoTracking().Where(o => o.WorkflowRunId == ctx.RunId && o.DownloadTaskId != null)
                    .Select(o => o.DownloadTaskId!.Value).ToListAsync(CancellationToken.None);
                pending.AddRange(owned.Select(id => new CloudDownloadTarget {Id = "owned-" + id, DownloadTaskId = id}));
                await StopTargets(ctx, pending, CancellationToken.None);
                var http = ctx.Services.GetRequiredService<IHttpDownloadManager>();

                var bt = ctx.Services.GetRequiredService<IBtDownloadManager>();

                throw;
            }
            catch (Exception error) when (error is InvalidOperationException or ArgumentException or IOException or InvalidDataException)
            { next = next with {Problem = TargetProblem(error), TransferDiagnostic = (error as CloudProviderException)?.TransferDiagnostic}; }
            updated.Add(next);
        }
        workset = workset with {Targets = updated};
        return updated.Any(t => t.Problem != null) ? Wait(workset, "cloudAttention") : WorkflowItemOutcome.ReplaceWith(workset);
    }

    private static DownloadWorkset AssignSelectedAccounts(DownloadWorkset item, string? account) => item with
    {
        Targets = item.Targets.Select(t => t with
        {
            AccountId = t.SelectedSource?.ProviderId == "baidu" ? account ?? t.SelectedSource.AccountId ?? t.AccountId
                : t.SelectedSource is { } selected && DownloadMethodCatalog.Select(selected)?.AccountProvider != null
                    ? selected.AccountId ?? t.AccountId
                    : t.SelectedSource == null ? t.AccountId : null
        }).ToList()
    };

    private static WorkflowItemOutcome Wait(DownloadWorkset item, string reason) => WorkflowItemOutcome.Suspend(
        new WorkflowSuspension("cloud." + reason, JsonSerializer.Serialize(new {item.Targets, Reason = reason,
            item.OutputDirectory}, WorkflowJson.Options), item));

    private static async Task<WorkflowItemOutcome> ResumeSelectedDownloads(WorkflowExecutionContext ctx,
        DownloadWorkset workset, IReadOnlySet<string> selected, CancellationToken ct, bool revalidate = false, bool verifyExisting = false)
    {
        var artifacts = ctx.Services.GetService<DownloadArtifactCoordinator>();
        var recovery = revalidate ? ctx.Services.GetRequiredService<CloudDownloadRecoveryService>() : null;
        var equivalentOwners = new Dictionary<string, CloudDownloadTarget>(StringComparer.Ordinal);
        var equivalentResults = new Dictionary<int, CloudDownloadTarget>();
        var groupedNativeIds = workset.Targets.Where(t => t.DownloadTaskId != null)
            .GroupBy(t => t.DownloadTaskId!.Value).Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet();
        if (recovery != null)
        {
            foreach (var selection in workset.Targets.Where(t => selected.Contains(t.Id) && t.DownloadTaskId is { } id && groupedNativeIds.Contains(id)))
            {
                CloudDownloadRecoveryService.EquivalentGroup? group;
                try {group = await recovery.ReadEquivalentGroupAsync(checked((int)ctx.RunId), selection, workset.OutputDirectory, ct);}
                catch (Exception error) when (error is IOException or ArgumentException or InvalidOperationException or JsonException) {continue;}
                if (group?.Owner.Id != selection.Id || group.Members.Count(t => selected.Contains(t.Id)) != 1) continue;
                foreach (var member in group.Members) equivalentOwners.Add(member.Id, group.Owner);
            }
            selected = selected.Concat(equivalentOwners.Keys).ToHashSet(StringComparer.Ordinal);
        }
        var admissionProblems = new Dictionary<string, string>(StringComparer.Ordinal);
        var selectedTargets = workset.Targets.Where(t => selected.Contains(t.Id)).ToArray();
        var updated = new List<CloudDownloadTarget>();
        try
        {
            // Give the Download Center every selected original before observing any completion.
            // Keep calls sequential within this workflow scope, which shares a DbContext.
            if (!verifyExisting)
            {
                foreach (var target in selectedTargets)
                {
                    ct.ThrowIfCancellationRequested();
                    if (target.ProcessedArtifactId != null || target.PublishedArtifactId != null ||
                        equivalentOwners.TryGetValue(target.Id, out var owner) && owner.Id != target.Id) continue;
                    try
                    {
                        if (recovery != null)
                        {
                            var grouped = owner != null;
                            if (!grouped && groupedNativeIds.Contains(target.DownloadTaskId.GetValueOrDefault()))
                                throw new InvalidOperationException("RECOVERY_DOWNLOAD_IDENTITY_CHANGED");
                            await StartRevalidatedDownload(ctx, recovery, target, workset.OutputDirectory, grouped, ct);
                        }
                        else if (target.DownloadArtifactId == null)
                        {
                            // A human-selected retry still requires the unchanged original dispatch.
                            var manual = ActivatorUtilities.GetServiceOrCreateInstance<CloudManualDownloadResume>(ctx.Services);
                            await manual.ResumeAsync(checked((int)ctx.RunId), target, workset.OutputDirectory,
                                ctx.Services.GetRequiredService<DownloadTaskService>(), ct);
                        }
                    }
                    catch (Exception error) when (error is InvalidOperationException or ArgumentException or IOException or InvalidDataException)
                    { admissionProblems.Add(target.Id, TargetProblem(error)); }
                }
            }

            foreach (var target in workset.Targets)
            {
                // Preserve each unselected target's checkpoint, files and original problem.
                if (!selected.Contains(target.Id)) {updated.Add(target); continue;}
                var next = target with {Problem = null, TransferDiagnostic = null};
                var nativeId = target.DownloadTaskId.GetValueOrDefault();
                var grouped = equivalentOwners.TryGetValue(target.Id, out var owner);
                try
                {
                    ct.ThrowIfCancellationRequested();
                    if (admissionProblems.TryGetValue(owner?.Id ?? target.Id, out var problem))
                        next = next with {Problem = problem};
                    else if (next.ProcessedArtifactId == null && next.PublishedArtifactId == null)
                    {
                        if (grouped && equivalentResults.TryGetValue(nativeId, out var observed))
                        {
                            if (observed.Problem != null) throw new InvalidOperationException(observed.Problem);
                            next = next with {DownloadDirectory = observed.DownloadDirectory, Files = observed.Files,
                                DownloadOperationId = observed.DownloadOperationId, DownloadArtifactId = observed.DownloadArtifactId,
                                ExecutionStatus = observed.ExecutionStatus};
                        }
                        else if (verifyExisting)
                        {
                            await ctx.Services.GetRequiredService<CloudDownloadRecoveryService>().VerifyExistingAsync(
                                checked((int)ctx.RunId), target, workset.OutputDirectory,
                                ctx.Services.GetRequiredService<DownloadTaskService>(), ct);
                        }
                        if (!grouped || !equivalentResults.ContainsKey(nativeId)) next = await Await(ctx, next, retry: false, ct);
                        if (artifacts != null)
                            next = await artifacts.RecordAsync(ctx, next, workset.OutputDirectory, ct);
                    }
                }
                catch (Exception error) when (error is InvalidOperationException or ArgumentException or IOException or InvalidDataException)
                { next = next with {Problem = TargetProblem(error), TransferDiagnostic = (error as CloudProviderException)?.TransferDiagnostic}; }
                if (grouped) equivalentResults.TryAdd(nativeId, next);
                updated.Add(next);
            }
        }
        catch (OperationCanceledException)
        {
            await StopTargets(ctx, selectedTargets, CancellationToken.None);
            throw;
        }
        workset = workset with {Targets = updated};
        // A partial resume does not certify other unfinished targets or advance past their wait.
        return updated.Any(t => t.Problem != null || !t.Excluded && !selected.Contains(t.Id) &&
            t.DownloadArtifactId == null && t.ProcessedArtifactId == null && t.PublishedArtifactId == null)
            ? Wait(workset, "cloudAttention") : WorkflowItemOutcome.ReplaceWith(workset);
    }

    private static async Task StartRevalidatedDownload(WorkflowExecutionContext ctx, CloudDownloadRecoveryService recovery,
        CloudDownloadTarget target, string outputDirectory, bool grouped, CancellationToken ct)
    {
        var runId = checked((int)ctx.RunId);
        var check = await recovery.CheckAsync(runId, target, outputDirectory, true, ct, requireEquivalentGroup: grouped);
        if (!check.CanRetry || check.VerifyExistingOnly) throw new InvalidOperationException(check.ReasonCode == "RECOVERY_SOURCE_STILL_BLOCKED"
            ? check.FailureCode ?? check.ReasonCode : check.ReasonCode);
        var response = await ctx.Services.GetRequiredService<DownloadTaskService>().StartConditionally(
            target.DownloadTaskId!.Value, async token =>
            {
                var local = await recovery.CheckAsync(runId, target, outputDirectory, false, token, requireEquivalentGroup: grouped);
                if (!local.Candidate || local.VerifyExistingOnly) throw new InvalidOperationException(local.ReasonCode);
            }, ct, grouped ? async token =>
            {
                if ((await recovery.ReadEquivalentGroupAsync(runId, target, outputDirectory, token))?.Owner.Id != target.Id)
                    throw new InvalidOperationException("RECOVERY_DOWNLOAD_IDENTITY_CHANGED");
            } : null);
        if (response.Code != 0) throw new InvalidOperationException(response.Message ?? "RECOVERY_DOWNLOAD_REVIEW_REQUIRED");
    }

    private static string TargetProblem(Exception error) => error is UserStoragePathException {MountUnavailable: true}
        ? "LOCAL_STORAGE_UNAVAILABLE" : error.Message;

    internal static async Task<CloudDownloadTarget> QueueNative(WorkflowExecutionContext ctx, DownloadWorkset item, CloudDownloadTarget target, CancellationToken ct)
    {
        var source = target.SelectedSource!;
        var intent = DownloadArtifactCoordinator.DownloadIntent(target, item.OutputDirectory, ctx.Services.GetService<FileProcessingWorkspaceResolver>());
        var generation = 1;
        if (ctx.Services.GetService<IArtifactOperationStore>() is { } store)
        {
            var claim = await store.ClaimAsync(new(intent, intent, "download", DownloadArtifactCoordinator.Source(target),
                    AllowRetry: item.NoRepeatExistingDownloads != true),
                DownloadArtifactCoordinator.Consumer(ctx, target), TimeSpan.FromMinutes(2), ct);
            if (claim.Artifact != null)
                return await ctx.Services.GetRequiredService<DownloadArtifactCoordinator>().TryReuseAsync(ctx, target, item.OutputDirectory, ct);
            if (claim.Status == ArtifactClaimStatus.StorageUnavailable)
                throw new UserStoragePathException("The existing download storage is unavailable. Reconnect it before continuing; no new download was started.");
            if (claim.Status == ArtifactClaimStatus.NeedsReconciliation)
                throw new InvalidOperationException(item.NoRepeatExistingDownloads == true ? "RECOVERY_DOWNLOAD_REVIEW_REQUIRED" :
                    "This acquisition needs reconciliation before it can continue.");
            generation = (await store.GetOperationAsync(intent, ct))!.Attempt;
            if (claim.Status == ArtifactClaimStatus.Busy) target = target with {ExecutionStatus = "waitingShared"};
        }
        else if (item.NoRepeatExistingDownloads == true) throw new InvalidOperationException("RECOVERY_DOWNLOAD_REVIEW_REQUIRED");
        var key = intent + ":" + generation;
        var explicitDirectory = CloudStageDirectoryPath.Download(target);
        var directory = explicitDirectory != null
            ? await CloudStageDirectoryPath.PrepareDownloadAsync(target, ctx.Services.GetRequiredService<IUserStoragePolicy>(),
                ctx.Services.GetService<FileProcessingWorkspaceResolver>(), ct)
            : target.DownloadDirectoryScope is { } scope
            ? await ctx.Services.GetRequiredService<FileProcessingWorkspaceResolver>().EnsureScopedDirectoryAsync(scope, ct)
            : Path.Combine(item.OutputDirectory, target.StoragePolicy == null ? "cloud-" + intent[..16] :
                ArtifactStoragePolicies.RenderDownload(target.StoragePolicy, target.Title, intent, source.ProviderId), generation.ToString());
        if (explicitDirectory != null && generation > 1 && Directory.EnumerateFileSystemEntries(directory).Any())
            throw new InvalidOperationException("RECOVERY_DOWNLOAD_REVIEW_REQUIRED");
        if (explicitDirectory == null && target.DownloadDirectoryScope != null && generation > 1)
            directory = Path.Combine(directory, "attempt-" + generation.ToString(System.Globalization.CultureInfo.InvariantCulture));
        if (source.ProviderId == "magnet" || source.Kind == "torrent")
        {
            var manager = ctx.Services.GetRequiredService<IBtDownloadManager>();
            directory = (await manager.FindByIdempotencyKeyAsync(key, ct))?.OutputDirectory ?? directory;
            var job = await manager.EnsureQueuedAsync(new BtDownloadRequest
                {MagnetUri = source.ProviderId == "magnet" ? source.Url : null, TorrentUrl = source.Kind == "torrent" ? source.Url : null,
                    Name = target.Title, OutputDirectory = directory, IdempotencyKey = key,
                    SourceKind = "workflow", SourceId = intent, WriteContext = new("workflow", ctx.RunId.ToString())}, ct);
            return target with {TransportTaskId = "bt:" + job.Id};
        }
        var httpManager = ctx.Services.GetRequiredService<IHttpDownloadManager>();
        directory = (await httpManager.FindByIdempotencyKeyAsync(key, ct))?.OutputDirectory ?? directory;
        var http = await httpManager.EnsureQueuedAsync(new HttpDownloadRequest
            {Url = source.Url, Name = target.Title, OutputDirectory = directory, IdempotencyKey = key,
                SourceKind = "workflow", SourceId = intent, WriteContext = new("workflow", ctx.RunId.ToString())}, ct);
        return target with {TransportTaskId = "http:" + http.Id};
    }

    private static async Task<CloudDownloadTarget> Await(WorkflowExecutionContext ctx, CloudDownloadTarget target, bool retry, CancellationToken ct)
    {
        if (target.DownloadArtifactId != null || target.PublishedArtifactId != null) return target;
        if (target.TransportTaskId is { } transport)
        {
            var split = transport.IndexOf(':'); var id = transport[(split + 1)..];
            if (transport.StartsWith("bt:", StringComparison.Ordinal))
            {
                var manager = ctx.Services.GetRequiredService<IBtDownloadManager>();
                if (retry) await manager.ResumeAsync(id, ct);
                await ctx.ReportStage("downloading");
                var job = await manager.WaitForCompletionAsync(id, stopOnCancellation: false, ct: ct);
                return target with {Files = job.Files.ToList(), DownloadDirectory = job.OutputDirectory};
            }
            var http = ctx.Services.GetRequiredService<IHttpDownloadManager>();
            if (retry) await http.ResumeAsync(id, ct);
            await ctx.ReportStage("downloading");
            var result = await http.WaitForCompletionAsync(id, stopOnCancellation: false, ct: ct);
            return target with {Files = result.Files.ToList(), DownloadDirectory = result.OutputDirectory};
        }
        if (target.DownloadTaskId is not { } taskId) throw new InvalidOperationException("This source has not been queued. Choose a supported source and run the download node.");
        var db = ctx.Services.GetRequiredService<BakabaseDbContext>();
        var service = ctx.Services.GetRequiredService<DownloadTaskService>();
        if (retry)
        {
            var state = await db.DownloadTasks.AsNoTracking().SingleOrDefaultAsync(t => t.Id == taskId, ct);
            if (state?.Status is DownloadTaskDbModelStatus.Failed or DownloadTaskDbModelStatus.Disabled)
                await service.Start(t => t.Id == taskId, targeted: true);
        }
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var row = await db.DownloadTasks.AsNoTracking().SingleOrDefaultAsync(t => t.Id == taskId, ct)
                ?? throw new InvalidOperationException("The cloud download was deleted.");
            if (row.Status == DownloadTaskDbModelStatus.Complete)
            {
                var result = await db.DownloadResults.AsNoTracking().Where(r => r.DownloadTaskId == taskId).OrderByDescending(r => r.Id).FirstOrDefaultAsync(ct);
                if (result == null) throw new InvalidDataException("The completed download has no saved file manifest. Inspect the task before continuing.");
                List<string> files;
                try { files = JsonSerializer.Deserialize<List<string>>(result.FilesJson) ?? []; }
                catch (JsonException error) { throw new InvalidDataException("The completed download file manifest is invalid.", error); }
                CloudDownloadManifest.Validate(row.DownloadPath, files);
                return target with {Files = files,
                    DownloadDirectory = CloudDownloadPayloadRoot.SelectLayout(row.DownloadPath, result.DownloadDirectory, files, target)};
            }
            if (row.Status is DownloadTaskDbModelStatus.Failed or DownloadTaskDbModelStatus.Disabled)
                throw new InvalidOperationException(row.Message ?? "The cloud download stopped. Resume this workflow to retry it.");
            await ctx.ReportStage("downloading");
            await Task.Delay(1000, ct);
        }
    }

    private static async Task StopTargets(WorkflowExecutionContext ctx, IEnumerable<CloudDownloadTarget> targets, CancellationToken ct)
    {
        foreach (var target in targets)
        {
            // A shared acquisition outlives an individual workflow consumer.
            if (target.AcquisitionNonce == null) continue;
            if (target.DownloadTaskId is { } id) await ctx.Services.GetRequiredService<DownloadTaskService>().Stop(t => t.Id == id);
            if (target.TransportTaskId?.StartsWith("http:", StringComparison.Ordinal) == true)
                await ctx.Services.GetRequiredService<IHttpDownloadManager>().StopAsync(target.TransportTaskId[5..], ct);
            if (target.TransportTaskId?.StartsWith("bt:", StringComparison.Ordinal) == true)
                await ctx.Services.GetRequiredService<IBtDownloadManager>().StopAsync(target.TransportTaskId[3..], ct);
        }
    }
}
public sealed class SelectCloudSourcesActivity() : CloudActivity("download.selectSources", "Select download sources", "workflow.activity.cloudSelect.description");
public interface ICloudTransferActivity : IWorkflowActivity
{
    bool Transfers(CloudSource source);
}
public sealed class BaiduTransferActivity() : CloudActivity("baidu.transfer", "Transfer Baidu share", "workflow.activity.baiduTransfer.description"), ICloudTransferActivity
{
    public bool Transfers(CloudSource source) => source.ProviderId == "baidu" && source.Kind != "remote";
}
public sealed class BaiduDownloadActivity() : CloudActivity("baidu.download", "Download Baidu files", "workflow.activity.baiduDownload.description");
public sealed class EnqueueCloudSourcesActivity() : CloudActivity("download.enqueueSupported", "Route selected downloads", "workflow.activity.cloudEnqueue.description");
public sealed class AwaitCloudDownloadsActivity() : CloudActivity("download.awaitCompletion", "Wait for downloads", "workflow.activity.cloudAwait.description");
