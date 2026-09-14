using Bakabase.Modules.Acquisition.Abstractions.Models.Domain;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Acquisition.Models.Domain;

namespace Bakabase.Modules.Acquisition.Components;

/// <summary>
/// The recipes that ship with Bakabase. They are seeds: created once by name, marked built-in, and
/// meant to be copied and changed rather than edited in place — the same arrangement as the
/// built-in text vocabularies.
/// <para>
/// Each recipe combines reusable acquisition nodes. Downloads may be placed directly or unpacked
/// first; platform-managed directories can be associated with the resource in place.
/// </para>
/// </summary>
public static class BuiltinAcquisitionRecipes
{
    /// <summary>
    /// Someone shared a link on a page or in a document. Read it, pick a link, let the user fetch
    /// it from a cloud drive into the inbox, then unpack and file it away.
    /// </summary>
    public const string ForumPostWithCloudDrive = "Forum post + cloud drive";

    /// <summary>An http(s) link that is the file itself.</summary>
    public const string DirectDownload = "Direct download";

    /// <summary>A magnet link, fetched by whatever the user already uses for torrents.</summary>
    public const string Magnet = "Magnet";

    public const string MagnetDownload = "Magnet download";
    public const string TorrentDownload = "Torrent download";

    /// <summary>The user owns it on a platform that can hand it over.</summary>
    public const string PlatformFetch = "Platform fetch";

    /// <summary>The files are already on disk somewhere; only the library does not know.</summary>
    public const string LocalDirectory = "Local directory";

    private static AcquisitionRecipeStep Step(string kind) => new(kind);

    public static readonly IReadOnlyList<AcquisitionRecipe> All =
    [
        new(ForumPostWithCloudDrive,
        [
            Step(AcquisitionStepKinds.ResolveSharedContent),
            Step(AcquisitionStepKinds.SelectLink),
            Step(AcquisitionStepKinds.WaitForInbox),
            Step(AcquisitionStepKinds.Unpack),
            Step(AcquisitionStepKinds.Place),
            Step(AcquisitionStepKinds.Materialize)
        ], "Extract download links and passwords from shared content, then receive downloaded files and import them.",
            "acquisition.workflow.sharedContent.description"),
        new(DirectDownload,
        [
            Step(AcquisitionStepKinds.FetchHttp),
            Step(AcquisitionStepKinds.Unpack),
            Step(AcquisitionStepKinds.Place),
            Step(AcquisitionStepKinds.Materialize)
        ], "Download an HTTP(S) file, extract archives when needed, and import the files into the library.",
            "acquisition.workflow.directDownload.description"),
        new(Magnet,
        [
            // Preserve the old manual workflow: suspended runs retain their cursor in this chain.
            Step(AcquisitionStepKinds.WaitForInbox),
            Step(AcquisitionStepKinds.Place),
            Step(AcquisitionStepKinds.Materialize)
        ], "Download the magnet with your own client, then provide the completed files for import.",
            "acquisition.workflow.manualMagnet.description"),
        new(MagnetDownload,
        [
            Step(AcquisitionStepKinds.FetchMagnet),
            Step(AcquisitionStepKinds.Place),
            Step(AcquisitionStepKinds.Materialize)
        ], "Download a magnet using the built-in BitTorrent engine, preserve its folders, and import the files.",
            "acquisition.workflow.magnetDownload.description"),
        new(TorrentDownload,
        [
            Step(AcquisitionStepKinds.FetchTorrent),
            Step(AcquisitionStepKinds.Place),
            Step(AcquisitionStepKinds.Materialize)
        ], "Download the files described by a torrent URL or uploaded torrent using the built-in BitTorrent engine.",
            "acquisition.workflow.torrentDownload.description"),
        new(PlatformFetch,
        [
            Step(AcquisitionStepKinds.FetchFromPlatform),
            Step(AcquisitionStepKinds.Materialize)
        ], "Use a linked platform account to obtain files and associate the platform's existing directory with this resource.",
            "acquisition.workflow.platform.description"),
        new(LocalDirectory,
        [
            Step(AcquisitionStepKinds.PickLocalDirectory),
            Step(AcquisitionStepKinds.Place),
            Step(AcquisitionStepKinds.Materialize)
        ], "Choose a server-accessible folder, organize it in the library, and associate it with the resource.",
            "acquisition.workflow.localDirectory.description")
    ];

    /// <summary>
    /// Which recipe a lead runs by default. The lead says how the resource can be obtained, so it
    /// is the only thing that needs consulting to start; the user can still choose another.
    /// </summary>
    public static string DefaultRecipeNameFor(AcquisitionLeadKind kind) => kind switch
    {
        AcquisitionLeadKind.PlatformHolding => PlatformFetch,
        AcquisitionLeadKind.SharedPage => ForumPostWithCloudDrive,
        AcquisitionLeadKind.SharedDocument => ForumPostWithCloudDrive,
        AcquisitionLeadKind.DirectUrl => DirectDownload,
        AcquisitionLeadKind.Magnet => MagnetDownload,
        AcquisitionLeadKind.Torrent => TorrentDownload,
        AcquisitionLeadKind.Manual => LocalDirectory,
        _ => LocalDirectory
    };

    /// <summary>The same effective default for the overview and for actually starting a task.</summary>
    public static string DefaultRecipeNameFor(AcquisitionLeadKind kind, AcquisitionOptions options) =>
        options.RecipeByLeadKind.GetValueOrDefault(kind) ?? DefaultRecipeNameFor(kind);

    public static AcquisitionRecipe? ByName(string name) =>
        All.FirstOrDefault(r => string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase));
}
