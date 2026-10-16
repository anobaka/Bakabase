namespace Bakabase.Abstractions.Models.Domain;

/// <summary>Only search criteria cross the source boundary; executable settings never do.</summary>
public sealed record ResourceProfileSearchRule
{
    public int Id { get; init; }
    public string? SearchJson { get; init; }
}

public sealed record ResourceProfileEvaluationInput
{
    public int[] ResourceIds { get; init; } = [];
    public ResourceProfileSearchRule[] Profiles { get; init; } = [];
}

public sealed record ResourceProfileResourceMatch
{
    public int ResourceId { get; init; }
    public int[] ProfileIds { get; init; } = [];
}

public sealed record ResourceProfileEvaluationResult
{
    public ResourceProfileResourceMatch[] Matches { get; init; } = [];
}
