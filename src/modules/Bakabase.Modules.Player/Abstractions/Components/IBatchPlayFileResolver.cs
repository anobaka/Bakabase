namespace Bakabase.Modules.Player.Abstractions.Components;

/// <summary>
/// Turns a path as the library names it into something a player on this machine can open.
/// </summary>
/// <remarks>
/// <para>
/// A seam for the same reason as <see cref="IBatchPlayResourceSource"/>: the machine
/// holding the files is not always the one starting the player. On the all-in-one the two
/// are the same and this is a file-existence check. In a relay for a managed server the path
/// belongs to another filesystem and has to be translated to a local mount — or, when there
/// is no mount for it, replaced by a URL back through the relay.
/// </para>
/// <para>
/// It also owns the separator conversion, which used to happen just before launch. A
/// resolved path is not necessarily a path any more, and rewriting the slashes in a URL
/// would break it.
/// </para>
/// </remarks>
public interface IBatchPlayFileResolver
{
    /// <summary>Null when nothing on this machine could open it.</summary>
    string? Resolve(string path);
}
