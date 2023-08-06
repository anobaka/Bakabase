using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.Resource.Components.Player.Infrastructures;
using Bakabase.InsideWorld.Models.Attributes;
using Bakabase.InsideWorld.Models.Configs.CustomOptions;
using Bakabase.InsideWorld.Models.Models.Dtos;
using Bootstrap.Extensions;
using CliWrap;
using CliWrap.Buffered;

namespace Bakabase.InsideWorld.Business.Components.Resource.Components.Player
{
    [Player(OptionsType = typeof(CustomPlayerOptions))]
    public class CustomPlayer : IPlayer
    {
        private const string DefaultCommandTemplate = "{0}";

        public Task<string> Validate()
        {
            return Task.FromResult((string) null);
        }

        protected readonly CustomPlayerOptions Options;

        public CustomPlayer(CustomPlayerOptions options)
        {
            Options = options;
        }

        public virtual async Task Play(string file)
        {
            var ext = Path.GetExtension(file);
            var subPlayerOptions =
                Options.SubPlayers.FirstOrDefault(t => t.Extensions.Contains(ext, StringComparer.OrdinalIgnoreCase));
            string cmdTemplate;
            string executable;
            if (subPlayerOptions != null)
            {
                cmdTemplate = subPlayerOptions.CommandTemplate;
                executable = subPlayerOptions.Executable;
            }
            else
            {
                cmdTemplate = Options.CommandTemplate;
                executable = Options.Executable;
            }

            if (executable.IsNullOrEmpty())
            {
                await new SelfPlayer().Play(file);
            }
            else
            {
                var cmd = cmdTemplate.IsNullOrEmpty() ? DefaultCommandTemplate : cmdTemplate;
                var args = cmd.Replace("{0}", file);
                Cli.Wrap(executable).WithArguments(args).ExecuteAsync();
            }
        }
    }
}