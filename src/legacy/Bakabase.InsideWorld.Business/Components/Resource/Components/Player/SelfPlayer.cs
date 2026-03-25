using System.Diagnostics;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Configuration;

namespace Bakabase.InsideWorld.Business.Components.Resource.Components.Player
{
    public class SelfPlayer : ISystemPlayer
    {
        public Task<string> Validate()
        {
            return Task.FromResult((string) null);
        }

        public async Task Play(string file)
        {
            // todo: windows only.
            var windowsSafeFile = file.Replace(InternalOptions.DirSeparator, InternalOptions.WindowsSpecificDirSeparator);

            var p = new Process
            {
                StartInfo = new ProcessStartInfo(windowsSafeFile)
                {
                    UseShellExecute = true
                }
            };
            p.Start();
        }
    }
}