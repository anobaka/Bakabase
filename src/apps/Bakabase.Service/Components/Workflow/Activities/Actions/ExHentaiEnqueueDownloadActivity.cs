using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models.Input;
using Bakabase.InsideWorld.Business.Components.Downloader.Components.Downloaders.ExHentai;
using Bakabase.InsideWorld.Business.Components.Downloader.Services;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.Modules.Subscription.Abstractions.Models.Domain;
using Bakabase.Modules.Subscription.Workflow;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Bakabase.Service.Components.Workflow.Activities.Actions;

/// <summary>
/// Takes a <see cref="SubscriptionItem"/> whose <c>Url</c> points at an ExHentai gallery
/// and enqueues a single-work download task. The ExHentai downloader helper handles
/// validation, default options (PreferTorrent), and naming.
/// </summary>
public class ExHentaiEnqueueDownloadActivity : IWorkflowActivity
{
    public string Kind { get; } = DownloaderWorkflowActivityKinds.EnqueueGallery;
    public string DisplayName => "Enqueue as ExHentai download";
    public WorkflowActivityCategory Category => WorkflowActivityCategory.Action;
    public string Group => WorkflowActivityGroups.ExHentai;
    // Source-specific: only operates on ExHentai galleries. Side-effect action → passthrough.
    public IReadOnlyList<string> AcceptedInputItemTypes { get; } = [WorkflowItemTypes.ExHentaiGallery];

    public async Task<WorkflowItemOutcome> ProcessItemAsync(
        WorkflowExecutionContext ctx, object item, CancellationToken ct)
    {
        if (item is not SubscriptionItem si)
        {
            ctx.Logger.LogWarning(
                "ExHentaiEnqueueDownloadActivity ignoring item of unexpected type {Type}", item.GetType());
            return WorkflowItemOutcome.KeepItem;
        }

        if (string.IsNullOrWhiteSpace(si.Url))
        {
            ctx.Logger.LogWarning("ExHentai gallery item #{Id} has no URL — skipping", si.Id);
            return WorkflowItemOutcome.KeepItem;
        }

        var cfg = ctx.GetConfig<Config>() ?? new Config();

        var downloaderHelper = ctx.Services.GetRequiredService<ExHentaiDownloaderHelper>();
        var downloadTaskService = ctx.Services.GetRequiredService<DownloadTaskService>();

        var input = new DownloadTaskAddInputModel
        {
            ThirdPartyId = ThirdPartyId.ExHentai,
            Type = (int)ExHentaiDownloadTaskType.SingleWork,
            Keys = [si.Url],
            Names = string.IsNullOrEmpty(si.Title) ? null : [si.Title],
            Interval = cfg.IntervalMs,
            AutoRetry = cfg.AutoRetry,
        };

        var tasks = await downloaderHelper.BuildTasks(input);
        if (tasks.Length == 0)
        {
            ctx.Logger.LogWarning(
                "ExHentai helper produced no tasks for {Url} — likely failed validation", si.Url);
            return WorkflowItemOutcome.KeepItem;
        }

        await downloadTaskService.AddRange(tasks);
        return WorkflowItemOutcome.KeepItem;
    }

    private sealed record Config
    {
        /// <summary>Per-task interval (ms) — must be ≥ 1000 (ExHentai rate limit).</summary>
        public int IntervalMs { get; init; } = 1000;

        public bool AutoRetry { get; init; } = true;
    }
}
