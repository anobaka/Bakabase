using System.Collections.Generic;

namespace Bakabase.Modules.ThirdParty.ThirdParties.Javbus.Models;

/// <summary>What one Javbus detail page yields for a batch magnet lookup.</summary>
public record JavbusMagnetSearchResult
{
    public string Number { get; init; } = string.Empty;

    public string DetailUrl { get; init; } = string.Empty;

    public string? Title { get; init; }

    public string? CoverUrl { get; init; }

    /// <summary>Every magnet the site lists, unfiltered — picking is <see cref="JavbusMagnetSelector"/>'s job.</summary>
    public List<JavbusMagnet> Magnets { get; init; } = [];
}
