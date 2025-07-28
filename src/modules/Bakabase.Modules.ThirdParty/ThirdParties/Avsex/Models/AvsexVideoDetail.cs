namespace Bakabase.Modules.ThirdParty.ThirdParties.Avsex.Models;

public record AvsexVideoDetail : Bakabase.Abstractions.Models.Domain.IAvDetail
{
    public string? Number { get; set; }
    public string? Title { get; set; }
    public string? OriginalTitle { get; set; }
    public string? Actor { get; set; }
    public string? Outline { get; set; }
    public string? Tag { get; set; }
    public string? Release { get; set; }
    public string? Year { get; set; }
    public string? Studio { get; set; }
    public string? Director { get; set; }
    public string? Publisher { get; set; }
    public string? Series { get; set; }
    public string? Source { get; set; }
    public string? CoverUrl { get; set; }
    public string? PosterUrl { get; set; }
    public string? Website { get; set; }
    public string? Mosaic { get; set; }
    public string? Runtime { get; set; }
    public string[]? ExtraFanart { get; set; }
}