namespace Bakabase.Abstractions.Models.Domain;

/// <summary>Independent option blocks; each retains the existing profile priority semantics.</summary>
public enum ResourceProfileRuleKind
{
    NameTemplate = 1,
    Enhancers = 2,
    PlayableFiles = 3,
    Players = 4,
    Properties = 5,
    OpeningMode = 6
}
