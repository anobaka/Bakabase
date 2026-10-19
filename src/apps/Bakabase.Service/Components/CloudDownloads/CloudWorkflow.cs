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
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Service.Components.CloudDownloads;

public static class CloudWorkflow
{
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
    public List<string> PreferProviders { get; init; } = ["baidu", "googleDrive", "gofile"];
    public bool AllowUngrouped { get; init; }
    public string? AccountId { get; init; }
    public string RemoteDirectory { get; init; } = "/Bakabase";
    public string? OutputDirectory { get; init; }
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
            "run:" + ctx.RunId, ctx.GetConfig<CloudWorkflowConfig>()?.OutputDirectory ?? "")));
    }
}

public abstract class CloudActivity(string kind, string name, string descriptionKey) : IResumableWorkflowActivity
{
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
        var config = ctx.GetConfig<CloudWorkflowConfig>() ?? new();
        if (!string.IsNullOrWhiteSpace(signal?.OutputDirectory ?? config.OutputDirectory))
            workset = workset with {OutputDirectory = signal?.OutputDirectory ?? config.OutputDirectory!};
        var account = signal?.AccountId ?? config.AccountId;
        if (!string.IsNullOrWhiteSpace(account))
        {
            if (workset.Targets.Any(t => (t.RemoteEntry != null || t.DownloadTaskId != null) &&
                t.SelectedSource?.ProviderId == "baidu" && t.AccountId != account))
                return Wait(workset with {Targets = workset.Targets.Select(t => t with
                    {Problem = "The transferred files and download task belong to the original account. Start another run to use a different account."}).ToList()}, "cloudAttention");
            workset = workset with {Targets = workset.Targets.Select(t => t with
                {AccountId = (t.SelectedSource?.ProviderId == "baidu" || t.SelectedSource == null && t.Sources.Any(s => s.ProviderId == "baidu")) ? account : null}).ToList()};
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
                    (target.RemoteEntry != null || target.DownloadTaskId != null || target.TransportTaskId != null))
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
            var ids = signal?.SourceIds ?? config.SourceIds;
            if (ids.Count > 0)
            {
                if (ids.Distinct().Count() != ids.Count || ids.Any(id => !workset.Targets.Any(t => t.Sources.Any(s => s.Id == id))))
                    throw new ArgumentException("The selection contains sources outside this saved workset.");
                if (workset.Targets.Any(t => t.Sources.Count(s => ids.Contains(s.Id)) > 1))
                    throw new ArgumentException("Choose at most one mirror for each content group.");
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
        if (string.IsNullOrWhiteSpace(workset.OutputDirectory) && kind != "baidu.transfer") return Wait(workset, "chooseDirectory");
        if (!string.IsNullOrWhiteSpace(workset.OutputDirectory)) ctx.Services.GetRequiredService<IUserStoragePolicy>().EnsurePathAllowed(workset.OutputDirectory);
        var operations = ctx.Services.GetRequiredService<CloudOperationService>();
        var updated = new List<CloudDownloadTarget>();
        foreach (var target in workset.Targets)
        {
            var next = target with {Problem = null};
            if (target.Excluded) {updated.Add(next); continue;}
            try
            {
                ct.ThrowIfCancellationRequested();
                var source = target.SelectedSource;
                if (source == null) throw new InvalidOperationException("Choose a download source.");
                if (source.ProviderId == "baidu" && string.IsNullOrWhiteSpace(next.AccountId))
                    next = next with {AccountId = await ctx.Services.GetRequiredService<Baidu.BaiduPcsAccountStore>().ResolveAccountIdAsync(null, ct)};
                if (kind == "baidu.transfer" && source.ProviderId == "baidu" && source.Kind != "remote" && target.RemoteEntry == null)
                {
                    if (string.IsNullOrWhiteSpace(next.AccountId)) throw new InvalidOperationException("Choose a Baidu account.");
                    var ready = await operations.Provider("baidu").CheckReadyAsync(next.AccountId, ct);
                    if (!ready.Available) throw new InvalidOperationException(ready.Problem);
                    var receipt = await operations.TransferAsync(checked((int)ctx.RunId), next, next.AccountId, config.RemoteDirectory, ct);
                    next = next with {RemoteEntry = receipt.Entry};
                }
                if (kind is "download.enqueueSupported" or "baidu.download" &&
                    (kind != "baidu.download" || source.ProviderId == "baidu"))
                {
                    if (source.ProviderId == "baidu" && source.Kind != "remote" && target.RemoteEntry == null)
                        throw new InvalidOperationException("Transfer this Baidu share before downloading it.");
                    if (source.ProviderId == "magnet" || source.Kind is "http" or "torrent")
                        next = await QueueNative(ctx, workset, next, ct);
                    else next = next with {DownloadTaskId = await operations.EnqueueAsync(checked((int)ctx.RunId), next, workset.OutputDirectory, ct)};
                }
                if (kind == "download.awaitCompletion") next = await Await(ctx, next, signal != null, ct);
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
                foreach (var job in (await http.ListAsync(CancellationToken.None)).Where(j => j.WorkflowRunId == ctx.RunId)) await http.StopAsync(job.Id);
                var bt = ctx.Services.GetRequiredService<IBtDownloadManager>();
                foreach (var job in (await bt.ListAsync(CancellationToken.None)).Where(j => j.WorkflowRunId == ctx.RunId)) await bt.StopAsync(job.Id);
                throw;
            }
            catch (Exception error) when (error is InvalidOperationException or ArgumentException or IOException or InvalidDataException)
            { next = next with {Problem = error.Message}; }
            updated.Add(next);
        }
        workset = workset with {Targets = updated};
        return updated.Any(t => t.Problem != null) ? Wait(workset, "cloudAttention") : WorkflowItemOutcome.ReplaceWith(workset);
    }

