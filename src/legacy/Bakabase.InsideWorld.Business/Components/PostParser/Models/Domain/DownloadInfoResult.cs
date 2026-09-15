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

    /// <summary>
    /// Which sharing service the link points at. Not something the model is asked for — it is
    /// worked out from the host, because a model's guess about a domain is worth less than a
    /// lookup table and costs a round trip.
    /// </summary>
    public Bakabase.Modules.Acquisition.Abstractions.Models.Domain.Constants.AcquisitionDriveKind DriveKind
    {
        get;
        set;
    }
}
