using System.Net;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Remoting.Components.Connection;
using Microsoft.AspNetCore.Http;

namespace Bakabase.Remoting.Components.UserMachine;

/// <summary>
/// Plays one named item on this machine.
/// </summary>
public sealed class PlayItemHandler(LocalPlayback playback) : IUserMachineHandler
{
    public string RouteKey => "GET /resource/{resourceId}/play-item";

    public async Task HandleAsync(HttpContext context, IReadOnlyDictionary<string, string> routeValues)
    {
        if (!routeValues.TryGetValue("resourceId", out var idText) || !int.TryParse(idText, out var resourceId))
        {
            await UserMachineResponse.WriteAsync(context, HttpStatusCode.BadRequest, "No resource id was given.");
            return;
        }

        if (!Enum.TryParse<DataOrigin>(context.Request.Query["origin"].ToString(), true, out var origin))
        {
            await UserMachineResponse.WriteAsync(context, HttpStatusCode.BadRequest, "No item was given to play.");
            return;
        }

        await playback.PlayAsync(context, resourceId, origin, context.Request.Query["key"].ToString());
    }
}

/// <summary>
/// Plays a whole resource on this machine — its first playable file, or the one named.
/// </summary>
/// <remarks>
/// Which file that is, is the server's answer: it holds the library, the profile rules
/// that decide what counts as playable, and the cache. So the client asks rather than
/// deciding from a path mapping, which would only see whichever libraries happen to be
/// mounted here.
/// </remarks>
public sealed class PlayResourceHandler(IUpstreamApi upstream, LocalPlayback playback) : IUserMachineHandler
{
    public string RouteKey => "GET /resource/{resourceId}/play";

    public async Task HandleAsync(HttpContext context, IReadOnlyDictionary<string, string> routeValues)
    {
        if (!routeValues.TryGetValue("resourceId", out var idText) || !int.TryParse(idText, out var resourceId))
        {
            await UserMachineResponse.WriteAsync(context, HttpStatusCode.BadRequest, "No resource id was given.");
            return;
        }

        var file = context.Request.Query["file"].ToString();

        if (!string.IsNullOrWhiteSpace(file))
        {
            await playback.PlayAsync(context, resourceId, DataOrigin.FileSystem, file);
            return;
        }

        var items = await upstream.GetPlayableItemsAsync(resourceId, context.RequestAborted);

        if (items == null)
        {
            await UserMachineResponse.WriteAsync(context, HttpStatusCode.ServiceUnavailable,
                $"Could not ask the server what is playable in resource {resourceId}. " +
                "Check that it is still reachable.");

            return;
        }

        // The server's own /play picks a local file specifically, not merely the first
        // item of any origin — opening a store page when the user asked to play a folder
        // would be a different action wearing the same name.
        var item = items.FirstOrDefault(i => i.Origin == DataOrigin.FileSystem);

        if (item == null)
        {
            await UserMachineResponse.WriteAsync(context, HttpStatusCode.BadRequest,
                "No playable file was found.");

            return;
        }

        await playback.PlayAsync(context, resourceId, item.Origin, item.Key);
    }
}

/// <summary>
/// Plays something at random on this machine.
/// </summary>
/// <remarks>
/// The picking stays on the server, where the resources, the playable-file cache and the
/// live fallback probe all are. This asks it for a pick and then does what it always
/// does with one — which keeps a single definition of "random" for both flavours rather
/// than a second, weaker one that could only draw from mapped libraries.
/// </remarks>
public sealed class PlayRandomResourceHandler(IUpstreamApi upstream, LocalPlayback playback) : IUserMachineHandler
{
    public string RouteKey => "GET /resource/play/random";

    public async Task HandleAsync(HttpContext context, IReadOnlyDictionary<string, string> routeValues)
    {
        var pick = await upstream.PickRandomPlayableItemAsync(context.RequestAborted);

        if (pick == null)
        {
            await UserMachineResponse.WriteAsync(context, HttpStatusCode.ServiceUnavailable,
                "Could not ask the server for something to play. Check that it is still reachable.");

            return;
        }

        // Asked and answered "nothing" is a different thing from not being able to ask,
        // and the user can act on only one of them.
        if (pick.Item == null)
        {
            await UserMachineResponse.WriteAsync(context, HttpStatusCode.BadRequest,
                "No playable resource was found.");

            return;
        }

        await playback.PlayAsync(context, pick.Item.ResourceId, pick.Item.Origin, pick.Item.Key);
    }
}
