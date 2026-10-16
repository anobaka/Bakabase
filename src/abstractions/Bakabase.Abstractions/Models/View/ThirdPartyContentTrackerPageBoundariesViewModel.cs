namespace Bakabase.Abstractions.Models.View;

public record ThirdPartyContentTrackerPageBoundaryViewModel
{
    public string ContentId { get; set; } = null!;
    /// <summary>ISO 8601 UTC content time with an explicit Z, independent of the server's display date format.</summary>
    public string UpdatedAt { get; set; } = null!;
}

/// <summary>Nearest viewed content outside the page, measured using content time rather than viewing time.</summary>
public record ThirdPartyContentTrackerPageBoundariesViewModel
{
    public ThirdPartyContentTrackerPageBoundaryViewModel? Older { get; set; }
    public ThirdPartyContentTrackerPageBoundaryViewModel? Newer { get; set; }
}
