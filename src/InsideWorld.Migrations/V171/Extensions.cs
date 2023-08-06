using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.Resource.Components.Player;
using Bakabase.InsideWorld.Models.Configs.CustomOptions;
using Bakabase.InsideWorld.Models.Models.Dtos;

namespace InsideWorld.Migrations.V171
{
    internal static class Extensions
    {
        public static CustomPlayerOptions ToNewVersion(this CustomPlayerOptionsDto obsoleteOptions)
        {
            return new CustomPlayerOptions
            {
                CommandTemplate = obsoleteOptions.CommandTemplate,
                Executable = obsoleteOptions.Executable,
                SubPlayers = obsoleteOptions.SubCustomPlayerOptionsList?.Select(a =>
                    new CustomPlayerOptions.ExtensionBasedPlayerOptions
                    {
                        CommandTemplate = a.CommandTemplate,
                        Executable = a.Executable,
                        Extensions = a.Extensions
                    }).ToList() ?? new List<CustomPlayerOptions.ExtensionBasedPlayerOptions>()
            };
        }

        public static ExtensionBasedPlayableFileSelectorOptions ToNewVersion(
            this CustomPlayableFileSelectorOptionsDto obsoleteOptions)
        {
            return new ExtensionBasedPlayableFileSelectorOptions
            {
                Extensions = obsoleteOptions.Extensions,
                MaxFileCount = obsoleteOptions.MaxFileCount
            };
        }
    }
}