using System.Collections.Generic;
using System.Globalization;

namespace Bakabase.InsideWorld.Business.Components.PostParser.Models.Domain;

public sealed record PostParserTaskPurchaseSelection
{
    public int TaskId { get; set; }
    public int Revision { get; set; }
    public int WorkflowRunId { get; set; }
    public List<string> LockUrls { get; set; } = [];
    public decimal MaxTotalCost { get; set; }
    public string MaxTotalCostText => MaxTotalCost.ToString(CultureInfo.InvariantCulture);
    public string? CurrencyCode { get; set; }
    public string? CurrencyName { get; set; }
    public string QuoteHash { get; set; } = "";
}

public sealed record PostParserTaskPurchasePreview
{
    public PostParserTaskFilterScope Scope { get; set; } = new();
    public int MatchedCount { get; set; }
    public int ActionableCount { get; set; }
    public decimal KnownTotalCost { get; set; }
    public string KnownTotalCostText => KnownTotalCost.ToString(CultureInfo.InvariantCulture);
    public string? CurrencyCode { get; set; }
    public string? CurrencyName { get; set; }
    public List<PostParserCurrencyTotal> CurrencyTotals { get; set; } = [];
    public decimal AutoBuyThreshold { get; set; }
    public decimal MinimumRemainingCoins { get; set; }
    public Dictionary<string, int> SkipReasonCounts { get; set; } = [];
    public List<PostParserTaskPurchaseSelection> Tasks { get; set; } = [];
    public List<PostParserTaskPurchaseSkip> Skipped { get; set; } = [];
}

public sealed record PostParserTaskPurchaseSkip(int TaskId, string Reason);

public sealed record ConfirmPostParserTaskPurchasesInput
{
    public PostParserTaskFilterScope Scope { get; set; } = null!;
    public List<PostParserTaskPurchaseSelection> Tasks { get; set; } = [];
}

public sealed record PostParserTaskPurchaseResult
{
    public int RequestedCount { get; set; }
    public int QueuedCount { get; set; }
    public int SkippedCount { get; set; }
    public int FailedCount { get; set; }
    public List<PostParserTaskPurchaseOutcome> Items { get; set; } = [];
}

public sealed record PostParserTaskPurchaseOutcome(int TaskId, string Status, int? WorkflowRunId = null, string? Reason = null);

public sealed record PostParserCurrencyTotal(string? CurrencyCode, string? CurrencyName, decimal Total)
{
    public string TotalText => Total.ToString(CultureInfo.InvariantCulture);
}
