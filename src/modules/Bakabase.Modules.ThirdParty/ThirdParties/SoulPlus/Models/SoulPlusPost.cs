namespace Bakabase.Modules.ThirdParty.ThirdParties.SoulPlus.Models;

public record SoulPlusPost
{
    // public int Id { get; set; }
    public string Title { get; set; } = null!;
    public string Html { get; set; } = null!;
    public List<SoulPlusPostLockedContent>? LockedContents { get; set; }
}