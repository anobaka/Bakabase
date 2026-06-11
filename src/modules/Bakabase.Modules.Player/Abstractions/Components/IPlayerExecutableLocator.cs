using Bakabase.Modules.Player.Abstractions.Models.Domain;

namespace Bakabase.Modules.Player.Abstractions.Components;

/// <summary>
/// Probes the local machine for the executables of a known player
/// (registry hints, well-known directories, PATH).
/// </summary>
public interface IPlayerExecutableLocator
{
    /// <summary>
    /// Returns existing executable paths for the definition, best match first.
    /// </summary>
    IReadOnlyList<string> Locate(KnownPlayerDefinition definition);
}
