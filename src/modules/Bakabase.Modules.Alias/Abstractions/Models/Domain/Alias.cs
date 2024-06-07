namespace Bakabase.Modules.Alias.Abstractions.Models.Domain;

public record Alias
{
    public string Text { get; set; } = null!;
    public string? Preferred { get; set; }

    /// <summary>
    /// Does not include current text.
    /// </summary>
    public HashSet<string>? Candidates { get; set; }
}