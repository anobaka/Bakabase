using System.Threading;
using System.Threading.Tasks;

namespace Bakabase.InsideWorld.Business.Components.ThirdParty.Installer
{
    /// <summary>
    /// <para>Not available: 1. New version limitation</para>
    /// </summary>
    public interface IComponentInstaller
    {
        string Id { get; }
        string DisplayName { get; }
        Task<LocalInstallation?> CheckInstallation();
        Task Install(CancellationToken ct);
        Task<SimpleVersion> GetLatestVersion();
    }
}