namespace Bakabase.Modules.Player.Abstractions.Models.Domain.Constants;

/// <summary>
/// How a player can be asked to open multiple files in one launch.
/// </summary>
[Flags]
public enum BatchPlayCapability
{
    None = 0,

    /// <summary>
    /// The player accepts an .m3u8 playlist file as its file argument.
    /// </summary>
    PlaylistFile = 1 << 0,

    /// <summary>
    /// The player accepts multiple file paths in a single command line.
    /// </summary>
    MultiFileArguments = 1 << 1,
}
