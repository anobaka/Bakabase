namespace Bakabase.Modules.ThirdParty.ThirdParties.ExHentai.Models;

public record ExHentaiTorrent
{
    public string DownloadUrl { get; set; } = null!;
    public int Downloaded { get; set; }
    public DateTime UpdatedAt { get; set; }
    public long Size { get; set; }
}