using Bakabase.Abstractions.Components.FileSystem;
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
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain;
using Bakabase.Service.Components.Acquisition.Planning;
using Bakabase.Modules.Acquisition.Components;
using Bakabase.Modules.Acquisition.Models.Domain;
using Bootstrap.Components.Configuration.Abstractions;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Bakabase.Service.Components.FileProcessing;
using Bakabase.Modules.Workflow.Artifacts;

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

    /// <summary>Null leaves the override unchanged; empty inherits the download center default for new tasks.</summary>
    public string? DownloadRootDirectory { get; set; }

    /// <summary>Null leaves the override unchanged; empty clears it to inherit the saved download root plus Acquired.</summary>
    public string? DeliveryRootDirectory { get; set; }
    public string? DeliveryDirectoryTemplate { get; set; }
    public string? DeliveryPartDirectoryTemplate { get; set; }
    public ArtifactDeliveryPolicy? DeliveryContentLayout { get; set; }
    public bool? KeepSupersededDeliveryFiles { get; set; }

    /// <summary>Null leaves the override unchanged; empty uses storage alongside the input files for new processing operations.</summary>
    public string? ProcessingWorkspaceDirectory { get; set; }

    public List<AcquisitionDriveKind>? PreferredDriveKinds { get; set; }

    public decimal? AutoPurchaseLimit { get; set; }
    public ArtifactStoragePolicy? StoragePolicy { get; set; }
    /// <summary>Null keeps the runtime reserve; zero disables the additional reserve.</summary>
    public long? MinimumFreeSpaceBytes { get; set; }
    public bool? UseEmergencySpaceReserve { get; set; }
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
    ILogger<AcquisitionSetupService> logger,
    IUserStoragePolicy storagePolicy)
{
    private static readonly SemaphoreSlim OptionsGate = new(1, 1);

    internal async Task ReplaceOptionsAsync(AcquisitionOptions model, CancellationToken ct)
    {
        await OptionsGate.WaitAsync(ct);
        try
        {
            model.PreserveUnspecifiedReserveMode(options.Value);
            await options.SaveAsync(model);
        }
        finally { OptionsGate.Release(); }
    }

    private async Task UpdateOptionsAsync(Action<AcquisitionOptions> change, CancellationToken ct)
    {
        await OptionsGate.WaitAsync(ct);
        try { await options.SaveAsync(change); }
        finally { OptionsGate.Release(); }
    }

    public async Task<AcquisitionSetupResult> ApplyAsync(AcquisitionSetupInputModel input,
        CancellationToken ct = default)
    {
        if (input.MinimumFreeSpaceBytes is < 0 or > 9_007_199_254_740_991L)
            throw new ArgumentException("STORAGE_RESERVE_INVALID");
        var template = input.DirectoryTemplate is {Length: > 0}
            ? input.DirectoryTemplate : options.Value.DirectoryTemplate;
        // Legacy placement settings remain frozen unless explicitly edited. A malformed
        // historical template must not block modern processing/delivery configuration.
        if (input.DirectoryTemplate is {Length: > 0} || !string.IsNullOrWhiteSpace(input.LibraryRootDirectory))
            AcquisitionDirectoryNamer.ValidateTemplate(template);
        var delivery = new AcquisitionDeliverySettings
        {
            RootDirectory = input.DeliveryRootDirectory ?? options.Value.DeliveryRootDirectory ?? "",
            DirectoryTemplate = input.DeliveryDirectoryTemplate ?? options.Value.DeliveryDirectoryTemplate,
            PartDirectoryTemplate = input.DeliveryPartDirectoryTemplate ?? options.Value.DeliveryPartDirectoryTemplate,
            ContentLayout = input.DeliveryContentLayout ?? options.Value.DeliveryContentLayout
        };
        AcquisitionDeliveryDirectoryResolver.ValidateSettings(delivery);
        if (delivery.RootDirectory.Length > 0) storagePolicy.EnsurePathAllowed(delivery.RootDirectory);
        AcquisitionOutputDirectoryResolver.ValidateConfiguredRoot(input.DownloadRootDirectory, storagePolicy);
        FileProcessingWorkspaceResolver.ValidateConfiguredRoot(input.ProcessingWorkspaceDirectory, storagePolicy);
        var policy = input.StoragePolicy ?? (input.ProcessingWorkspaceDirectory == null ? null : options.Value.StoragePolicy with
            {ProcessingWorkspaceDirectory = input.ProcessingWorkspaceDirectory});
        if (policy != null)
        {
            ArtifactStoragePolicies.Validate(policy);
            FileProcessingWorkspaceResolver.ValidateConfiguredRoot(policy.ProcessingWorkspaceDirectory, storagePolicy);
        }

        // Check the whole request before creating either directory or saving options.
        foreach (var path in new[] {input.InboxDirectory, input.LibraryRootDirectory})
            if (!string.IsNullOrWhiteSpace(path)) storagePolicy.EnsurePathAllowed(path);
        var createdInbox = EnsureDirectory(input.InboxDirectory);
        var createdLibrary = EnsureDirectory(input.LibraryRootDirectory);

        await UpdateOptionsAsync(o =>
        {
            if (input.InboxDirectory is {Length: > 0}) o.InboxDirectory = input.InboxDirectory;
            if (input.LibraryRootDirectory is {Length: > 0}) o.LibraryRootDirectory = input.LibraryRootDirectory;
            if (input.DirectoryTemplate is {Length: > 0}) o.DirectoryTemplate = input.DirectoryTemplate;
            if (input.DownloadRootDirectory != null)
                o.DownloadRootDirectory = string.IsNullOrWhiteSpace(input.DownloadRootDirectory)
                    ? null : Path.GetFullPath(input.DownloadRootDirectory);
            if (input.DeliveryRootDirectory != null) o.DeliveryRootDirectory = input.DeliveryRootDirectory.Length == 0 ? null : Path.GetFullPath(input.DeliveryRootDirectory);
            if (input.DeliveryDirectoryTemplate != null) o.DeliveryDirectoryTemplate = delivery.DirectoryTemplate;
            if (input.DeliveryPartDirectoryTemplate != null) o.DeliveryPartDirectoryTemplate = delivery.PartDirectoryTemplate;
            if (input.DeliveryContentLayout != null) o.DeliveryContentLayout = input.DeliveryContentLayout;
            if (input.KeepSupersededDeliveryFiles is { } keep) o.KeepSupersededDeliveryFiles = keep;
            if (input.ProcessingWorkspaceDirectory != null)
                o.ProcessingWorkspaceDirectory = string.IsNullOrWhiteSpace(input.ProcessingWorkspaceDirectory)
                    ? null : Path.GetFullPath(input.ProcessingWorkspaceDirectory.Trim());
            if (policy != null)
            {
                o.StoragePolicy = policy;
                o.ProcessingWorkspaceDirectory = policy.ProcessingWorkspaceDirectory.Length == 0 ? null : policy.ProcessingWorkspaceDirectory;
            }
            if (input.PreferredDriveKinds != null)
            {
                o.PreferredDriveKinds = input.PreferredDriveKinds
                    .Where(kind => kind != AcquisitionDriveKind.Unknown && Enum.IsDefined(kind))
                    .Distinct().ToList();
            }
            if (input.AutoPurchaseLimit is { } limit) o.AutoPurchaseLimit = limit;
            if (input.MinimumFreeSpaceBytes is { } reserve) o.MinimumFreeSpaceBytes = reserve;
            if (input.UseEmergencySpaceReserve is { } useEmergency) o.UseEmergencySpaceReserve = useEmergency;
        }, ct);

        // Variable categories and workflow overrides can change depth. Those paths receive a
        // boundary and an exact resource mark when placement knows the final destination.
        var markId = string.IsNullOrWhiteSpace(input.LibraryRootDirectory) || AcquisitionDirectoryNamer.HasSubdirectories(template)
            ? null
            : await EnsureLibraryMarkAsync(input.LibraryRootDirectory, ct);

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
