using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.Configurations.Models.Domain;
using Bakabase.InsideWorld.Business.Components.PostParser.Fetchers;
using Bakabase.InsideWorld.Business.Components.PostParser.Models.Domain.Constants;
using Bakabase.Modules.AI.Models.Domain;
using Bakabase.Modules.AI.Services;
using Bakabase.Modules.PostParser.Models.Domain;
using Bakabase.Modules.PostParser.Services;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Bootstrap.Components.Configuration.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.InsideWorld.Business.Components.PostParser.Workflow;

/// <summary>The receipt is a selection from the saved quote, never a price suggested by an LLM.</summary>
public sealed record PostParserPurchaseSignal
{
    public List<string> LockUrls { get; init; } = [];
}

public sealed class UnlockPostContentActivity : IResumableWorkflowActivity
{
    public string Kind => PostParserWorkflow.UnlockContent;
    public string DisplayName => "Assess and unlock post content";
    public string Description => "Assess expiry reports before buying eligible items. Save partial information and wait when purchases need a person.";
    public string DescriptionKey => "workflow.activity.postParserUnlockContent.description";
    public string Group => "postParser";
    public WorkflowActivityCategory Category => WorkflowActivityCategory.Transform;
    public IReadOnlyList<string> AcceptedInputItemTypes => [PostParserWorkflow.ContentType];
    public WorkflowItemTypeBehavior OutputBehavior => WorkflowItemTypeBehavior.Passthrough;
    public sealed record Config { public bool UseConfiguredSoulPlusPurchaseLimit { get; init; } }

    public Task<WorkflowItemOutcome> ProcessItemAsync(WorkflowExecutionContext ctx, object item, CancellationToken ct) =>
        ProcessAsync(ctx, (PostParserContentItem)item, null, false, ct);

    public Task<WorkflowItemOutcome> ResumeAsync(WorkflowExecutionContext ctx, object item, string signalJson, CancellationToken ct)
    {
        var signal = JsonSerializer.Deserialize<PostParserPurchaseSignal>(signalJson, WorkflowJson.Options)
            ?? new PostParserPurchaseSignal();
        return ProcessAsync(ctx, (PostParserContentItem)item, signal, true, ct);
    }

