using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.Configurations.Models.Domain;
using Bakabase.InsideWorld.Business.Components.PostParser.Fetchers;
using Bakabase.InsideWorld.Business.Components.PostParser.Models.Domain;
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
    public bool EnforceConfiguredLimits { get; init; }
    public decimal MaxTotalCost { get; init; }
    public string? ExpectedAccountKeyHash { get; init; }
    public string? ContentReadinessDecisionId { get; init; }
    public string? ContentReviewId { get; init; }
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
        using var executionScope = PostParserExecutionScope.Begin((stage, _) => ctx.ReportStage(stage));
        var reader = ctx.Services.GetRequiredService<IPostContentService>();
        var quote = PostParserPurchaseQuote.Offers(item.Content).Where(l => l.Url != null)
            .ToDictionary(l => l.Url!, l => l.Price);
        var content = item.Content;
        // A checkpoint can predate a successful purchase whose final read/save was interrupted.
        // Refresh locked input even without a resume signal, before asking AI or purchasing again.
        if ((refresh || content.Locks.Any(l => !l.IsBought)) && item.Input.Link is {Length: > 0} reference)
        {
            await ctx.ReportStage("fetching");
            content = PostContentReadinessPolicy.Reconcile(await reader.ReadAsync(reference, item.Input.SourceHint, ct), content,
                ctx.Services.GetServices<IPostContentEvidenceCanonicalizer>());
        }
        try { content = await bridge.ResolveReadinessAsync(item.Input, runId, content, ct, signal?.ContentReadinessDecisionId); }
        catch (InvalidOperationException error) when (signal?.ContentReadinessDecisionId != null)
        {
            return await WaitAsync(ctx, item with {Content = content with {Readiness = null}}, "awaitingPurchase", error.Message, ct);
        }
        item = item with {Content = content, Availability = PostAvailabilityEvidencePolicy.Apply(item.Availability)};
        await bridge.SaveSnapshotAsync(item.Input, runId, content, item.Availability, "snapshotSaved", null, ct);
        var review = await bridge.ExecuteContentReviewAsync(item.Input, runId, content, signal?.ContentReviewId, ct);
        if (review != null)
        {
            item = item with {Content = review.Content, Warnings = item.Warnings.Concat(review.Warnings).Distinct().ToList()};
            await bridge.SaveSnapshotAsync(item.Input, runId, item.Content, item.Availability, "snapshotSaved", null, ct);
            if (review.CanContinue && !PostContentReadinessPolicy.HasBlockingLocks(item.Content)) return WorkflowItemOutcome.ReplaceWith(item);
            return await WaitAsync(ctx, item, "awaitingPurchase", review.Warnings.FirstOrDefault() ??
                "Some restricted content remains unresolved. Review the deferred or required items before continuing.", ct);
        }
        if (!PostContentReadinessPolicy.HasBlockingLocks(content)) return WorkflowItemOutcome.ReplaceWith(item);

        var options = ctx.Services.GetRequiredService<IBOptions<SoulPlusOptions>>().Value;
        var manual = signal?.LockUrls.Count > 0;
        var enforceLimits = signal?.EnforceConfiguredLimits == true || item.Input.TaskId != null;
        var mayBuy = !string.IsNullOrWhiteSpace(item.Input.Link) &&
            PostParserPurchaseRules.SupportsSource(content.SourceHint ?? item.Input.SourceHint,
                ctx.Services.GetServices<ISharedContentPurchaser>()) &&
            (manual || ctx.GetConfig<Config>()?.UseConfiguredSoulPlusPurchaseLimit == true);
        if (manual && signal!.LockUrls.Any(url => !quote.ContainsKey(url)))
            throw new InvalidOperationException("The purchase selection does not belong to the saved quote. Refresh the post first.");

        var attemptedFreeUrls = new HashSet<string>(StringComparer.Ordinal);
        if (!manual)
        {
            // A verified zero-price offer spends no coins. Unlock it before risk assessment,
            // selecting only these exact endpoints and rechecking an approved price of zero.
            // Keep the original zero-price ceiling across the refresh as well: a free offer
            // becoming paid must not fall through into automatic paid purchasing in this attempt.
            var neededUrls = PostParserPurchaseQuote.Offers(content).Select(l => l.Url).ToHashSet(StringComparer.Ordinal);
            var free = quote.Where(l => l.Value == 0 && neededUrls.Contains(l.Key)).ToDictionary(l => l.Key, l => l.Value);
            if (mayBuy && free.Count > 0)
            {
                attemptedFreeUrls.UnionWith(free.Keys);
                await bridge.EnsureCurrentAsync(item.Input, runId, ct);
                await ctx.ReportStage("purchasing");
                var unlocked = await ctx.Services.GetRequiredService<SharedContentPurchasePolicy>().PurchaseAsync(
                    item.Input.Link!, content.SourceHint ?? item.Input.SourceHint, options.AutoBuyThreshold,
                    options.MinimumRemainingCoins, free, ct, enforceThreshold: true, maxTotalCost: 0);
                content = await bridge.ResolveReadinessAsync(item.Input, runId,
                    PostContentReadinessPolicy.Reconcile(unlocked.Content, content, ctx.Services.GetServices<IPostContentEvidenceCanonicalizer>()), ct);
                item = item with {Content = content, Warnings = unlocked.Warnings};
                await bridge.SaveSnapshotAsync(item.Input, runId, content, item.Availability, "snapshotSaved", null, ct);
                if (!PostContentReadinessPolicy.HasBlockingLocks(content)) return WorkflowItemOutcome.ReplaceWith(item);
            }
            var aiMissing = await PostParserAi.ConfigurationProblemAsync(ctx, ct);
            if (aiMissing != null)
                return await WaitAsync(ctx, item, "awaitingAi", aiMissing, ct);
            var assessment = await ctx.Services.GetRequiredService<IPostAvailabilityAnalyzer>().AnalyzeAsync(content, ct);
            content = await bridge.ResolveReadinessAsync(item.Input, runId,
                PostContentReadinessPolicy.Apply(content, assessment.ContentReadiness), ct);
            item = item with {Availability = assessment, Content = content};
            await bridge.SaveSnapshotAsync(item.Input, runId, content, assessment, "snapshotSaved", assessment.Reason, ct);
        }

        if (mayBuy && (manual || item.Availability?.Status is "noExpiryReported" or "restored"))
        {
            await bridge.EnsureCurrentAsync(item.Input, runId, ct);
            var stillNeeded = PostParserPurchaseQuote.Offers(item.Content).Select(l => l.Url).ToHashSet(StringComparer.Ordinal);
            var approved = manual ? signal!.LockUrls.Where(stillNeeded.Contains).Distinct().ToDictionary(url => url, url => quote[url]) :
                quote.Where(l => stillNeeded.Contains(l.Key) && !attemptedFreeUrls.Contains(l.Key)).ToDictionary(l => l.Key, l => l.Value);
            // An interrupted attempt may no longer have a manual signal. Its checkpoint still
            // bounds automatic URLs/prices; fresh reads must not expand them or retry a free offer.
            if (approved.Count > 0)
            {
                await ctx.ReportStage("purchasing");
                var purchase = await ctx.Services.GetRequiredService<SharedContentPurchasePolicy>().PurchaseAsync(
                    item.Input.Link!, content.SourceHint ?? item.Input.SourceHint, options.AutoBuyThreshold,
                    options.MinimumRemainingCoins, approved, ct, enforceThreshold: enforceLimits || !manual,
                    maxTotalCost: manual && enforceLimits ? signal!.MaxTotalCost : null,
                    expectedAccountKeyHash: signal?.ExpectedAccountKeyHash);
                content = await bridge.ResolveReadinessAsync(item.Input, runId,
                    PostContentReadinessPolicy.Reconcile(purchase.Content, item.Content, ctx.Services.GetServices<IPostContentEvidenceCanonicalizer>()), ct);
                item = item with {Content = content, Warnings = item.Warnings.Concat(purchase.Warnings).Distinct().ToList()};
                await bridge.SaveSnapshotAsync(item.Input, runId, item.Content, item.Availability, "snapshotSaved", null, ct);
                if (purchase.PurchasedUrls.Count > 0 && PostContentReadinessPolicy.HasBlockingLocks(content) &&
                    !PostContentReadinessPolicy.IsCurrent(content, content.Readiness) &&
                    await PostParserAi.ConfigurationProblemAsync(ctx, ct) == null)
                {
                    // Buying changes the readable evidence. Reassess once, without authorizing
                    // another purchase round or re-signing the old evidence as current.
                    var assessment = await ctx.Services.GetRequiredService<IPostAvailabilityAnalyzer>().AnalyzeAsync(content, ct);
                    content = await bridge.ResolveReadinessAsync(item.Input, runId,
                        PostContentReadinessPolicy.Apply(content, assessment.ContentReadiness), ct);
                    item = item with {Content = content, Availability = assessment};
                    await bridge.SaveSnapshotAsync(item.Input, runId, content, assessment, "snapshotSaved", assessment.Reason, ct);
                }
            }
        }

        if (!PostContentReadinessPolicy.HasBlockingLocks(item.Content)) return WorkflowItemOutcome.ReplaceWith(item);

        // Expose readable information while acquisition-relevant or unresolved restricted blocks still remain.
        var problem = await PostParserAi.ConfigurationProblemAsync(ctx, ct);
        if (problem == null)
        {
            var partial = await ctx.Services.GetRequiredService<IPostDownloadInfoExtractor>().ExtractAsync(item.Content, ct);
            partial = partial with
            {
                IsComplete = false, Availability = item.Availability, ContentReadiness = item.Content.Readiness,
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
            JsonSerializer.Serialize(new {item.Input.TaskId, item.Input.Revision, WorkflowRunId = ctx.RunId, item.Content, item.Availability, options.AutoBuyThreshold,
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
        content = content with {Content = await bridge.ResolveReadinessAsync(content.Input, runId, content.Content, ct)};
        using var executionScope = PostParserExecutionScope.Begin((stage, _) => ctx.ReportStage(stage));
        var problem = await PostParserAi.ConfigurationProblemAsync(ctx, ct);
        if (problem != null) return await UnlockPostContentActivity.WaitAsync(ctx, content, "awaitingAi", problem, ct);
        var result = await ctx.Services.GetRequiredService<IPostDownloadInfoExtractor>().ExtractAsync(content.Content, ct);
        result = result with
        {
            Title = string.IsNullOrWhiteSpace(result.Title) ? content.Content.Title : result.Title,
            IsComplete = !PostContentReadinessPolicy.HasBlockingLocks(content.Content),
            ContentReadiness = content.Content.Readiness,
            Availability = content.Availability,
            Warnings = result.Warnings.Concat(content.Warnings).Distinct().ToList()
        };
        await bridge.SaveResultAsync(content.Input, runId, result, ct, PostContentReadinessPolicy.Fingerprint(content.Content));
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
        await ctx.ReportStage("checkingLinks");
        // One large post must not hold the parsing lane indefinitely. Observations retain
        // original order/indices; exceeding the metadata budget is never evidence of expiry.
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        budget.CancelAfter(TimeSpan.FromSeconds(45));
        foreach (var chunk in parsed.Result.Resources.Chunk(4))
        {
            var checkedResources = await Task.WhenAll(chunk.Select(async resource =>
            {
                PostLinkHealth health;
                try {health = await checker.CheckAsync(resource.Link, resource.Code, budget.Token);}
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {health = new() {Status = "unknown", Reason = "SOURCE_CHECK_BUDGET_EXHAUSTED", ReasonCode = "SOURCE_CHECK_BUDGET_EXHAUSTED"};}
                return resource with {LinkHealth = health};
            }));
            resources.AddRange(checkedResources);
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
