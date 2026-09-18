using System.Net;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Client.Remoting.Components.Connection;
using Bakabase.Client.Remoting.Components.Paths;
using Bakabase.Modules.Player.Components;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Bakabase.Client.Remoting.Components.UserMachine;

/// <summary>
/// Starting a player on this machine, whichever route asked for it.
/// </summary>
/// <remarks>
/// <para>
/// Three routes play something — one named item, a whole resource, a random one — and
/// they differ only in how they work out what to play. Everything after that is the same
/// decision, and it is the part with the sharp edges: which player, a real path or a
/// stream, and never running an executable this machine did not recognise.
/// </para>
/// <para>
/// The interesting case is a local file, and it has two ways to reach a player. If the
/// user has mapped that library, the player gets the real path and reads from disk —
/// fast, seekable, no server involved once it starts. If they have not, the player gets a
/// URL through this client's own forwarding layer instead, which signs and relays it.
/// </para>
/// <para>
/// That fallback is the difference between "set up a path mapping first" and "it plays".
/// Streaming is worse than local disk, so mapping is still worth doing and the settings
/// page says so — but nothing has to be configured before the first video plays, and a
/// library that genuinely is not on this machine works at all.
/// </para>
/// <para>
/// The one thing the stream cannot be handed to is the OS default. A path and a URL look
/// alike to <c>ShellExecute</c> and are not: the default program for <c>http</c> is a
/// browser, and a browser answers a video stream by offering to download it. So a
/// streamed target insists on a program this client knows to be a player, and says why
/// when there is none — a download dialog reported as success is the worst of both.
/// </para>
/// </remarks>
public sealed class LocalPlayback(
    ActiveConnection connection,
    IUpstreamApi upstream,
    LocalPlayerResolver players,
    IShellOpener shell,
    ILoopbackAddressProvider loopback,
    ILogger<LocalPlayback> logger)
{
    /// <summary>
    /// Plays one item and tells the server it happened.
    /// </summary>
    /// <remarks>
    /// Writes its own response either way, so a caller that gets false has nothing left
    /// to do — the reason it failed is already on the wire and only this method knows it.
    /// </remarks>
    public async Task<bool> PlayAsync(HttpContext context, int resourceId, DataOrigin origin, string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            await UserMachineResponse.WriteAsync(context, HttpStatusCode.BadRequest, "No item was given to play.");
            return false;
        }

        var played = origin switch
        {
            DataOrigin.FileSystem or DataOrigin.Manual => await PlayFileAsync(context, resourceId, key),
            DataOrigin.Steam => await LaunchUriAsync(context, SteamUri(key)),
            DataOrigin.DLsite => await LaunchUriAsync(context,
                $"https://www.dlsite.com/maniax/work/=/product_id/{Uri.EscapeDataString(key)}.html"),
            DataOrigin.ExHentai => await LaunchUriAsync(context, $"https://exhentai.org/g/{key.TrimStart('/')}"),
            _ => await UnsupportedAsync(context, origin)
        };

        if (!played)
        {
            return false;
        }

        // The server owns play history, so it is told after the fact — in the same
        // "{origin}:{key}" shape it writes when it plays something itself, or the two
        // halves would record the same event differently.
        await upstream.MarkPlayedAsync(resourceId, $"{origin}:{key}", context.RequestAborted);

        await UserMachineResponse.WriteAsync(context, HttpStatusCode.OK, null);

        return true;
    }

    private async Task<bool> PlayFileAsync(HttpContext context, int resourceId, string serverPath)
    {
        var mapped = ClientPathMapper.Map(serverPath, connection.Server?.PathMappings ?? []);

        // A mapped file that is actually there beats streaming it back from the server
        // it already lives on. Mapped-but-missing means a stale mount, and streaming is
        // the honest fallback there too.
        var onDisk = mapped.Mapped && File.Exists(mapped.LocalPath);
        var target = onDisk ? mapped.LocalPath! : loopback.BuildRawFileUrl(serverPath);

        var options = await upstream.GetEffectivePlayerOptionsAsync(resourceId, context.RequestAborted);
        var player = players.Resolve(options, serverPath);

        if (!onDisk && player.IsSystemDefault)
        {
            // "Let the OS decide" is the right answer for a path and the wrong one for a
            // URL: what answers for http is a browser, and a browser meets a video stream
            // with a download prompt. That is not playback, and returning OK for it left
            // the user with a download and no explanation.
            var installed = players.ResolveInstalled(serverPath);

            if (installed == null)
            {
                await UserMachineResponse.WriteAsync(context, HttpStatusCode.NotImplemented,
                    $"'{serverPath}' is not on this machine, so it would have to be streamed — and no player " +
                    "this client can recognise is installed here to stream it into. Either map that library " +
                    "to a local path in the client's settings, or install one of " +
                    $"{string.Join(", ", KnownPlayerDefinitions.All.Select(d => d.DisplayName))}.");

                return false;
            }

            player = installed;
        }

        try
        {
            if (player.IsSystemDefault)
            {
                shell.Launch(target);
            }
            else
            {
                shell.LaunchProcess(player.ExecutablePath!,
                    BatchPlayArguments.BuildFromTemplate(player.CommandTemplate, target),
                    player.NeedsShellExecute);
            }

            return true;
        }
        catch (Exception e)
        {
            logger.LogError(e, "Failed to play {Target}", target);
            await UserMachineResponse.WriteAsync(context, HttpStatusCode.InternalServerError,
                $"Could not start a player on your machine: {e.Message}");

            return false;
        }
    }

    private async Task<bool> LaunchUriAsync(HttpContext context, string? uri)
    {
        if (uri == null)
        {
            await UserMachineResponse.WriteAsync(context, HttpStatusCode.BadRequest,
                "That item cannot be opened on your machine.");

            return false;
        }

        try
        {
            shell.Launch(uri);
            return true;
        }
        catch (Exception e)
        {
            logger.LogError(e, "Failed to open {Uri}", uri);
            await UserMachineResponse.WriteAsync(context, HttpStatusCode.InternalServerError,
                $"Could not open it on your machine: {e.Message}");

            return false;
        }
    }

    /// <summary>
    /// Steam app ids are numbers, and this string reaches the OS as a URI to act on.
    /// Anything else is refused rather than handed over — the key came from a server
    /// that is not necessarily the user's.
    /// </summary>
    public static string? SteamUri(string key) =>
        key.Length > 0 && key.All(char.IsAsciiDigit) ? $"steam://rungameid/{key}" : null;

    private static async Task<bool> UnsupportedAsync(HttpContext context, DataOrigin origin)
    {
        await UserMachineResponse.WriteAsync(context, HttpStatusCode.NotImplemented,
            $"This client does not know how to play a {origin} item yet.");

        return false;
    }
}
