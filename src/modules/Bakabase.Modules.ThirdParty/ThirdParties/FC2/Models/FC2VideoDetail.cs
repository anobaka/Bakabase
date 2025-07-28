namespace Bakabase.Modules.ThirdParty.ThirdParties.FC2.Models;

public record FC2VideoDetail : Bakabase.Abstractions.Models.Domain.IAvDetail
{
    public string? Number { get; set; }
    public string? Title { get; set; }
    public string? OriginalTitle { get; set; }
    public string? Actor { get; set; }
    public string? Outline { get; set; }
    public string? OriginalPlot { get; set; }
    public string? Tag { get; set; }
    public string? Release { get; set; }
    public string? Year { get; set; }
    public string? Runtime { get; set; }
    public string? Score { get; set; }
    public string? Series { get; set; }
    public string? Director { get; set; }
    public string? Studio { get; set; }
    public string? Publisher { get; set; }
    public string? Source { get; set; }
    public string? Website { get; set; }
    public Dictionary<string, string>? ActorPhoto { get; set; }
    public string? CoverUrl { get; set; }
    public string? PosterUrl { get; set; }
    public List<string>? ExtraFanart { get; set; }
    public string? Trailer { get; set; }
    public bool ImageDownload { get; set; }
    public string? ImageCut { get; set; }
    public string? Mosaic { get; set; }
    public string? Wanted { get; set; }
}