using Bakabase.Abstractions.Models.Domain;
using Bakabase.Modules.Player.Abstractions.Components;
using Bakabase.Modules.Player.Abstractions.Models.Domain;

namespace Bakabase.Modules.Player.Components;

/// <param name="ExecutablePath">Null means "let the OS decide".</param>
/// <param name="CommandTemplate">The user's argument template, e.g. <c>"{0}" --fullscreen</c>.</param>
public sealed record ResolvedPlayer(string? ExecutablePath, string? CommandTemplate)
{
    public static readonly ResolvedPlayer SystemDefault = new(null, null);

    public bool IsSystemDefault => ExecutablePath == null;
    public string? DefinitionId { get; init; }
    public bool SupportsHttp { get; init; }
    public IReadOnlySet<string>? SupportedExtensions { get; init; }

    /// <summary>
    /// Windows shortcuts only start through the shell; the OS refuses them otherwise
    /// with "not a valid Win32 application".
    /// </summary>
    public bool NeedsShellExecute =>
        ExecutablePath != null &&
        Path.GetExtension(ExecutablePath).Equals(".lnk", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Works out which program should open a file on <em>this</em> machine, from a choice
/// the user made on another one.
/// </summary>
/// <remarks>
/// <para>
/// The player configuration lives in the server's database and its executable paths
/// point at whatever filesystem configured them. A Windows desktop and a macOS laptop
/// sharing one server cannot both be right, and running the other's path would fail —
/// or, worse, start whatever happens to sit there.
/// </para>
/// <para>
/// So the stored path is treated as a statement of <em>which player</em>, not of where
/// it is: it is matched against the known-player catalog and re-resolved against this
/// machine's own installations. A player nobody recognises, or one that is not installed
/// here, falls back to the OS default — which plays the file, just not with the program
/// the user picked, and that beats an error.
/// </para>
/// </remarks>
public sealed class LocalPlayerResolver(IPlayerExecutableLocator locator)
{
    /// <summary>
    /// Picks the player for one file, mirroring the server's own rule: the first whose
    /// extensions include this one, else the first that claims no extensions at all.
    /// </summary>
    public ResolvedPlayer Resolve(ResourceProfilePlayerOptions? options, string fileNameOrPath,
        bool requireHttp = false, bool explicitChoice = false)
    {
        var chosen = PlayerFileCapabilities.SelectConfigured(options, fileNameOrPath);

        if (chosen == null)
        {
            return ResolvedPlayer.SystemDefault;
        }

        var known = KnownPlayerDefinitions.MatchByExecutable(chosen.ExecutablePath);

        if (known == null)
        {
            // Not a player we know how to find. The stored path belongs to another
            // machine, so trying it is at best a failure and at worst a launch of
            // something unrelated that happens to live there.
            if (explicitChoice)
                throw new InvalidOperationException("The configured player cannot be identified on this device. Choose an installed local player or the system default application.");
            return ResolvedPlayer.SystemDefault;
        }

        if (!PlayerFileCapabilities.SupportsFile(known, fileNameOrPath))
        {
            if (explicitChoice) throw UnsupportedFile(known, fileNameOrPath);
            return ResolvedPlayer.SystemDefault;
        }

        var supportsHttp = PlayerFileCapabilities.SupportsHttp(chosen, known);
        if (requireHttp && !supportsHttp)
            throw new InvalidOperationException($"{known.DisplayName} is configured not to open HTTP media. Map the file to an accessible local folder, or choose a player that supports HTTP media.");

        var here = locator.Locate(known).FirstOrDefault();

        if (here == null)
        {
            if (explicitChoice) throw NotInstalled(known);
            return ResolvedPlayer.SystemDefault;
        }
        return ResolveAt(known, here, chosen.Command) with {SupportsHttp = supportsHttp};
    }

    /// <summary>Resolves an explicit device-local choice. A failed choice is never replaced silently.</summary>
    public ResolvedPlayer ResolveKnown(string playerId, string fileNameOrPath, bool requireHttp = false)
    {
        var known = KnownPlayerDefinitions.All.FirstOrDefault(d => d.Id == playerId) ??
                    throw new InvalidOperationException("The selected player is not available. Choose another local player.");
        if (!PlayerFileCapabilities.SupportsFile(known, fileNameOrPath)) throw UnsupportedFile(known, fileNameOrPath);
        if (requireHttp && (!known.SupportsHttp || !PlayerFileCapabilities.CanStream(fileNameOrPath)))
            throw new InvalidOperationException($"{known.DisplayName} cannot open this file through HTTP. Map an accessible local folder first.");
        var here = locator.Locate(known).FirstOrDefault() ?? throw NotInstalled(known);
        return ResolveAt(known, here, null);
    }

    private static InvalidOperationException NotInstalled(KnownPlayerDefinition known) =>
        new($"{known.DisplayName} is not installed on this device. Install it or choose another player.");

    private static InvalidOperationException UnsupportedFile(KnownPlayerDefinition known, string path) =>
        new($"{known.DisplayName} does not support '{PlayerFileCapabilities.Extension(path)}' files. Choose another application or the system default.");

    private static ResolvedPlayer ResolveAt(KnownPlayerDefinition known, string executable, string? template) =>
        new(executable, AddRequiredArguments(known, template))
        {
            DefinitionId = known.Id, SupportsHttp = known.SupportsHttp,
            SupportedExtensions = known.SupportedExtensions
        };

    private static string? AddRequiredArguments(Abstractions.Models.Domain.KnownPlayerDefinition definition, string? template) =>
        definition.ArgumentPrefix == null ? template : $"{definition.ArgumentPrefix} {template ?? "{0}"}";

    /// <summary>
    /// Any player installed on this machine that can open this kind of file, best match
    /// first, or null when there is none.
    /// </summary>
    /// <remarks>
    /// For when the OS default will not do. Handing a stream URL to the shell starts
    /// whatever answers for <c>http</c>, which is a browser, and a browser answers a
    /// video stream with a download prompt — so a target that is a URL needs a program
    /// known to be a player, not whatever the OS would pick.
    /// </remarks>
    public ResolvedPlayer? ResolveInstalled(string fileNameOrPath,
        Func<Abstractions.Models.Domain.KnownPlayerDefinition, bool>? isAllowed = null, bool requireHttp = false)
    {
        if (requireHttp && !PlayerFileCapabilities.CanStream(fileNameOrPath)) return null;

        foreach (var definition in KnownPlayerDefinitions.All)
        {
            if (isAllowed?.Invoke(definition) == false || (requireHttp && !definition.SupportsHttp))
            {
                continue;
            }

            // A null set means the player claims everything; foobar2000 claims audio
            // only, and starting it on a video is not an improvement on failing.
            if (!PlayerFileCapabilities.SupportsFile(definition, fileNameOrPath))
            {
                continue;
            }

            var here = locator.Locate(definition).FirstOrDefault();

            if (here != null)
            {
                return ResolveAt(definition, here, null);
            }
        }

        return null;
    }
}
