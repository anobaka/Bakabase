using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.ComponentModel;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Bootstrap.Extensions;
using CliWrap;
using Microsoft.Extensions.Logging;

namespace Bakabase.InsideWorld.Business.Components.Dependency.Discovery
{
    public abstract class ExecutableDiscoverer
        : IDiscoverer
    {
        protected ExecutableDiscoverer(ILoggerFactory loggerFactory)
        {
            Logger = loggerFactory.CreateLogger(GetType());
        }

        protected ILogger Logger;
        protected abstract HashSet<string> RequiredRelativeFileNamesWithoutExtensions { get; }
        protected abstract string RelativeFileNameWithoutExtensionForAcquiringVersion { get; }
        protected abstract string? ArgumentsForAcquiringVersion { get; }

        protected abstract string ParseVersion(string output);

        public async Task<(string Location, string? Version)?> Discover(string defaultDirectory, CancellationToken ct)
        {
            var directories = new[] { defaultDirectory }.Concat(
                (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator));
            var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
            foreach (var directory in directories.Where(d => !string.IsNullOrWhiteSpace(d)).Distinct(comparer))
            {
                var location = OperatingSystem.IsWindows() ? directory.Trim('"') : directory;
                ct.ThrowIfCancellationRequested();
                var discovered = await TryDiscoverAt(location, ct);
                if (discovered != null) return discovered;
            }
            return null;
        }

        private async Task<(string Location, string? Version)?> TryDiscoverAt(string location, CancellationToken ct)
        {
            try
            {
                if (!DiscoverByDirectory(location, RequiredRelativeFileNamesWithoutExtensions)) return null;
                var executable = Path.Combine(location, PlatformFileName(RelativeFileNameWithoutExtensionForAcquiringVersion));
                var output = new StringBuilder();
                var error = new StringBuilder();
                var command = Cli.Wrap(executable);
                if (ArgumentsForAcquiringVersion.IsNotEmpty()) command = command.WithArguments(ArgumentsForAcquiringVersion);
                command = command.WithStandardOutputPipe(PipeTarget.ToStringBuilder(output))
                    .WithStandardErrorPipe(PipeTarget.ToStringBuilder(error))
                    .WithValidation(CommandResultValidation.None);
                var result = await command.ExecuteAsync(ct);
                if (result.ExitCode != 0)
                {
                    Logger.LogWarning("Skipping unusable component {Executable}: version command exited {ExitCode}", executable, result.ExitCode);
                    return null;
                }
                try
                {
                    return (location, ParseVersion(output.ToString()));
                }
                catch (Exception exception)
                {
                    Logger.LogWarning(exception, "Skipping component with an unrecognized version at {Executable}", executable);
                    return null;
                }
            }
            catch (Exception exception) when (exception is Win32Exception or IOException or UnauthorizedAccessException)
            {
                // Imported components may target another OS/architecture or lack execute
                // permissions. Keep that copy intact and try the current machine's PATH.
                Logger.LogWarning(exception, "Skipping unavailable component directory {Directory}", location);
                return null;
            }
        }

        private static string PlatformFileName(string name) => OperatingSystem.IsWindows() ? name + ".exe" : name;

        protected static bool DiscoverByDirectory(string directory, HashSet<string> relativePathsWithoutExt)
        {
            if (!Directory.Exists(directory)) return false;
            foreach (var relativePath in relativePathsWithoutExt)
            {
                // Do not strip arbitrary extensions: ffmpeg.exe is not ffmpeg on Unix.
                var file = Path.Combine(directory, PlatformFileName(relativePath));
                if (!File.Exists(file)) return false;
                if (!OperatingSystem.IsWindows() && (File.GetUnixFileMode(file) &
                    (UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute)) == 0)
                    return false;
            }
            return true;
        }
    }
}
