using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Services;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Acquisition.Models.Domain;
using Bootstrap.Components.Configuration.Abstractions;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace Bakabase.Service.Components.Acquisition;

/// <summary>What the first-run wizard asked for.</summary>
public record AcquisitionSetupInputModel
{
    /// <summary>Where the user's downloads land.</summary>
    public string? InboxDirectory { get; set; }

    /// <summary>Where acquired resources are filed.</summary>
    public string? LibraryRootDirectory { get; set; }

    /// <summary>The folder name an acquired resource gets.</summary>
    public string? DirectoryTemplate { get; set; }

    public List<AcquisitionDriveKind>? PreferredDriveKinds { get; set; }

    public decimal? AutoPurchaseLimit { get; set; }
}

public record AcquisitionSetupResult(bool CreatedInbox, bool CreatedLibrary, int? PathMarkId);

/// <summary>
/// Everything the first-run wizard does, in one call.
/// <para>
/// The path mark is the reason this is a service rather than four settings writes from the browser.
/// A library folder is worth nothing to Bakabase until something says "each folder in here is a
/// resource" — and that is the one path mark a user of the acquisition pipeline should never have
/// to learn about, because the wizard can state it for them.
/// </para>
/// </summary>
public class AcquisitionSetupService(
    IBOptionsManager<AcquisitionOptions> options,
    IPathMarkService pathMarks,
    ILogger<AcquisitionSetupService> logger)
{
    public async Task<AcquisitionSetupResult> ApplyAsync(AcquisitionSetupInputModel input,
        CancellationToken ct = default)
    {
        var createdInbox = EnsureDirectory(input.InboxDirectory);
        var createdLibrary = EnsureDirectory(input.LibraryRootDirectory);

        await options.SaveAsync(o =>
        {
            if (input.InboxDirectory is {Length: > 0}) o.InboxDirectory = input.InboxDirectory;
            if (input.LibraryRootDirectory is {Length: > 0}) o.LibraryRootDirectory = input.LibraryRootDirectory;
            if (input.DirectoryTemplate is {Length: > 0}) o.DirectoryTemplate = input.DirectoryTemplate;
            if (input.PreferredDriveKinds != null) o.PreferredDriveKinds = input.PreferredDriveKinds;
            if (input.AutoPurchaseLimit is { } limit) o.AutoPurchaseLimit = limit;
        });

        var markId = await EnsureLibraryMarkAsync(input.LibraryRootDirectory, ct);

        return new AcquisitionSetupResult(createdInbox, createdLibrary, markId);
    }

    /// <summary>
    /// "Every folder directly under the library root is a resource." Created once and never
    /// duplicated: running the wizard again finds the mark it made last time.
    /// </summary>
    private async Task<int?> EnsureLibraryMarkAsync(string? libraryRoot, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(libraryRoot)) return null;

        var normalized = Path.GetFullPath(libraryRoot).TrimEnd(Path.DirectorySeparatorChar);
        var existing = (await pathMarks.GetAll())
            .FirstOrDefault(m => m.Type == PathMarkType.Resource &&
                                 string.Equals(
                                     m.Path.TrimEnd(Path.DirectorySeparatorChar, '/'),
                                     normalized.Replace(Path.DirectorySeparatorChar, '/')
                                         .TrimEnd('/'),
                                     StringComparison.OrdinalIgnoreCase));

        if (existing != null) return existing.Id;

        var mark = await pathMarks.Add(new PathMark
        {
            Path = normalized,
            Type = PathMarkType.Resource,
            ConfigJson = JsonConvert.SerializeObject(new ResourceMarkConfig
            {
                MatchMode = PathMatchMode.Layer,
                Layer = 1,
                FsTypeFilter = PathFilterFsType.Directory,
            }),
        });

        logger.LogInformation("[Acquisition] Marked {Path} as a library of resources", normalized);

        return mark.Id;
    }

    private static bool EnsureDirectory(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || Directory.Exists(path)) return false;

        Directory.CreateDirectory(path);

        return true;
    }
}
