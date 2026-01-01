namespace Bakabase.Abstractions.Models.Domain;

public interface IAvDetail
{
    string? Number { get; set; }
    string? Title { get; set; }
    string? OriginalTitle { get; set; }
    string? Actor { get; set; }
    string? Tag { get; set; }
    string? Release { get; set; }
    string? Year { get; set; }
    string? Studio { get; set; }
    string? Publisher { get; set; }
    string? Series { get; set; }
    string? Runtime { get; set; }
    string? Director { get; set; }
    string? Source { get; set; }
    string? CoverUrl { get; set; }
    string? PosterUrl { get; set; }
    string? Website { get; set; }
    string? Mosaic { get; set; }
    /// <summary>
    /// The URL used to search for this resource.
    /// </summary>
    string? SearchUrl { get; set; }
}
