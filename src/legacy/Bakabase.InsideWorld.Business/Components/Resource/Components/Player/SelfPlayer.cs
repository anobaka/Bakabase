using System.Diagnostics;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Configuration;
using Bakabase.Abstractions.Helpers;

namespace Bakabase.InsideWorld.Business.Components.Resource.Components.Player
{
    public class SelfPlayer : ISystemPlayer
    {
        public Task<string> Validate()
        {
            return Task.FromResult((string) null);
        }

        internal static ProcessStartInfo BuildStartInfo(string file, bool isWindows)
        {
            var target = isWindows
                ? file.Replace(InternalOptions.DirSeparator, InternalOptions.WindowsSpecificDirSeparator)
                : file;
            return new ProcessStartInfo(target)
            {
                UseShellExecute = true,
                WorkingDirectory = System.IO.Path.GetDirectoryName(file) ?? string.Empty
            };
        }

        public Task Play(string file)
        {
            if (WindowsWebpViewer.TryOpen(file)) return Task.CompletedTask;

            // .NET's shell opener uses the platform's default application. Only
            // Windows paths need backslashes; changing POSIX paths made them invalid.
            using var p = new Process {StartInfo = BuildStartInfo(file, System.OperatingSystem.IsWindows())};
            p.Start();
            return Task.CompletedTask;
        }
    }
}
