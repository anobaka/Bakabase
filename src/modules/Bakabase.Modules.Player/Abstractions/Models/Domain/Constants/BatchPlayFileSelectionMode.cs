namespace Bakabase.Modules.Player.Abstractions.Models.Domain.Constants;

public enum BatchPlayFileSelectionMode
{
    /// <summary>
    /// Take only the first playable file of each resource. Safe default:
    /// for video/audio resources this is usually the main file, and it
    /// keeps image-heavy resources (e.g. comics) from flooding the playlist.
    /// </summary>
    FirstFilePerResource = 1,

    /// <summary>
    /// Take every playable file of each resource (e.g. albums). Guarded by
    /// <see cref="PlayerModuleOptions.MaxTotalFiles"/>.
    /// </summary>
    AllFiles = 2,
}
