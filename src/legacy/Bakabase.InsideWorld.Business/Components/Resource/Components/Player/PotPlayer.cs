using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.Resource.Components.Player.Infrastructures;
using Bakabase.InsideWorld.Business.Services;
using Bakabase.InsideWorld.Models.Configs.CustomOptions;
using Bootstrap.Extensions;
using Microsoft.Win32;
using Serilog;

namespace Bakabase.InsideWorld.Business.Components.Resource.Components.Player
{
    [Player]
    public class PotPlayer : CustomPlayer
    {
        private static CustomPlayerOptions BuildCustomPlayerOptions()
        {
            try
            {
                const string key = @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\PotPlayerMini64.exe";
                var localMachine = Registry.LocalMachine;
                var fileKey = localMachine.OpenSubKey(key);
                object result;
                if (fileKey != null)
                {
                    result = fileKey.GetValue(string.Empty);
                    fileKey.Close();
                }
                else
                {
                    throw new Exception($"Could not find register key: {key}");
                }

                if (result == null)
                {
                    throw new Exception($@"Can not get valid value of register key: {key}");
                }

                return new CustomPlayerOptions
                {
                    Executable = result.ToString()!
                };
            }
            catch (Exception e)
            {
                Log.Logger.Error(
                    $"An error occurred during discovering PotPlayerMini64.exe. {e.BuildFullInformationText()}");
                throw new Exception(@"Could not find PotPlayerMini64.exe in registry [SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\PotPlayerMini64.exe]");
            }
        }

        public PotPlayer() : base(BuildCustomPlayerOptions())
        {
            
        }
    }
}