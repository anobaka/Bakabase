using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Infrastructures.Components.App;
using Bakabase.Infrastructures.Components.App.Models.Constants;
using Bakabase.InsideWorld.Business.Components.Dependency.Discovery;

namespace Bakabase.InsideWorld.Business.Components.Dependency.Implementations.BakabaseUpdater
{
    public class BakabaseUpdaterDiscover : IDiscoverer
    {
        private const string KeyName = "Bakabase.Updater";

        public async Task<(string Location, string? Version)?> Discover(string defaultDirectory, CancellationToken ct)
        {
            if (Directory.Exists(defaultDirectory))
            {
                var fileNames = Directory.GetFiles(defaultDirectory).Select(Path.GetFileName).ToHashSet();
                var targetName = AppService.OsPlatform == OsPlatform.Windows ? $"{KeyName}.exe" : KeyName;
                if (fileNames.Contains(targetName))
                {
                    var info = FileVersionInfo.GetVersionInfo(Path.Combine(defaultDirectory, targetName));
                    return (defaultDirectory, info.ProductVersion);
                }
            }

            return null;
        }
    }
}