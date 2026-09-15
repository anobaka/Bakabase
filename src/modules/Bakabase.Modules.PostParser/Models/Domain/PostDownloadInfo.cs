namespace Bakabase.Modules.PostParser.Models.Domain;

/// <summary>Information extracted from a post. An empty list means no download links were found.</summary>
public record PostDownloadInfo
{
    public string? Title { get; init; }
    public List<PostDownloadResource> Resources { get; init; } = [];
}

/// <summary>A download location and its credentials, without any download or library policy.</summary>
public record PostDownloadResource
{
    public string Link { get; init; } = "";
    public string? Code { get; init; }
    public string? Password { get; init; }
}
