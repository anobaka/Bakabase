using System;
using System.Collections.Generic;
using System.Globalization;
using Bakabase.Modules.PostParser.Models.Domain;

namespace Bakabase.InsideWorld.Business.Components.PostParser.Models.Domain;

public sealed record PostParserContentReviewScope
{
    public string? Keyword { get; init; }
    public string? Status { get; init; }
    public string? ReasonCode { get; init; }
    public List<int>? TaskIds { get; init; }
}

public sealed record PostParserContentReviewPreview
{
    public PostParserContentReviewScope Scope { get; init; } = new();
    public int MatchedCount { get; init; }
    public decimal AutoBuyThreshold { get; init; }
    public decimal MinimumRemainingCoins { get; init; }
    public List<PostParserContentReviewTask> Tasks { get; init; } = [];
}

public sealed record PostParserContentReviewTask
{
    public int TaskId { get; init; }
    public Bakabase.InsideWorld.Business.Components.PostParser.Models.Domain.Constants.PostParserSource Source { get; init; }
    public string? Title { get; init; }
    public string? Link { get; init; }
    public int Revision { get; init; }
    public int? WorkflowRunId { get; init; }
    public int DecisionVersion { get; init; }
    public string? ContentFingerprint { get; init; }
    public string QuoteHash { get; init; } = "";
    public bool CanSubmit { get; init; }
    public string? BlockReason { get; init; }
    public bool AlreadyUnlocked { get; init; }
    public List<PostParserContentReviewTarget> Targets { get; init; } = [];
}

public sealed record PostParserContentReviewTarget
{
    public string TargetId { get; init; } = "";
    public string? Floor { get; init; }
    public List<string> BlockIds { get; init; } = [];
    public List<PostContentLock> Blocks { get; init; } = [];
    public decimal? Price { get; init; }
    public string? PriceText => Price?.ToString(CultureInfo.InvariantCulture);
    public string? CurrencyCode { get; init; }
    public string? CurrencyName { get; init; }
    public bool CanBuy { get; init; }
    public bool CanMarkNotRequired { get; init; }
    public string? BuyBlockReason { get; init; }
    public string DefaultAction { get; init; } = "defer";
}

public sealed record ConfirmPostParserContentReviewInput
{
    public PostParserContentReviewScope Scope { get; init; } = new();
    public string RequestId { get; init; } = "";
    public List<PostParserContentReviewSelection> Tasks { get; init; } = [];
}

public sealed record PostParserContentReviewSelection
{
    public int TaskId { get; init; }
    public int Revision { get; init; }
    public int WorkflowRunId { get; init; }
    public int DecisionVersion { get; init; }
    public string ContentFingerprint { get; init; } = "";
    public string QuoteHash { get; init; } = "";
    public List<PostParserContentReviewAction> Targets { get; init; } = [];
}

public sealed record PostParserContentReviewAction(string TargetId, string Action);

public sealed record PostParserContentReviewResult
{
    public string RequestId { get; init; } = "";
    public int RequestedCount { get; init; }
    public int QueuedCount { get; set; }
    public int UnchangedCount { get; set; }
    public int SkippedCount { get; set; }
    public int FailedCount { get; set; }
    public List<PostParserContentReviewOutcome> Items { get; init; } = [];
}

public sealed record PostParserContentReviewOutcome(int TaskId, string Status, int? WorkflowRunId = null,
    string? ReviewId = null, string? Reason = null);

// Internal durable authorization. A pending attempt is not proof of a purchase, nor permission to retry it.
public sealed record PostParserContentReviewReceipt
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string RequestId { get; init; } = "";
    public string RequestHash { get; init; } = "";
    public int SourceRevision { get; init; }
    public int WorkflowRunId { get; init; }
    public int DecisionVersion { get; set; }
    public string ContentFingerprint { get; init; } = "";
    public string AccountKeyHash { get; init; } = "";
    public decimal AutoBuyThreshold { get; init; }
    public decimal MinimumRemainingCoins { get; init; }
    public decimal MaxTotalCost { get; init; }
    public string ConfirmationOutcome { get; set; } = "prepared";
    public string? Problem { get; set; }
    public Bakabase.Modules.PostParser.Models.Domain.PostContent Evidence { get; set; } = new();
    public List<PostParserContentReviewApprovedTarget> Targets { get; init; } = [];
    public List<PostParserContentPurchaseAttempt> Attempts { get; init; } = [];
    public List<PostParserContentReviewTransition> Transitions { get; init; } = [];
}

public sealed record PostParserContentReviewApprovedTarget
{
    public string TargetId { get; init; } = "";
    public string Action { get; init; } = "defer";
    public string? Url { get; init; }
    public decimal? Price { get; init; }
    public List<string> BlockIds { get; init; } = [];
    public List<string> ReaderTargetIds { get; init; } = [];
}

public sealed record PostParserContentPurchaseAttempt
{
    public string TargetId { get; init; } = "";
    public string? Url { get; init; }
    public decimal Price { get; init; }
    public string Status { get; set; } = "pending";
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public string BeforeFingerprint { get; init; } = "";
    // Minimal reader proof, retained independently of the review's advancing content snapshot.
    public Bakabase.Modules.PostParser.Models.Domain.PostContent? BeforeEvidence { get; init; }
}

public sealed record PostParserContentReviewTransition(string BeforeFingerprint, string AfterFingerprint,
    List<string> BlockIds, DateTime CreatedAt);
