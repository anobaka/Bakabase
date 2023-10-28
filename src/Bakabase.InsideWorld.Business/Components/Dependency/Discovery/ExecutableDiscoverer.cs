using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Infrastructures.Components.App;
using Bakabase.Infrastructures.Components.App.Models.Constants;
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
        protected abstract string ArgumentsForAcquiringVersion { get; }

        protected abstract string ParseVersion(string output);

        public async Task<(string Location, string? Version)?> Discover(string defaultDirectory, CancellationToken ct)
        {
            string location;
            if (!DiscoverByDirectory(defaultDirectory, RequiredRelativeFileNamesWithoutExtensions))
            {
                var bin = AppService.OsPlatform switch
                {
                    OsPlatform.Windows => "where",
                    OsPlatform.Osx => "which",
                    OsPlatform.Linux => "which",
                    OsPlatform.FreeBsd => "which",
                    _ => throw new NotSupportedException($"{AppService.OsPlatform} is not supported")
                };

                var osb = new StringBuilder();
                var esb = new StringBuilder();
                var cmd = Cli.Wrap(bin)
                    .WithArguments(RelativeFileNameWithoutExtensionForAcquiringVersion)
                    .WithStandardOutputPipe(PipeTarget.ToStringBuilder(osb))
                    .WithStandardErrorPipe(PipeTarget.ToStringBuilder(esb))
                    .WithValidation(CommandResultValidation.None);

                var r = await cmd.ExecuteAsync(ct);
                if (r.ExitCode == 0)
                {
                    var output = osb.ToString();
                    var first = output.Split(Environment.NewLine)[0];
                    var dir = Path.GetDirectoryName(first)!;
                    if (!DiscoverByDirectory(dir, RequiredRelativeFileNamesWithoutExtensions))
                    {
                        Logger.LogError(
                            $"{RelativeFileNameWithoutExtensionForAcquiringVersion} is found but some of other required files are not: {string.Join(',', RelativeFileNameWithoutExtensionForAcquiringVersion)}");
                        return null;
                    }

                    location = dir;
                }
                else
                {
                    Logger.LogError(
                        $"Failed to find location by executable: {RelativeFileNameWithoutExtensionForAcquiringVersion}, exit code: {r.ExitCode}, output: {osb}, error: {esb}");
                    return null;
                }
            }
            else
            {
                location = defaultDirectory;
            }

            {
                var executableSuffix = AppService.OsPlatform == OsPlatform.Windows ? ".exe" : null;
                var executable = Path.Combine(location,
                    $"{RelativeFileNameWithoutExtensionForAcquiringVersion}{executableSuffix}");
                var osb = new StringBuilder();
                var esb = new StringBuilder();
                var cmd = Cli.Wrap(executable)
                    .WithArguments(ArgumentsForAcquiringVersion)
                    .WithStandardOutputPipe(PipeTarget.ToStringBuilder(osb))
                    .WithStandardErrorPipe(PipeTarget.ToStringBuilder(esb))
                    .WithValidation(CommandResultValidation.None);

                var r = await cmd.ExecuteAsync(ct);
                if (r.ExitCode == 0)
                {
                    var output = osb.ToString();

                    try
                    {
                        var version = ParseVersion(output);
                        return (location, version);
                    }
                    catch (Exception e)
                    {
                        Logger.LogError(
                            $"Failed to find version from output:{e.Message}{Environment.NewLine}{output}{Environment.NewLine}of command: {cmd}");
                        throw;
                    }
                }

                Logger.LogError(
                    $"Failed to get version by executable: {RelativeFileNameWithoutExtensionForAcquiringVersion}, exit code: {r.ExitCode}, output: {osb}, error: {esb}");
                return null;
            }
        }

        protected static bool DiscoverByDirectory(string directory, HashSet<string> relativePathsWithoutExt)
        {
            var dir = new DirectoryInfo(directory);
            if (dir.Exists)
            {
                var files = Directory.GetFiles(directory, "*", SearchOption.AllDirectories);
                var relativeFilesWithoutExt = files.Select(f =>
                {
                    f = f.Replace(dir.FullName, null).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar); ;
                    var ext = Path.GetExtension(f);
                    return !string.IsNullOrEmpty(ext) ? f.Remove(f.Length - ext.Length) : f;
                }).ToHashSet();
                var intersection = relativePathsWithoutExt.Intersect(relativeFilesWithoutExt);
                if (intersection.Count() == relativePathsWithoutExt.Count)
                {
                    return true;
                }
            }

            return false;
        }
    }
}