using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Cover;
using Bakabase.InsideWorld.Models.Constants;

namespace Bakabase.Tests.Implementations;

public class TestCoverDiscoverer : ICoverDiscoverer
{
    public Task<CoverDiscoveryResult?> Discover(string path, CoverSelectOrder order, bool useIconAsFallback, CancellationToken ct)
    {
        return Task.FromResult<CoverDiscoveryResult?>(null);
    }
}
