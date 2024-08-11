using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Cover;
using Bakabase.InsideWorld.Models.Constants;

namespace Bakabase.InsideWorld.Business.Components;

public class CoverDiscoverer : ICoverDiscoverer
{
    public Task<CoverDiscoveryResult?> DiscoverCover(string path, CancellationToken ct, CoverSelectOrder order)
    {
        throw new System.NotImplementedException();
    }
}