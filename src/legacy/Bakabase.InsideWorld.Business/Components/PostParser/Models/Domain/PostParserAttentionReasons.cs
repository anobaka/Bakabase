using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using Bakabase.InsideWorld.Business.Components.PostParser.Services;
using Bakabase.Modules.PostParser.Models.Domain;
using Bakabase.Modules.PostParser.Services;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;

namespace Bakabase.InsideWorld.Business.Components.PostParser.Models.Domain;

/// <summary>Independent, overlapping reasons; monetary limits are not availability overrides.</summary>
public static class PostParserAttentionReasons
{
    public static readonly IReadOnlySet<string> Known = new HashSet<string>
    {
        "aiConfiguration", "possiblyExpired", "availabilityUnknown", "priceUnknown", "overPurchaseLimit",
        "balanceUnknown", "insufficientBalance", "purchaseRequired", "incompleteInformation", "workflowWaiting"
    };

    public static List<string> For(PostParserTask task)
    {
        var value = task.Results?.GetValueOrDefault(Bakabase.InsideWorld.Business.Components.PostParser.Models.Domain.Constants.PostParseTarget.DownloadInfo) as JsonObject;
        var data = (value?["data"] ?? value?["Data"] ?? value) as JsonObject;
        var incomplete = (data?["isComplete"] ?? data?["IsComplete"]) is JsonValue complete &&
            complete.TryGetValue<bool>(out var isComplete) && !isComplete;
        var partial = incomplete || task.ContentSnapshot != null && Bakabase.Modules.PostParser.Services.PostContentReadinessPolicy.HasBlockingLocks(task.ContentSnapshot);
        var group = PostParserTaskSummary.Classify(task.WorkflowRunId, task.WorkflowStatus, task.ParsingState,
            PostParserTaskSummary.IsFailed(task), partial, (data?["resources"] ?? data?["Resources"]) is JsonArray, false);
        return group == "attention" ? Create(task.ParsingState, incomplete, task.ContentSnapshot,
            task.Availability?.Status, task.Availability?.Evidence, task.AutoBuyThreshold, task.MinimumRemainingCoins) : [];
    }

    public static List<string> Create(string? state, bool incomplete, Bakabase.Modules.PostParser.Models.Domain.PostContent? content,
        string? availability, IEnumerable<string>? evidence, decimal threshold, decimal reserve, IReadOnlyList<PostContentLock>? blocking = null)
    {
        var reasons = new List<string>();
        if (state == "awaitingAi") reasons.Add("aiConfiguration");
        var offers = content == null ? [] : PostParserPurchaseQuote.Offers(content, blocking).ToList();
        if (offers.Count > 0)
        {
            var effective = PostAvailabilityEvidencePolicy.EffectiveStatus(availability, evidence);
            if (effective == "expired") reasons.Add("possiblyExpired");
            else if (effective is null or "unknown") reasons.Add("availabilityUnknown");
            if (offers.Any(o => o.Price is null or < 0)) reasons.Add("priceUnknown");
            if (offers.Any(o => o.Price > threshold) || threshold < 0) reasons.Add("overPurchaseLimit");
            if (offers.Any(o => o.Price > 0 && o.Price <= threshold))
            {
                if (content!.Balance == null) reasons.Add("balanceUnknown");
                else if (offers.Where(o => o.Price > 0 && o.Price <= threshold).Sum(o => o.Price!.Value) > content.Balance - reserve)
                    reasons.Add("insufficientBalance");
            }
            reasons.Add("purchaseRequired");
        }
        if (incomplete || state == "partial") reasons.Add("incompleteInformation");
        if (reasons.Count == 0) reasons.Add("workflowWaiting");
        return reasons;
    }
}
