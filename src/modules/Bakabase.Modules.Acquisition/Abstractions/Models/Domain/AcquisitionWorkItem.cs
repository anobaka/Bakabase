using Bakabase.Modules.Acquisition.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Workflow.Abstractions.Components;

namespace Bakabase.Modules.Acquisition.Abstractions.Models.Domain;

/// <summary>
/// One downloadable thing found in shared content.
/// </summary>
/// <param name="Url">Where it is.</param>
/// <param name="AccessCode">The code the sharing service asks for before showing the file.</param>
/// <param name="ArchivePassword">The password the archive itself needs, which is usually a different one.</param>
/// <param name="DriveKind">Which service it points at, so a link can be preferred or fetched automatically.</param>
public record AcquisitionLink(
    string Url,
    string? AccessCode = null,
    string? ArchivePassword = null,
    AcquisitionDriveKind DriveKind = AcquisitionDriveKind.Unknown);

/// <summary>
/// Something that was bought on the user's behalf while acquiring. Kept so the spending is visible
/// afterwards rather than only in a log line.
/// </summary>
public record PurchaseRecord(decimal Price, DateTime PurchasedAt, string Where);

/// <summary>
/// The data a run carries from step to step, enriched as it goes. Serializable in full: the host
/// writes a snapshot after every step, so a restart resumes from the cursor with everything the
/// next step needs.
/// <para>
/// It is an <see cref="ITextWorkpiece"/> so the existing <c>transform.text.*</c> activities can
/// rewrite the directory name a recipe is about to use, without the acquisition module knowing
/// anything about them.
/// </para>
/// <para>
/// Note that the compiler-generated equality compares the collection members by reference, so two
/// items with identical contents are not equal. Compare snapshots as JSON rather than with
/// <c>==</c>.
/// </para>
/// </summary>
public sealed record AcquisitionWorkItem : ITextWorkpiece
{
    /// <summary>The resource being acquired. A run has exactly one.</summary>
    public required int ResourceId { get; init; }

    /// <summary>How it is being obtained — the lead decides which recipe runs by default.</summary>
    public required AcquisitionLeadKind LeadKind { get; init; }

    /// <summary>The lead's value: a link, a document row, or a platform identity.</summary>
    public required string LeadValue { get; init; }

    /// <summary>Context only — statistics and notifications. It never changes what runs.</summary>
    public int? CollectionId { get; init; }

    /// <summary>Parsed from the shared content, or the resource's own name.</summary>
    public string? Title { get; init; }

    public IReadOnlyList<AcquisitionLink> Links { get; init; } = [];

    public int? SelectedLinkIndex { get; init; }

    /// <summary>Where this run keeps its files while working.</summary>
    public string WorkingDirectory { get; init; } = "";

    /// <summary>Files fetched or claimed from the inbox.</summary>
    public IReadOnlyList<string> Files { get; init; } = [];

    /// <summary>What unpacking produced.</summary>
    public string? ExtractedDirectory { get; init; }

    /// <summary>
    /// The directory name the resource will end up under. This is the item's working text, so a
    /// recipe can put any text transform in front of the placement step to clean it up.
    /// </summary>
    public string WorkingName { get; init; } = "";

    /// <summary>Where placement put it.</summary>
    public string? TargetDirectory { get; init; }

    /// <summary>Whatever a step wants to leave for a later one.</summary>
    public IReadOnlyDictionary<string, string> Variables { get; init; } =
        new Dictionary<string, string>();

    public IReadOnlyList<PurchaseRecord> Purchases { get; init; } = [];

    public string WorkingText => WorkingName;

    public object WithWorkingText(string workingText) => this with { WorkingName = workingText };

    /// <summary>The link a step is meant to act on, if one has been chosen.</summary>
    public AcquisitionLink? SelectedLink =>
        SelectedLinkIndex is { } i && i >= 0 && i < Links.Count ? Links[i] : null;
}
