using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Bakabase.InsideWorld.Business.Components.Dependency.Discovery;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace Bakabase.InsideWorld.Business.Components.Dependency.Implementations.MegaCmd;

/// <summary>Discovery must never start/connect to MEGAcmd: even its version command can start a persistent server.</summary>
public sealed class MegaCmdDiscoverer(ILoggerFactory loggerFactory) : IDiscoverer
{
    public async Task<(string Location, string? Version)?> Discover(string defaultDirectory, CancellationToken ct)
    {
        try
        {
            if (OperatingSystem.IsLinux())
            {
                var asset = MegaCmdRelease.Select(DependentComponentPlatforms.CurrentArchitecture);
                var path = Path.Combine(defaultDirectory, asset.DirectoryName);
                return await MegaCmdRelease.VerifyInstallationAsync(path, asset, ct) ? (path, MegaCmdRelease.Version) : null;
            }
            if (OperatingSystem.IsWindows())
            {
                var paths = new[] { defaultDirectory, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MEGAcmd") }
                    .Concat((Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator));
                foreach (var path in paths.Where(p => !string.IsNullOrWhiteSpace(p)).Distinct())
                {
                    ct.ThrowIfCancellationRequested();
                    var directory = path.Trim('"');
                    var server = Path.Combine(directory, "MEGAcmdServer.exe");
                    if (!File.Exists(server) || !File.Exists(Path.Combine(directory, "MEGAclient.exe"))) continue;
                    var version = FileVersionInfo.GetVersionInfo(server).ProductVersion;
                    if (System.Version.TryParse(version, out var v) && v >= new Version(2, 7, 0)) return (directory, version);
                }
            }
            if (OperatingSystem.IsMacOS())
            {
                foreach (var bundle in new[] { Path.Combine(defaultDirectory, "MEGAcmd.app"), "/Applications/MEGAcmd.app" })
                {
                    var info = Path.Combine(bundle, "Contents", "Info.plist");
                    var bin = Path.Combine(bundle, "Contents", "MacOS");
                    if (!File.Exists(info) || !File.Exists(Path.Combine(bin, "mega-exec")) ||
                        !File.Exists(Path.Combine(bin, "mega-cmd"))) continue;
                    // Official app plist is XML. Binary/unknown formats are not silently assumed compatible.
                    using var reader = System.Xml.XmlReader.Create(info, new System.Xml.XmlReaderSettings
                        { DtdProcessing = System.Xml.DtdProcessing.Ignore, XmlResolver = null, MaxCharactersInDocument = 65536 });
                    var document = XDocument.Load(reader);
                    var version = document.Descendants("key").FirstOrDefault(e => e.Value == "CFBundleShortVersionString")?.NextNode as XElement;
                    if (System.Version.TryParse(version?.Value, out var v) && v >= new Version(2, 7, 0)) return (bin, version!.Value);
                }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or System.Xml.XmlException)
        { loggerFactory.CreateLogger<MegaCmdDiscoverer>().LogDebug("Skipping unavailable MEGAcmd installation."); }
        return null;
    }
}
