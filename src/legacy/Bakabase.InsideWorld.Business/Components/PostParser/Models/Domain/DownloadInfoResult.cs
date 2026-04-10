using System.Collections.Generic;

namespace Bakabase.InsideWorld.Business.Components.PostParser.Models.Domain;

/// <summary>
/// Strongly-typed model for the DownloadInfo LLM response.
/// Title is extracted as OptimizedTitle at the handler level; only Resources are stored in result data.
/// </summary>
public class DownloadInfoLlmResponse
{
    public string? Title { get; set; }
    public List<DownloadInfoResource>? Resources { get; set; }
}

public class DownloadInfoResource
{
    public string? Link { get; set; }
    public string? Code { get; set; }
    public string? Password { get; set; }
}
