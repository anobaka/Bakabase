using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models;
using Bakabase.Modules.Acquisition.Components.Workflow;
using Bakabase.Modules.Acquisition.Abstractions.Components;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Service.Components.Downloader;

public static class DownloadResultWorkflow
{
    public const string Trigger = "downloader.resultReady";
    public const string ItemType = "item.downloader.result";
    public const string FetchTorrent = "action.downloader.fetchTorrentResult";
    public const string PrepareResource = "transform.downloader.prepareResource";
    public const string BuiltinName = "Download torrent contents";

    public static IServiceCollection AddDownloadResultWorkflows(this IServiceCollection services)
    {
        services.AddScoped<DownloadResultWorkflowService>();
        services.AddScoped<IAcquisitionContentsObserver>(sp => sp.GetRequiredService<DownloadResultWorkflowService>());
        services.AddSingleton<IWorkflowTrigger, DownloadResultReadyTrigger>();
        services.AddSingleton<IWorkflowItemTypeDescriptor, DownloadResultItemTypeDescriptor>();
        services.AddSingleton<IWorkflowActivity, FetchTorrentResultActivity>();
        services.AddSingleton<IWorkflowActivity, PrepareDownloadResultResourceActivity>();
        return services;
    }
}

/// <summary>The result id is authoritative. Display fields never authorize a file or another task.</summary>
public sealed record DownloadResultReadyPayload(int ResultId, DownloadResultKind Kind, string? Name);

public sealed record DownloadResultItem : ITextWorkpiece
{
    public int ResultId { get; init; }
    public string WorkingName { get; init; } = "";
    public string WorkingText => WorkingName;
    public object WithWorkingText(string workingText) => this with {WorkingName = workingText};
}

public sealed class DownloadResultItemTypeDescriptor : IWorkflowItemTypeDescriptor
{
    public string ItemType => DownloadResultWorkflow.ItemType;
    public string DisplayName => "Downloader: work result";
    public Type ClrType => typeof(DownloadResultItem);
}

public sealed class DownloadResultReadyTrigger : IWorkflowTrigger
{
    public string Kind => DownloadResultWorkflow.Trigger;
    public string DisplayName => "Download result ready";
    public WorkflowActivationMode ActivationMode => WorkflowActivationMode.Module;
    public string SourceModule => "downloader";
    public string Description => "Start or retry from a downloader result. Each saved result runs only its configured workflow; results owned by an acquisition continue that original run and never start an independent result workflow.";
    public string DescriptionKey => "workflow.trigger.downloaderResultReady.description";
    public bool SupportsManualRun => false;
    public Type PayloadType => typeof(DownloadResultReadyPayload);
    public bool Matches(object payload, string? triggerFilterJson)
    {
        if (payload is not DownloadResultReadyPayload result || result.ResultId <= 0) return false;
        var filter = string.IsNullOrWhiteSpace(triggerFilterJson) ? null :
            JsonSerializer.Deserialize<Filter>(triggerFilterJson, WorkflowJson.Options);
        return filter?.Kinds is not {Length: > 0} kinds || kinds.Contains((int)result.Kind);
    }
    public IReadOnlyList<object> ExtractItems(object payload) => payload is DownloadResultReadyPayload result
        ? [new DownloadResultItem {ResultId = result.ResultId, WorkingName = result.Name ?? ""}]
        : [];
    public string ResolveOutputItemType(string? triggerFilterJson) => DownloadResultWorkflow.ItemType;
    private sealed record Filter(int[]? Kinds);
}

public sealed class FetchTorrentResultActivity : IWorkflowActivity
{
    public string Kind => DownloadResultWorkflow.FetchTorrent;
    public string DisplayName => "Download torrent contents";
    public string Description => "Downloads the files described by a saved torrent using the shared BitTorrent engine. Already downloaded content is reused; ordinary files pass through.";
    public string DescriptionKey => "workflow.activity.downloaderFetchTorrentResult.description";
    public string Group => "downloader";
    public WorkflowActivityCategory Category => WorkflowActivityCategory.Action;
    public IReadOnlyList<string> AcceptedInputItemTypes => [DownloadResultWorkflow.ItemType];
    public sealed record Config {public int TimeoutMinutes { get; init; } = 240;}
    public Task<IReadOnlyList<WorkflowValidationIssue>> ValidateConfigAsync(WorkflowValidationContext context,
        CancellationToken ct)
    {
        var config = JsonSerializer.Deserialize<Config>(context.ConfigJson is {Length: > 0} json ? json : "{}", WorkflowJson.Options) ?? new();
        return Task.FromResult<IReadOnlyList<WorkflowValidationIssue>>(config.TimeoutMinutes is >= 1 and <= 43200 ? [] :
            [new WorkflowValidationIssue {Code = "timeoutInvalid", Message = "The download timeout must be between 1 and 43200 minutes.", MessageKey = "workflow.validation.acquisition.timeoutInvalid"}]);
    }
    public async Task<WorkflowItemOutcome> ProcessItemAsync(WorkflowExecutionContext ctx, object item, CancellationToken ct)
    {
        var result = item as DownloadResultItem ?? throw new InvalidOperationException("This node needs a download result.");
        await ctx.Services.GetRequiredService<DownloadResultWorkflowService>().DownloadContentsAsync(result.ResultId,
            checked((int)ctx.RunId), (ctx.GetConfig<Config>() ?? new()).TimeoutMinutes, ctx.ReportProgress, ct);
        return WorkflowItemOutcome.KeepItem;
    }
}

public sealed class PrepareDownloadResultResourceActivity : IWorkflowActivity
{
    public string Kind => DownloadResultWorkflow.PrepareResource;
    public string DisplayName => "Prepare downloaded content for import";
    public string Description => "Matches or creates the resource for actual downloaded content. Follow with placement and resource association nodes to import it.";
    public string DescriptionKey => "workflow.activity.downloaderPrepareResource.description";
    public string Group => "downloader";
    public WorkflowActivityCategory Category => WorkflowActivityCategory.Transform;
    public IReadOnlyList<string> AcceptedInputItemTypes => [DownloadResultWorkflow.ItemType];
    public WorkflowItemTypeBehavior OutputBehavior => WorkflowItemTypeBehavior.Fixed;
    public string FixedOutputItemType => AcquisitionWorkflowKinds.ItemAcquisition;
    public async Task<WorkflowItemOutcome> ProcessItemAsync(WorkflowExecutionContext ctx, object item, CancellationToken ct)
    {
        var result = item as DownloadResultItem ?? throw new InvalidOperationException("This node needs a download result.");
        return WorkflowItemOutcome.ReplaceWith(await ctx.Services.GetRequiredService<DownloadResultWorkflowService>()
            .PrepareResourceAsync(result.ResultId, checked((int)ctx.RunId), result.WorkingName, ct));
    }
}
