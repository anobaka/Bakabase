using Bakabase.InsideWorld.Models.Constants;

namespace Bakabase.Abstractions.Components.Cover;

public interface ICoverDiscoverer
{
    Task<CoverDiscoveryResult?> Discover(string path, CoverSelectOrder order, bool useIconAsFallback, CancellationToken ct);
}