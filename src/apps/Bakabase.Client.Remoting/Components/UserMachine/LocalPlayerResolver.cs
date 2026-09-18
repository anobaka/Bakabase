using Bakabase.Abstractions.Models.Domain;
using Bakabase.Modules.Player.Abstractions.Components;
using Bakabase.Modules.Player.Components;

namespace Bakabase.Client.Remoting.Components.UserMachine;

/// <param name="ExecutablePath">Null means "let the OS decide".</param>
/// <param name="CommandTemplate">The user's argument template, e.g. <c>"{0}" --fullscreen</c>.</param>
public sealed record ResolvedPlayer(string? ExecutablePath, string? CommandTemplate)
{
    public static readonly ResolvedPlayer SystemDefault = new(null, null);

    public bool IsSystemDefault => ExecutablePath == null;

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
    public ResolvedPlayer Resolve(ResourceProfilePlayerOptions? options, string fileNameOrPath)
    {
        if (options?.Players is not {Count: > 0} players)
        {
            return ResolvedPlayer.SystemDefault;
        }

        var extension = Path.GetExtension(fileNameOrPath);

        var chosen =
            players.FirstOrDefault(p =>
                p.Extensions?.Contains(extension, StringComparer.OrdinalIgnoreCase) == true) ??
            players.FirstOrDefault(p => p.Extensions?.Any() != true);

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
            return ResolvedPlayer.SystemDefault;
        }

        var here = locator.Locate(known).FirstOrDefault();

        return here == null ? ResolvedPlayer.SystemDefault : new ResolvedPlayer(here, chosen.Command);
    }

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
    public ResolvedPlayer? ResolveInstalled(string fileNameOrPath)
    {
        var extension = Path.GetExtension(fileNameOrPath);

        foreach (var definition in KnownPlayerDefinitions.All)
        {
            // A null set means the player claims everything; foobar2000 claims audio
            // only, and starting it on a video is not an improvement on failing.
            if (definition.SupportedExtensions?.Contains(extension) == false)
            {
                continue;
            }

            var here = locator.Locate(definition).FirstOrDefault();

            if (here != null)
            {
                return new ResolvedPlayer(here, null);
            }
        }

        return null;
    }
}
