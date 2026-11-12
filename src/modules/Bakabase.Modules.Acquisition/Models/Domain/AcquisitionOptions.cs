using Bakabase.Modules.Acquisition.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Acquisition.Components;
using Bakabase.Modules.Workflow.Artifacts;
using Bootstrap.Components.Configuration.Abstractions;
using Bootstrap.Components.Doc.Swagger;

namespace Bakabase.Modules.Acquisition.Models.Domain;

[Options(OptionsKey = AcquisitionOptions.ConfigurationKey)]
[SwaggerCustomModel]
public record AcquisitionOptions
{
    public const string ConfigurationKey = "Acquisition";

    /// <summary>
    /// Where the user drops files they fetched themselves. Set once — pointing the browser's or the
    /// cloud drive client's download directory at it is the whole configuration.
    /// </summary>
    public string? InboxDirectory { get; set; }

    /// <summary>Where acquired resources are filed away.</summary>
    public string? LibraryRootDirectory { get; set; }

    /// <summary>
    /// The directory name an acquired resource gets. Placeholders are resolved from the work item's
    /// variables; the default is just the title.
    /// </summary>
    public string DirectoryTemplate { get; set; } = "{Title}";

    /// <summary>
    /// Download/work input root for new acquisition plans. Unset inherits the download center's
    /// default. Processing workspaces and final deliveries have their own independent settings;
    /// changing this default never moves files or changes a saved plan.
    /// </summary>
    public string? DownloadRootDirectory { get; set; }

    /// <summary>Final delivery root for new plans; unset inherits the task's download root plus Acquired.</summary>
    public string? DeliveryRootDirectory { get; set; }
    public string DeliveryDirectoryTemplate { get; set; } = "{Title} [A{TaskId}]";
    public string DeliveryPartDirectoryTemplate { get; set; } = "{PartTitle} [{PartId}]";
    /// <summary>Captured for new plans only; publication changes never alter extraction checkpoints.</summary>
    public ArtifactDeliveryPolicy DeliveryContentLayout { get; set; } = new();

    /// <summary>Retain the previous published files when a completed acquisition delivers a verified replacement.</summary>
    public bool KeepSupersededDeliveryFiles { get; set; }

    /// <summary>
    /// Optional processing workspace parent for new operations. Unset keeps isolated working
    /// files alongside the downloaded or selected input files; existing operations retain their location.
    /// </summary>
    public string? ProcessingWorkspaceDirectory { get; set; }
    public ArtifactStoragePolicy StoragePolicy { get; set; } = new();

    /// <summary>Runtime reserve for each volume written by acquisition work. Zero disables this
    /// additional reserve. Changing it does not change operation identities or saved file paths.</summary>
    public long MinimumFreeSpaceBytes { get; set; } = 100L * 1024 * 1024 * 1024;
    /// <summary>Use the 1 GiB reserve for acquisition writes while retaining the configured threshold.</summary>
    private bool? _useEmergencySpaceReserve;
    public bool UseEmergencySpaceReserve
    {
        get => _useEmergencySpaceReserve ?? false;
        set => _useEmergencySpaceReserve = value;
    }

    public void PreserveUnspecifiedReserveMode(AcquisitionOptions current) =>
        _useEmergencySpaceReserve ??= current.UseEmergencySpaceReserve;

    /// <summary>
    /// Which sharing services to prefer when a post offers the same file on several. First match
    /// wins; anything not listed comes after.
    /// </summary>
    public List<AcquisitionDriveKind> PreferredDriveKinds { get; set; } = [];

    /// <summary>Provider IDs ordered for new automatic same-content mirror selections. Missing providers follow in original order.</summary>
    [Newtonsoft.Json.JsonProperty(ObjectCreationHandling = Newtonsoft.Json.ObjectCreationHandling.Replace)]
    [System.Text.Json.Serialization.JsonObjectCreationHandling(System.Text.Json.Serialization.JsonObjectCreationHandling.Replace)]
    public List<string> SourceProviderPriority { get; set; } = ["baidu", "http", "pixeldrain", "gofile", "pikpak", "googleDrive", "oneDrive", "magnet", "torrent", "exHentai"];

    /// <summary>
    /// Which recipe each kind of lead runs by default. Absent entries fall back to
    /// <see cref="BuiltinAcquisitionRecipes.DefaultRecipeNameFor"/>.
    /// </summary>
    public Dictionary<AcquisitionLeadKind, string> RecipeByLeadKind { get; set; } = new();

    /// <summary>
    /// The most that may be spent on one piece of paid content without asking. Zero — the default —
    /// means never buy unattended; anything dearer suspends and shows the price.
    /// </summary>
    public decimal AutoPurchaseLimit { get; set; }

    /// <summary>Delete an archive once it has been extracted successfully.</summary>
    public bool DeleteArchiveAfterExtraction { get; set; } = true;

    /// <summary>
    /// Try passwords used recently when the ones found in the content and the file name fail. Off
    /// for anyone who would rather be asked than have Bakabase guess through their password list.
    /// </summary>
    public bool TryRecentPasswords { get; set; } = true;

    /// <summary>How many of the recent passwords to try.</summary>
    public int RecentPasswordCandidateCount { get; set; } = 20;

    /// <summary>
    /// How many acquisitions run at once. Two by default: most of the time is spent waiting on a
    /// person or a network, and more parallel downloads mostly means more ways to be rate-limited.
    /// </summary>
    public int Concurrency { get; set; } = 2;
}
