namespace Bakabase.Abstractions.Models.Domain;

/// <summary>Identity of a saved query's data owner; never an endpoint or an authorization grant.</summary>
public record ResourceSearchOwner
{
    public string ServerId { get; set; } = string.Empty;
    public string LibraryEpoch { get; set; } = string.Empty;
}
