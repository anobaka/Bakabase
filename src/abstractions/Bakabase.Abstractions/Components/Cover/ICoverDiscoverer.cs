using Bakabase.InsideWorld.Models.Constants;

namespace Bakabase.Abstractions.Components.Cover;

public interface ICoverDiscoverer
{
    Task<CoverDiscoveryResult?> Discover(string path, CancellationToken ct, CoverSelectOrder order);
}