    private static async Task<WorkflowItemOutcome> ProcessAsync(WorkflowExecutionContext ctx,
        PostParserContentItem item, PostParserPurchaseSignal? signal, bool refresh, CancellationToken ct)
    {
        var bridge = ctx.Services.GetRequiredService<IPostParserWorkflowTaskBridge>();
        var runId = checked((int)ctx.RunId);
        await bridge.EnsureCurrentAsync(item.Input, runId, ct);
        var reader = ctx.Services.GetRequiredService<IPostContentService>();
        var quote = item.Content.Locks.Where(l => !l.IsBought && l.Url != null)
            .GroupBy(l => l.Url!).ToDictionary(g => g.Key, g => g.First().Price);
        var content = refresh && item.Input.Link is {Length: > 0} reference
            ? await reader.ReadAsync(reference, item.Input.SourceHint, ct) : item.Content;
        item = item with {Content = content};
        await bridge.SaveSnapshotAsync(item.Input, runId, content, item.Availability, "snapshotSaved", null, ct);
        if (!content.Locks.Any(l => !l.IsBought)) return WorkflowItemOutcome.ReplaceWith(item);

        var options = ctx.Services.GetRequiredService<IBOptions<SoulPlusOptions>>().Value;
        var manual = signal?.LockUrls.Count > 0;
        var mayBuy = !string.IsNullOrWhiteSpace(item.Input.Link) &&
            (item.Input.SourceHint == nameof(PostParserSource.SoulPlus) || content.SourceHint == nameof(PostParserSource.SoulPlus)) &&
            (manual || ctx.GetConfig<Config>()?.UseConfiguredSoulPlusPurchaseLimit == true);
        if (manual && signal!.LockUrls.Any(url => !quote.ContainsKey(url)))
            throw new InvalidOperationException("The purchase selection does not belong to the saved quote. Refresh the post first.");

        if (!manual)
        {
            var aiMissing = await PostParserAi.ConfigurationProblemAsync(ctx, ct);
            if (aiMissing != null)
                return await WaitAsync(ctx, item, "awaitingAi", aiMissing, ct);
            var assessment = await ctx.Services.GetRequiredService<IPostAvailabilityAnalyzer>().AnalyzeAsync(content, ct);
            item = item with {Availability = assessment};
            await bridge.SaveSnapshotAsync(item.Input, runId, content, assessment, "snapshotSaved", assessment.Reason, ct);
        }

        if (mayBuy && (manual || item.Availability?.Status is "noExpiryReported" or "restored"))
        {
            await bridge.EnsureCurrentAsync(item.Input, runId, ct);
            var approved = manual ? signal!.LockUrls.Distinct().ToDictionary(url => url, url => quote[url]) : null;
            var purchase = await ctx.Services.GetRequiredService<SharedContentPurchasePolicy>().PurchaseAsync(
                item.Input.Link!, content.SourceHint ?? item.Input.SourceHint, options.AutoBuyThreshold,
                options.MinimumRemainingCoins, approved, ct);
            item = item with {Content = purchase.Content, Warnings = purchase.Warnings};
            await bridge.SaveSnapshotAsync(item.Input, runId, item.Content, item.Availability, "snapshotSaved", null, ct);
        }

        if (!item.Content.Locks.Any(l => !l.IsBought)) return WorkflowItemOutcome.ReplaceWith(item);

        // Expose whatever is readable, but keep this run waiting until every restricted item is resolved.
        var problem = await PostParserAi.ConfigurationProblemAsync(ctx, ct);
        if (problem == null)
        {
            var partial = await ctx.Services.GetRequiredService<IPostDownloadInfoExtractor>().ExtractAsync(item.Content, ct);
            partial = partial with
            {
                IsComplete = false, Availability = item.Availability,
                Warnings = partial.Warnings.Concat(item.Warnings)
                    .Append("Some content is still locked; download codes or extraction instructions may be missing.").Distinct().ToList()
            };
            await bridge.SaveResultAsync(item.Input, runId, partial, ct);
        }
        var state = item.Availability?.Status == "expired" ? "possiblyExpired" : "awaitingPurchase";
        var message = problem ?? (item.Warnings.Count > 0 ? string.Join("\n", item.Warnings) :
            item.Availability?.Reason ?? "Review the remaining restricted items, then purchase them or retry after unlocking on the source site.");
        return await WaitAsync(ctx, item, state, message, ct);
    }

    internal static async Task<WorkflowItemOutcome> WaitAsync(WorkflowExecutionContext ctx, PostParserContentItem item,
        string state, string message, CancellationToken ct)
    {
        var bridge = ctx.Services.GetRequiredService<IPostParserWorkflowTaskBridge>();
        await bridge.SaveSnapshotAsync(item.Input, checked((int)ctx.RunId), item.Content, item.Availability, state, message, ct);
        var options = ctx.Services.GetRequiredService<IBOptions<SoulPlusOptions>>().Value;
        return WorkflowItemOutcome.Suspend(new WorkflowSuspension($"postParser.{state}",
            JsonSerializer.Serialize(new {item.Content, item.Availability, options.AutoBuyThreshold,
                options.MinimumRemainingCoins, Message = message}, WorkflowJson.Options), item));
    }
}

public sealed class ExtractPostDownloadInfoActivity : IResumableWorkflowActivity
{
    public string Kind => PostParserWorkflow.ExtractDownloadInfo;
    public string DisplayName => "Extract download and extraction instructions";
    public string Description => "Extract links, access codes and ordered extraction instructions. Missing AI configuration waits after saving the content.";
    public string DescriptionKey => "workflow.activity.postParserExtractDownloadInfo.description";
    public string Group => "postParser";
    public WorkflowActivityCategory Category => WorkflowActivityCategory.Transform;
    public IReadOnlyList<string> AcceptedInputItemTypes => [PostParserWorkflow.ContentType];
    public WorkflowItemTypeBehavior OutputBehavior => WorkflowItemTypeBehavior.Fixed;
    public string FixedOutputItemType => PostParserWorkflow.ResultType;

    // AI is checked at execution of this step, so it cannot prevent the earlier snapshot from being saved.
    public Task<WorkflowItemOutcome> ResumeAsync(WorkflowExecutionContext ctx, object item, string signalJson, CancellationToken ct) =>
        ProcessItemAsync(ctx, item, ct);

