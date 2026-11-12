namespace Bakabase.Modules.PostParser.Models.Domain;

/// <summary>Resolved against a particular visible snapshot; never an instruction to purchase anything.</summary>
public sealed record PostContentReadinessAssessment
{
    public string Fingerprint { get; init; } = "";
    public string PolicyVersion { get; init; } = "";
    public string Basis { get; init; } = "analysis";
    public List<PostContentBlockDecision> Decisions { get; init; } = [];
}

public sealed record PostContentBlockDecision
{
    public string BlockId { get; init; } = "";
    public string Relevance { get; init; } = "unknown";
    public string Purpose { get; init; } = "unknown";
    public List<string> Evidence { get; init; } = [];
    public string? Reason { get; init; }
}
