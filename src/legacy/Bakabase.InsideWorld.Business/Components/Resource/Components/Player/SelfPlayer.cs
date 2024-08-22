using System.Diagnostics;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Configuration;
using Bakabase.InsideWorld.Business.Components.Resource.Components.Player.Infrastructures;
using Bakabase.InsideWorld.Models.Constants;

namespace Bakabase.InsideWorld.Business.Components.Resource.Components.Player
{
    [Player(Description = "Use playable file as executable")]
    public class SelfPlayer : IPlayer
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