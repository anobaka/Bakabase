namespace Bakabase.Abstractions.Components.FileSystem;

/// <summary>
/// Runtime storage locations, relative to AppService's effective AppData directory.
/// Resolving a location never creates it. Keep writers and storage diagnostics on the
/// same catalogue; startup/anchor/profile ownership remains with AppService.
/// </summary>
public static class AppDataStorage
{
    public const string DataDirectoryName = "data";
    public const string AigcDirectoryName = "aigc";

    public static readonly Location Configuration = new("configuration", "configs");
    public static readonly Location Logs = new("logs", "logs");
    public static readonly Location Covers = new("covers", "data/covers");
    public static readonly Location LegacyCovers = new("covers", "data/cover");
    public static readonly Location EnhancerFiles = new("enhancerFiles", "data/enhancers");
    public static readonly Location LegacyEnhancerFiles = new("enhancerFiles", "data/enhancer");
    public static readonly Location Attachments = new("attachments", "data/attachments");
    public static readonly Location Aigc = new("aigc", $"data/{AigcDirectoryName}");
    public static readonly Location Downloads = new("downloads", "downloader");
    public static readonly Location TorrentDownloads = new("downloads", "bt-downloads");
    public static readonly Location HttpDownloads = new("downloads", "http-downloads");
    public static readonly Location TorrentCache = new("downloads", "acquisition-torrent-cache");
    // Retained data from earlier feature implementations remains classified after upgrades.
    public static readonly Location LegacyDownloads = new("downloads", "downloads");
    public static readonly Location CloudDownloads = new("downloads", "cloud-downloads");
    public static readonly Location Acquisition = new("acquisition", "acquisition");
    public static readonly Location FileProcessing = new("fileProcessing", "file-processing");
    public static readonly Location DownloadResultWorkflows = new("fileProcessing", "download-result-workflows");
    public static readonly Location ArtifactOperations = new("fileProcessing", "artifact-operations");
    public static readonly Location ArtifactPlanCheckpoints = new("fileProcessing", "artifact-plan-checkpoints");
    public static readonly Location Temporary = new("temporary", "temp");
    public static readonly Location LegacyCache = new("temporary", "data/cache");
    public static readonly Location ImportWork = new("temporary", ".bakabase-import-work");
    public static readonly Location RelocationWork = new("temporary", ".bakabase-relocate-work");
    public static readonly Location RelocationStaging = new("temporary", ".bakabase_relocate_staging");
    public static readonly Location Dependencies = new("dependencies", "components");
    public static readonly Location Backups = new("backups", "backups");
    public static readonly Location DataSync = new("dataSync", "data-sync");
    public static readonly Location Federation = new("federation", "federation");
    public static readonly Location RemoteAccess = new("remoteAccess", "remote-access");
    public static readonly Location OAuthAuthorizations = new("remoteAccess", "oauth-authorizations");
    public static readonly Location OAuthClients = new("remoteAccess", "oauth-clients");
    public static readonly Location Analytics = new("analytics", "analytics");
    public static readonly Location WebView2 = new("browser", "WebView2");
    public static readonly Location WebKit = new("browser", "WebKit");

    public static IReadOnlyList<Location> Locations { get; } = Array.AsReadOnly<Location>([
        Configuration, Logs, Covers, LegacyCovers, EnhancerFiles, LegacyEnhancerFiles, Attachments, Aigc,
        Downloads, TorrentDownloads, HttpDownloads, TorrentCache, LegacyDownloads, CloudDownloads, Acquisition,
        FileProcessing, DownloadResultWorkflows, ArtifactOperations, ArtifactPlanCheckpoints,
        Temporary, LegacyCache, ImportWork, RelocationWork, RelocationStaging,
        Dependencies, Backups, DataSync, Federation, RemoteAccess, OAuthAuthorizations, OAuthClients,
        Analytics, WebView2, WebKit
    ]);

    // Stable IDs for localized presentation. Files not assigned below always enter Other.
    public static IReadOnlyList<string> CategoryIds { get; } = Array.AsReadOnly<string>([
        "database", "logs", "configuration", "covers", "enhancerFiles", "attachments", "aigc",
        "downloads", "acquisition", "fileProcessing", "temporary", "dependencies", "backups",
        "dataSync", "federation", "remoteAccess", "analytics", "browser", "other"
    ]);

    public sealed record Location(string CategoryId, string RelativePath)
    {
        public string GetPath(string appDataDirectory, params string[] descendants) =>
            Path.Combine([appDataDirectory, RelativePath.Replace('/', Path.DirectorySeparatorChar), ..descendants]);
    }

    /// <summary>A file or directory belongs to exactly one category, using path-segment boundaries.</summary>
    public static string GetCategoryId(string relativePath)
    {
        var normalized = relativePath.Replace(Path.DirectorySeparatorChar, '/');
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        foreach (var location in Locations)
        {
            if (normalized.Equals(location.RelativePath, comparison) ||
                normalized.StartsWith(location.RelativePath + "/", comparison)) return location.CategoryId;
        }

        if (!normalized.Contains('/'))
        {
            // Log SQLite and its WAL/SHM files serve logs rather than the media database.
            if (normalized.StartsWith("bootstrap_log.db", comparison) ||
                normalized.EndsWith(".log", comparison)) return "logs";
            var databaseName = normalized.EndsWith("-wal", comparison) || normalized.EndsWith("-shm", comparison) ||
                               normalized.EndsWith("-journal", comparison)
                ? normalized[..normalized.LastIndexOf('-')]
                : normalized;
            if (databaseName.EndsWith(".db", comparison) || databaseName.EndsWith(".sqlite", comparison) ||
                databaseName.EndsWith(".sqlite3", comparison)) return "database";
            if (normalized.EndsWith(".json", comparison) || normalized.EndsWith(".json.tmp", comparison) ||
                normalized == ".redirect") return "configuration";
        }

        return "other";
    }

    /// <summary>Only expose a dedicated parent when the whole category has one location.</summary>
    public static string? GetCategoryPath(string appDataDirectory, string categoryId)
    {
        // These categories also contain root-level files, or unknown residual paths.
        if (categoryId is "database" or "logs" or "configuration" or "other") return null;
        var locations = Locations.Where(x => x.CategoryId == categoryId && ExistsWithoutLinks(appDataDirectory, x)).ToArray();
        return locations.Length == 1 ? locations[0].GetPath(appDataDirectory) : null;
    }

    private static bool ExistsWithoutLinks(string root, Location location)
    {
        try
        {
            var path = root;
            foreach (var segment in location.RelativePath.Split('/'))
            {
                path = Path.Combine(path, segment);
                if (!Directory.Exists(path) || (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) return false;
            }
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
    }
}
