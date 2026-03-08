using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.Dependency.Discovery;
using Microsoft.Extensions.Logging;

namespace Bakabase.InsideWorld.Business.Components.Dependency.Implementations.LocaleEmulator
{
    /// <summary>
    /// Discovers Locale Emulator by looking for LEProc.exe in the component directory.
    /// Since LE doesn't support version querying via CLI, we use a simple file-based discovery.
    /// </summary>
    public class LocaleEmulatorDiscoverer : IDiscoverer
    {
        private readonly ILogger _logger;

        public LocaleEmulatorDiscoverer(ILoggerFactory loggerFactory)
        {
            _logger = loggerFactory.CreateLogger<LocaleEmulatorDiscoverer>();
        }

        public async Task<(string Location, string? Version)?> Discover(string defaultDirectory,
            CancellationToken ct)
        {
            var leProcPath = Path.Combine(defaultDirectory, "LEProc.exe");
            if (File.Exists(leProcPath))
            {
                // Try to get version from the file's version info
                string? version = null;
                try
                {
                    var versionInfo = System.Diagnostics.FileVersionInfo.GetVersionInfo(leProcPath);
                    if (!string.IsNullOrEmpty(versionInfo.FileVersion))
                    {
                        version = versionInfo.FileVersion;
                    }
                }
                catch (Exception e)
                {
                    _logger.LogWarning(e, "Failed to get Locale Emulator version info");
                }

                return (defaultDirectory, version ?? "unknown");
            }

            return null;
        }
    }
}