    private static DownloadWorkset AssignSelectedAccounts(DownloadWorkset item, string? account) => item with
    {
        Targets = item.Targets.Select(t => t with
        {
            AccountId = t.SelectedSource?.ProviderId == "baidu" ? account ?? t.AccountId
                : t.SelectedSource == null && t.Sources.Any(s => s.ProviderId == "baidu") ? t.AccountId : null
        }).ToList()
    };

    private static WorkflowItemOutcome Wait(DownloadWorkset item, string reason) => WorkflowItemOutcome.Suspend(
        new WorkflowSuspension("cloud." + reason, JsonSerializer.Serialize(new {item.Targets, Reason = reason,
            item.OutputDirectory}, WorkflowJson.Options), item));

    private static async Task<CloudDownloadTarget> QueueNative(WorkflowExecutionContext ctx, DownloadWorkset item, CloudDownloadTarget target, CancellationToken ct)
    {
        var source = target.SelectedSource!;
        var key = $"cloud:{ctx.RunId}:{target.Id}";
        var directory = Path.Combine(item.OutputDirectory, "cloud-" + target.Id[..16]);
        if (source.ProviderId == "magnet" || source.Kind == "torrent")
        {
            var job = await ctx.Services.GetRequiredService<IBtDownloadManager>().EnsureQueuedAsync(new BtDownloadRequest
                {MagnetUri = source.ProviderId == "magnet" ? source.Url : null, TorrentUrl = source.Kind == "torrent" ? source.Url : null,
                    Name = target.Title, OutputDirectory = directory, IdempotencyKey = key,
                    SourceKind = "workflow", SourceId = ctx.RunId.ToString(), WorkflowRunId = checked((int)ctx.RunId), WorkflowDefinitionId = ctx.WorkflowDefinitionId}, ct);
            return target with {TransportTaskId = "bt:" + job.Id};
        }
        var http = await ctx.Services.GetRequiredService<IHttpDownloadManager>().EnsureQueuedAsync(new HttpDownloadRequest
            {Url = source.Url, Name = target.Title, OutputDirectory = directory, IdempotencyKey = key,
                SourceKind = "workflow", SourceId = ctx.RunId.ToString(), WorkflowRunId = checked((int)ctx.RunId), WorkflowDefinitionId = ctx.WorkflowDefinitionId}, ct);
        return target with {TransportTaskId = "http:" + http.Id};
    }

    private static async Task<CloudDownloadTarget> Await(WorkflowExecutionContext ctx, CloudDownloadTarget target, bool retry, CancellationToken ct)
    {
        if (target.TransportTaskId is { } transport)
        {
            var split = transport.IndexOf(':'); var id = transport[(split + 1)..];
            if (transport.StartsWith("bt:", StringComparison.Ordinal))
            {
                var manager = ctx.Services.GetRequiredService<IBtDownloadManager>();
                if (retry) await manager.ResumeAsync(id, ct);
                var job = await manager.WaitForCompletionAsync(id, stopOnCancellation: true, ct: ct);
                return target with {Files = job.Files.ToList()};
            }
            var http = ctx.Services.GetRequiredService<IHttpDownloadManager>();
            if (retry) await http.ResumeAsync(id, ct);
            var result = await http.WaitForCompletionAsync(id, stopOnCancellation: true, ct: ct);
            return target with {Files = result.Files.ToList()};
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
                return target with {Files = files};
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
            if (target.DownloadTaskId is { } id) await ctx.Services.GetRequiredService<DownloadTaskService>().Stop(t => t.Id == id);
            if (target.TransportTaskId?.StartsWith("http:", StringComparison.Ordinal) == true)
                await ctx.Services.GetRequiredService<IHttpDownloadManager>().StopAsync(target.TransportTaskId[5..], ct);
            if (target.TransportTaskId?.StartsWith("bt:", StringComparison.Ordinal) == true)
                await ctx.Services.GetRequiredService<IBtDownloadManager>().StopAsync(target.TransportTaskId[3..], ct);
        }
    }
}
public sealed class SelectCloudSourcesActivity() : CloudActivity("download.selectSources", "Select download sources", "workflow.activity.cloudSelect.description");
public sealed class BaiduTransferActivity() : CloudActivity("baidu.transfer", "Transfer Baidu share", "workflow.activity.baiduTransfer.description");
public sealed class BaiduDownloadActivity() : CloudActivity("baidu.download", "Download Baidu files", "workflow.activity.baiduDownload.description");
public sealed class EnqueueCloudSourcesActivity() : CloudActivity("download.enqueueSupported", "Route selected downloads", "workflow.activity.cloudEnqueue.description");
public sealed class AwaitCloudDownloadsActivity() : CloudActivity("download.awaitCompletion", "Wait for downloads", "workflow.activity.cloudAwait.description");
