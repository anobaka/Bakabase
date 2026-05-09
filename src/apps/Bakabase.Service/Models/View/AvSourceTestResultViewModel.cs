namespace Bakabase.Service.Models.View;

public class AvSourceTestDetailViewModel
{
    public string? Number { get; set; }
    public string? Title { get; set; }
    public string? OriginalTitle { get; set; }
    public string? Actor { get; set; }
    public string? Tag { get; set; }
    public string? Release { get; set; }
    public string? Year { get; set; }
    public string? Studio { get; set; }
    public string? Publisher { get; set; }
    public string? Series { get; set; }
    public string? Runtime { get; set; }
    public string? Director { get; set; }
    public string? Source { get; set; }
    public string? CoverUrl { get; set; }
    public string? PosterUrl { get; set; }
    public string? Website { get; set; }
    public string? Mosaic { get; set; }
    public string? SearchUrl { get; set; }
}

public class AvSourceTestResultViewModel
{
    public string Source { get; set; } = null!;
    public AvSourceTestDetailViewModel? Detail { get; set; }
    public string? Error { get; set; }
    public bool Skipped { get; set; }
    public long DurationMs { get; set; }
}

public class AvSourceInfoViewModel
{
    public string Id { get; set; } = null!;
    public string? DefaultBaseUrl { get; set; }
    public string? DefaultCookie { get; set; }
    public string? ResolvedBaseUrl { get; set; }
    public string? ResolvedCookie { get; set; }
    public bool Enabled { get; set; }
}
