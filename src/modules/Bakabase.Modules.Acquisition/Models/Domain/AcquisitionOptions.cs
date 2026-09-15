using Bakabase.Modules.Acquisition.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Acquisition.Components;
using Bootstrap.Components.Configuration.Abstractions;
using Bootstrap.Components.Doc.Swagger;

namespace Bakabase.Modules.Acquisition.Models.Domain;

[Options]
[SwaggerCustomModel]
public record AcquisitionOptions
{
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
    /// Which sharing services to prefer when a post offers the same file on several. First match
    /// wins; anything not listed comes after.
    /// </summary>
    public List<AcquisitionDriveKind> PreferredDriveKinds { get; set; } = [];

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
