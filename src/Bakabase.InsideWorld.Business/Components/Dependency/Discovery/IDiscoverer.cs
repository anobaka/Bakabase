using System.Threading;
using System.Threading.Tasks;

namespace Bakabase.InsideWorld.Business.Components.Dependency.Discovery
{
    public interface IDiscoverer
    {
        Task<(string Location, string? Version)?> Discover(string defaultDirectory, CancellationToken ct);
    }
}