namespace Bakabase.Modules.ThirdParty.ThirdParties.SoulPlus.Models;

public record SoulPlusPost
{
    // public int Id { get; set; }
    public string Title { get; set; } = null!;
    public string Html { get; set; } = null!;
    public List<SoulPlusPostLockedContent>? LockedContents { get; set; }
    public string? SourceUrl { get; set; }
    public decimal? Balance { get; set; }
    public List<SoulPlusPostComment> Comments { get; set; } = [];
}

public record SoulPlusPostComment
{
    public string? Id { get; set; }
    public string? Floor { get; set; }
    public string? Author { get; set; }
    public DateTimeOffset? PostedAt { get; set; }
    public string Html { get; set; } = "";
}