    public async Task<WorkflowItemOutcome> ProcessItemAsync(WorkflowExecutionContext ctx, object item, CancellationToken ct)
    {
        var content = item as PostParserContentItem ?? throw new InvalidOperationException("This node needs readable post content.");
        var bridge = ctx.Services.GetRequiredService<IPostParserWorkflowTaskBridge>();
        var runId = checked((int)ctx.RunId);
        await bridge.EnsureCurrentAsync(content.Input, runId, ct);
        var problem = await PostParserAi.ConfigurationProblemAsync(ctx, ct);
        if (problem != null) return await UnlockPostContentActivity.WaitAsync(ctx, content, "awaitingAi", problem, ct);
        var result = await ctx.Services.GetRequiredService<IPostDownloadInfoExtractor>().ExtractAsync(content.Content, ct);
        result = result with
        {
            Title = string.IsNullOrWhiteSpace(result.Title) ? content.Content.Title : result.Title,
            IsComplete = !content.Content.Locks.Any(l => !l.IsBought),
            Availability = content.Availability,
            Warnings = result.Warnings.Concat(content.Warnings).Distinct().ToList()
        };
        await bridge.SaveResultAsync(content.Input, runId, result, ct);
        if (!result.IsComplete)
            return await UnlockPostContentActivity.WaitAsync(ctx, content, "awaitingPurchase", "The post still contains locked content. Unlock it and retry.", ct);
        return WorkflowItemOutcome.ReplaceWith(new PostParserResultItem(content.Input, result));
    }
}

public sealed class CheckPostLinksActivity : IWorkflowActivity
{
    public string Kind => PostParserWorkflow.CheckLinks;
    public string DisplayName => "Check sharing links";
    public string Description => "Check supported sharing links conservatively. Unsupported or inconclusive checks remain unknown.";
    public string DescriptionKey => "workflow.activity.postParserCheckLinks.description";
    public string Group => "postParser";
    public WorkflowActivityCategory Category => WorkflowActivityCategory.Transform;
    public IReadOnlyList<string> AcceptedInputItemTypes => [PostParserWorkflow.ResultType];
    public async Task<WorkflowItemOutcome> ProcessItemAsync(WorkflowExecutionContext ctx, object item, CancellationToken ct)
    {
        var parsed = (PostParserResultItem)item;
        var bridge = ctx.Services.GetRequiredService<IPostParserWorkflowTaskBridge>();
        await bridge.EnsureCurrentAsync(parsed.Input, checked((int)ctx.RunId), ct);
        var checker = ctx.Services.GetRequiredService<IPostLinkHealthChecker>();
        var resources = new List<PostDownloadResource>();
        foreach (var resource in parsed.Result.Resources)
        {
            var health = await checker.CheckAsync(resource.Link, resource.Code, ct);
            resources.Add(resource with {LinkHealth = health});
        }
        var result = parsed.Result with {Resources = resources};
        await bridge.SaveResultAsync(parsed.Input, checked((int)ctx.RunId), result, ct);
        return WorkflowItemOutcome.ReplaceWith(parsed with {Result = result});
    }
}

internal static class PostParserAi
{
    public static async Task<string?> ConfigurationProblemAsync(WorkflowExecutionContext ctx, CancellationToken ct)
    {
        var features = ctx.Services.GetService<IAiFeatureService>();
        var providers = ctx.Services.GetService<IAiProviderService>();
        var config = features == null ? null : await features.GetConfigAsync(AiFeature.PostParser, ct);
        if (features != null && (config == null || config.UseDefault)) config = await features.GetConfigAsync(AiFeature.Default, ct);
        if (config?.ProviderConfigId == null || string.IsNullOrWhiteSpace(config.ModelId) || providers == null)
            return "Configure an AI provider and model for post parsing, then retry. The captured content has been saved.";
        var provider = await providers.GetAsync(config.ProviderConfigId.Value, ct);
        return provider == null || !provider.IsEnabled || !provider.LlmEnabled
            ? "The configured post-parsing AI provider is missing or disabled. Enable it and retry." : null;
    }
}
