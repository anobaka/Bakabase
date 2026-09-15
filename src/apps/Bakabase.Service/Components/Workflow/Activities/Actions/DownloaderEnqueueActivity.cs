using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Services;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Components;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models.Input;
using Bakabase.InsideWorld.Business.Components.Downloader.Components;
using Bakabase.InsideWorld.Business.Components.Downloader.Services;
using Bakabase.Modules.Acquisition.Abstractions.Services;
using Bakabase.Modules.Subscription.Abstractions.Models.Domain;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Bakabase.Service.Components.Workflow.Activities.Actions;

/// <summary>
/// Hands a link to whichever downloader can fetch it.
/// <para>
/// This is the ExHentai-specific enqueue action with the platform taken out of it: the link says
/// which downloader is meant (<see cref="DownloadTargetResolver"/>), so a chain that ends in
/// "download this" is one step regardless of where the thing came from.
/// </para>
/// </summary>
public class DownloaderEnqueueActivity : IWorkflowActivity
{
    public string Kind { get; } = DownloaderWorkflowActivityKinds.Enqueue;
    public string DisplayName => "Download it";
    public WorkflowActivityCategory Category => WorkflowActivityCategory.Action;
    public string Group => WorkflowActivityGroups.Downloader;

    // Anything that carries a link. A side-effect action, so the item passes through unchanged.
    public IReadOnlyList<string> AcceptedInputItemTypes { get; } =
    [
        WorkflowItemTypes.SubscriptionAny,
        WorkflowItemTypes.ExHentaiGallery,
        WorkflowItemTypes.Resource
    ];

    public async Task<WorkflowItemOutcome> ProcessItemAsync(WorkflowExecutionContext ctx, object item,
        CancellationToken ct)
    {
        var (url, name) = await ReadLink(ctx, item);

        if (string.IsNullOrWhiteSpace(url)) return WorkflowItemOutcome.KeepItem;

        if (!DownloadTargetResolver.TryResolve(url, out var target))
        {
            // Naming the link rather than the item: the user's next question is always "which one?",
            // and a resource id does not answer it.
            ctx.Logger.LogWarning("No downloader here can fetch {Url} — leaving it alone", url);

            return WorkflowItemOutcome.KeepItem;
        }

        var cfg = ctx.GetConfig<Config>() ?? new Config();

        var input = new DownloadTaskAddInputModel
        {
            ThirdPartyId = target.ThirdPartyId,
            Type = target.TaskType,
            Keys = [target.Key],
            Names = string.IsNullOrWhiteSpace(name) ? null : [name],
            Interval = cfg.IntervalMs,
            AutoRetry = cfg.AutoRetry
        };

        var helper = ctx.Services.GetRequiredService<IDownloaderFactory>()
            .GetHelper(target.ThirdPartyId, target.TaskType);

        var tasks = await helper.BuildTasks(input);

        if (tasks.Length == 0)
        {
            ctx.Logger.LogWarning("{Platform} refused {Url} — nothing was queued", target.ThirdPartyId, url);

            return WorkflowItemOutcome.KeepItem;
        }

        await ctx.Services.GetRequiredService<DownloadTaskService>().AddRange(tasks);

        return WorkflowItemOutcome.KeepItem;
    }

    /// <summary>
    /// The link this item is about, and what to call what comes back.
    /// <para>
    /// A subscription item carries both. A resource carries neither directly — its links are its
    /// acquisition leads, so the first of those is what it means by "where to get it".
    /// </para>
    /// </summary>
    private static async Task<(string? Url, string? Name)> ReadLink(WorkflowExecutionContext ctx,
        object item)
    {
        switch (item)
        {
            case SubscriptionItem si:
                return (si.Url, si.Title);
            case IHasResourceId hasResource:
            {
                var leads = await ctx.Services.GetRequiredService<IAcquisitionLeadService>()
                    .GetByResourceId(hasResource.ResourceId);

                return (leads.Count > 0 ? leads[0].Value : null, null);
            }
            default:
                ctx.Logger.LogWarning("DownloaderEnqueueActivity ignoring item of unexpected type {Type}",
                    item.GetType());

                return (null, null);
        }
    }

    private sealed record Config
    {
        /// <summary>
        /// Per-task interval in milliseconds. A thousand is the slowest platform's floor rather than
        /// a preference — going faster gets an account banned, not throttled.
        /// </summary>
        public int IntervalMs { get; init; } = 1000;

        public bool AutoRetry { get; init; } = true;
    }
}
