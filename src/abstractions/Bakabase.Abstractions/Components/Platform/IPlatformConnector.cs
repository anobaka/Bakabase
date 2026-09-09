using Bakabase.Abstractions.Models.Domain.Constants;

namespace Bakabase.Abstractions.Components.Platform;

/// <summary>One thing a user holds on a platform, as the platform describes it.</summary>
/// <param name="SourceKey">The platform's own id for it — an RJ number, an AppId, a gallery id.</param>
/// <param name="DisplayName">What the platform calls it.</param>
/// <param name="LocalPath">Where it already is on this machine, when the platform knows.</param>
/// <param name="CoverUrls">Cover images, best first.</param>
/// <param name="MetadataJson">The platform's own fields, verbatim, for a typed reader to make sense of later.</param>
public record PlatformHolding(
    string SourceKey,
    string DisplayName,
    string? LocalPath = null,
    List<string>? CoverUrls = null,
    string? MetadataJson = null);

/// <summary>How far a fetch got before it had to hand back control.</summary>
public abstract record PlatformFetchOutcome
{
    private PlatformFetchOutcome()
    {
    }

    /// <summary>The files are here, in this directory.</summary>
    public sealed record Done(string Directory) : PlatformFetchOutcome;

    /// <summary>
    /// The platform is working on it — a download queued, an installer launched — and will say so
    /// by having the files somewhere later. The caller waits and asks again.
    /// </summary>
    /// <param name="Note">What the user is waiting for, in words, for the interface to show.</param>
    public sealed record Started(string? Note = null) : PlatformFetchOutcome;

    /// <summary>It cannot be fetched, and saying why is more use than a silent failure.</summary>
    public sealed record Refused(string Why) : PlatformFetchOutcome;
}

/// <summary>
/// A platform the user holds resources on.
/// <para>
/// Steam, DLsite and ExHentai are all the same shape underneath: an account that owns things, a
/// way of pulling one down, and a way of telling whether it is already here. They each grew their
/// own table, service, parser and page; this is the contract that says what they have in common,
/// so a resource held on a platform is an ordinary resource that happens to know where it lives.
/// </para>
/// </summary>
public interface IPlatformConnector
{
    ResourceSource Source { get; }

    /// <summary>Everything the user holds on this platform.</summary>
    Task<IReadOnlyList<PlatformHolding>> EnumerateHoldingsAsync(CancellationToken ct);

    /// <summary>
    /// Whether the platform will hand the files over at all. A catalog knows what exists but has
    /// nothing to give; a shop the user has bought from does.
    /// </summary>
    bool CanFetch { get; }

    /// <summary>
    /// Brings one holding down.
    /// </summary>
    /// <param name="workDirectory">
    /// Where the acquisition would like it. A platform that insists on its own location says so by
    /// returning that directory instead.
    /// </param>
    Task<PlatformFetchOutcome> FetchAsync(string sourceKey, string workDirectory,
        Func<int, string?, Task>? onProgress, CancellationToken ct);

    /// <summary>
    /// Where this holding already is on the machine, if it is. Steam knows because it installed it;
    /// DLsite knows because it downloaded it. Null means not here.
    /// </summary>
    Task<string?> DetectLocalPathAsync(string sourceKey, CancellationToken ct);
}

/// <summary>
/// Finds the connector for a platform, without building the others.
/// <para>
/// Each connector wraps a client of its own — an HTTP session, a download queue — and asking for
/// "all of them" to use one would construct all of that every time a step ran.
/// </para>
/// </summary>
public interface IPlatformConnectorRegistry
{
    /// <summary>The platforms this build can speak for.</summary>
    IReadOnlyCollection<ResourceSource> Sources { get; }

    /// <summary>The connector for a platform, or null when this build has none.</summary>
    IPlatformConnector? Get(ResourceSource source);
}
