using Bakabase.Modules.Player.Abstractions.Models.Domain.Constants;

namespace Bakabase.Modules.Player.Abstractions.Models.Domain;

/// <summary>
/// A player the user can pick to open a multi-resource selection with.
/// </summary>
public record BatchPlayCandidate
{
    /// <summary>
    /// Opaque identifier the frontend sends back to
    /// <see cref="Services.IBatchPlayService.PlayAsync"/>. Stable across
    /// requests for the same player.
    /// </summary>
    public required string Key { get; init; }

    public BatchPlayCandidateType Type { get; init; }
    public required string DisplayName { get; init; }
    public required string ExecutablePath { get; init; }
    public BatchPlayCapability Capabilities { get; init; }

    /// <summary>
    /// Launch command template of a profile-configured player ("{0}" is the
    /// file placeholder). Null means a bare "{0}".
    /// </summary>
    public string? CommandTemplate { get; init; }

    /// <summary>
    /// True when the player is not in the known catalog and its playlist
    /// support is assumed rather than verified.
    /// </summary>
    public bool CapabilitiesAssumed { get; init; }

    /// <summary>
    /// File extensions (with leading dot) this player handles: a profile
    /// player's configured extensions, or the known-player catalog's set.
    /// Null means the player takes any file.
    /// </summary>
    public IReadOnlySet<string>? SupportedExtensions { get; init; }

    /// <summary>
    /// Resource-selection flow: how many of the selected resources have at
    /// least one playable file this player can open. Null in flows where it
    /// is not computed.
    /// </summary>
    public int? MatchedResourceCount { get; init; }

    /// <summary>
    /// Playlist flow: how many of the playlist's files this player can open.
    /// Null in flows where it is not computed.
    /// </summary>
    public int? MatchedFileCount { get; init; }
}
