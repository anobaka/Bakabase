using System;
using System.IO;
using System.Linq;
using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.InsideWorld.Business.Components.Configurations.Models.Domain;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.Modules.Acquisition.Models.Domain;
using Bootstrap.Components.Configuration.Abstractions;

namespace Bakabase.Service.Components.Acquisition.Planning;

/// <summary>
/// Resolves a new acquisition's directory once, before it is persisted. Null means inherit the
/// common download default; an explicit empty string keeps the task unconfigured. Saved plans
/// never consult changing defaults during execution or history reads.
/// </summary>
public sealed class AcquisitionOutputDirectoryResolver(IBOptions<DownloaderGlobalOptions> options,
    IBOptions<AcquisitionOptions>? acquisitions = null)
{
    public string? ResolveNew(string? directory)
    {
        if (directory != null) return directory;
        var root = acquisitions?.Value.DownloadRootDirectory;
        if (string.IsNullOrWhiteSpace(root)) return options.Value.DefaultDownloadPath;
        // Producer completion snapshots this value in its transaction. Filesystem availability
        // belongs to configuration validation and acquisition preflight, not parser completion.
        return Path.GetFullPath(root);
    }

    public static void ValidateConfiguredRoot(string? directory, IUserStoragePolicy? storage = null)
    {
        if (string.IsNullOrWhiteSpace(directory)) return;
        if (!Path.IsPathFullyQualified(directory) || directory.Length > 4096 || directory.Any(char.IsControl))
            throw new ArgumentException("Choose an absolute acquisition download directory.");
        ArtifactManifestFiles.RejectLinks(directory);
        storage?.EnsurePathAllowed(directory);
        if (!Directory.Exists(directory))
            throw new DirectoryNotFoundException("The acquisition download storage is unavailable. Reconnect it before continuing.");
    }
}
