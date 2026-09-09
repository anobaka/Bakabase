using Bakabase.Modules.Acquisition.Abstractions.Models.Domain;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain.Constants;

namespace Bakabase.Modules.Acquisition.Components;

/// <summary>
/// The recipes that ship with Bakabase. They are seeds: created once by name, marked built-in, and
/// meant to be copied and changed rather than edited in place — the same arrangement as the
/// built-in text vocabularies.
/// <para>
/// The five of them differ only in how the files are obtained. Everything after that — unpack,
/// place, materialize — is the same work, which is the point of making a recipe data.
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
        ]),
        new(DirectDownload,
        [
            Step(AcquisitionStepKinds.FetchHttp),
            Step(AcquisitionStepKinds.Unpack),
            Step(AcquisitionStepKinds.Place),
            Step(AcquisitionStepKinds.Materialize)
        ]),
        new(Magnet,
        [
            // No fetch step yet: the user's own torrent client puts the files in the inbox. An
            // external-downloader step slots in here later without the rest changing.
            Step(AcquisitionStepKinds.WaitForInbox),
            Step(AcquisitionStepKinds.Place),
            Step(AcquisitionStepKinds.Materialize)
        ]),
        new(PlatformFetch,
        [
            Step(AcquisitionStepKinds.FetchFromPlatform),
            Step(AcquisitionStepKinds.Materialize)
        ]),
        new(LocalDirectory,
        [
            Step(AcquisitionStepKinds.PickLocalDirectory),
            Step(AcquisitionStepKinds.Place),
            Step(AcquisitionStepKinds.Materialize)
        ])
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
        AcquisitionLeadKind.Magnet => Magnet,
        AcquisitionLeadKind.Manual => LocalDirectory,
        _ => LocalDirectory
    };

    public static AcquisitionRecipe? ByName(string name) =>
        All.FirstOrDefault(r => string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase));
}
