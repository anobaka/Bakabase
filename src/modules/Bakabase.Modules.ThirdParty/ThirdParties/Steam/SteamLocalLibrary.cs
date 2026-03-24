using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace Bakabase.Modules.ThirdParty.ThirdParties.Steam;

/// <summary>
/// Detects locally installed Steam apps by parsing Steam's library folders and app manifests.
/// Works on Windows, macOS, and Linux.
/// </summary>
public class SteamLocalLibrary
{
    private readonly ILogger<SteamLocalLibrary> _logger;

    public SteamLocalLibrary(ILoggerFactory loggerFactory)
    {
        _logger = loggerFactory.CreateLogger<SteamLocalLibrary>();
    }

    public record InstalledApp(int AppId, string InstallPath);

    /// <summary>
    /// Discovers all locally installed Steam apps by scanning library folders.
    /// Returns a dictionary of AppId → install path.
    /// </summary>
    public Dictionary<int, string> DetectInstalledApps()
    {
        var result = new Dictionary<int, string>();

        try
        {
            var steamPath = FindSteamInstallPath();
            if (steamPath == null)
            {
                _logger.LogInformation("Steam installation not found on this machine");
                return result;
            }

            _logger.LogInformation("Found Steam installation at: {Path}", steamPath);

            var libraryFolders = GetLibraryFolders(steamPath);
            _logger.LogInformation("Found {Count} Steam library folder(s)", libraryFolders.Count);

            foreach (var folder in libraryFolders)
            {
                var steamAppsDir = Path.Combine(folder, "steamapps");
                if (!Directory.Exists(steamAppsDir))
                {
                    continue;
                }

                try
                {
                    var manifests = Directory.GetFiles(steamAppsDir, "appmanifest_*.acf");
                    foreach (var manifest in manifests)
                    {
                        var app = ParseAppManifest(manifest, steamAppsDir);
                        if (app != null && !result.ContainsKey(app.AppId))
                        {
                            result[app.AppId] = app.InstallPath;
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to scan Steam library folder: {Folder}", folder);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to detect installed Steam apps");
        }

        _logger.LogInformation("Detected {Count} installed Steam app(s)", result.Count);
        return result;
    }

    /// <summary>
    /// Checks if a specific AppId is installed locally and returns the install path.
    /// </summary>
    public string? GetInstallPath(int appId)
    {
        var installed = DetectInstalledApps();
        return installed.GetValueOrDefault(appId);
    }

    private string? FindSteamInstallPath()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return FindSteamPathWindows();
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return FindSteamPathMacOS();
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return FindSteamPathLinux();
        }

        return null;
    }

    private string? FindSteamPathWindows()
    {
        // Try registry first
        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\WOW6432Node\Valve\Steam");
            var path = key?.GetValue("InstallPath") as string;
            if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
            {
                return path;
            }
        }
        catch
        {
            // Registry access may fail
        }

        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\Valve\Steam");
            var path = key?.GetValue("InstallPath") as string;
            if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
            {
                return path;
            }
        }
        catch
        {
            // Registry access may fail
        }

        // Fallback to common paths
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        var defaultPath = Path.Combine(programFiles, "Steam");
        if (Directory.Exists(defaultPath))
        {
            return defaultPath;
        }

        return null;
    }

    private string? FindSteamPathMacOS()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var path = Path.Combine(home, "Library", "Application Support", "Steam");
        return Directory.Exists(path) ? path : null;
    }

    private string? FindSteamPathLinux()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        // Check common Linux Steam paths
        string[] candidates =
        [
            Path.Combine(home, ".steam", "steam"),
            Path.Combine(home, ".local", "share", "Steam"),
            Path.Combine(home, ".steam", "debian-installation"),
            Path.Combine(home, "snap", "steam", "common", ".local", "share", "Steam")
        ];

        foreach (var candidate in candidates)
        {
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    /// <summary>
    /// Parses libraryfolders.vdf to find all Steam library folder paths.
    /// Always includes the main Steam installation directory.
    /// </summary>
    private List<string> GetLibraryFolders(string steamPath)
    {
        var folders = new List<string> { steamPath };

        var vdfPath = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");
        if (!File.Exists(vdfPath))
        {
            // Try config path (older Steam versions)
            vdfPath = Path.Combine(steamPath, "config", "libraryfolders.vdf");
        }

        if (!File.Exists(vdfPath))
        {
            return folders;
        }

        try
        {
            var content = File.ReadAllText(vdfPath);
            // Match "path" values in VDF format: "path"		"C:\\Program Files (x86)\\Steam"
            var pathPattern = new Regex("\"path\"\\s+\"([^\"]+)\"", RegexOptions.IgnoreCase);
            var matches = pathPattern.Matches(content);

            foreach (Match match in matches)
            {
                var path = match.Groups[1].Value.Replace("\\\\", "\\");
                if (Directory.Exists(path) && !folders.Contains(path, StringComparer.OrdinalIgnoreCase))
                {
                    folders.Add(path);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse libraryfolders.vdf at {Path}", vdfPath);
        }

        return folders;
    }

    /// <summary>
    /// Parses an appmanifest_*.acf file to extract AppId and install directory.
    /// </summary>
    private InstalledApp? ParseAppManifest(string manifestPath, string steamAppsDir)
    {
        try
        {
            var content = File.ReadAllText(manifestPath);

            var appIdMatch = Regex.Match(content, "\"appid\"\\s+\"(\\d+)\"", RegexOptions.IgnoreCase);
            var installDirMatch = Regex.Match(content, "\"installdir\"\\s+\"([^\"]+)\"", RegexOptions.IgnoreCase);

            if (!appIdMatch.Success || !installDirMatch.Success)
            {
                return null;
            }

            if (!int.TryParse(appIdMatch.Groups[1].Value, out var appId))
            {
                return null;
            }

            var installDir = installDirMatch.Groups[1].Value;
            var fullPath = Path.Combine(steamAppsDir, "common", installDir);

            // Only return if the directory actually exists
            if (!Directory.Exists(fullPath))
            {
                return null;
            }

            return new InstalledApp(appId, fullPath);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse app manifest: {Path}", manifestPath);
            return null;
        }
    }
}
