using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Bakabase.Modules.Player.Abstractions.Models.Input;
using Bakabase.Modules.Player.Abstractions.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Bakabase.Remoting.Components.UserMachine;

/// <summary>
/// Shared groundwork for the four batch-play routes.
/// </summary>
/// <remarks>
/// <para>
/// These run the server's own orchestration in this process. That is the whole design:
/// which players are candidates, which files each one gets, whether they go as arguments
/// or as an m3u8 — all of it is one implementation, reading the library through a port
/// the client backs with HTTP instead of a database.
/// </para>
/// <para>
/// So the client cannot drift from the server on any of the decisions that were hard to
/// get right, and the two answers a menu shows differ only where they must: the players
/// installed here, and the files reachable from here.
/// </para>
/// </remarks>
public abstract class BatchPlayHandlerBase(ILogger logger) : IUserMachineHandler
{
    /// <summary>Matches the server's own serializer settings, since these are its models.</summary>
    protected static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters = {new JsonStringEnumConverter()}
    };

    public abstract string RouteKey { get; }

    public abstract Task HandleAsync(HttpContext context, IReadOnlyDictionary<string, string> routeValues);

    /// <summary>
    /// Runs one batch-play call and writes its answer.
    /// </summary>
    /// <remarks>
    /// The orchestration reports user-environment problems — the player is gone, the
    /// files moved, the selection is too large — by throwing
    /// <see cref="InvalidOperationException"/>, and the server's controller turns those
    /// into a bad request rather than a 500. The same handling here, so the frontend sees
    /// one behaviour whichever machine answered.
    /// </remarks>
    protected async Task RunAsync<T>(HttpContext context, Func<IBatchPlayService, Task<T>> work)
    {
        try
        {
            // Resolved per request: the orchestration is scoped, these handlers are not.
            var batchPlay = context.RequestServices.GetRequiredService<IBatchPlayService>();

            await WriteDataAsync(context, HttpStatusCode.OK, await work(batchPlay));
        }
        catch (InvalidOperationException e)
        {
            await WriteDataAsync<object?>(context, HttpStatusCode.BadRequest, null, e.Message);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            // The window went away mid-play; nothing to report to nobody.
        }
        catch (Exception e)
        {
            logger.LogError(e, "Batch play failed on this machine");
            await WriteDataAsync<object?>(context, HttpStatusCode.InternalServerError, null, e.Message);
        }
    }

    private static async Task WriteDataAsync<T>(HttpContext context, HttpStatusCode status, T data,
        string? message = null)
    {
        context.Response.StatusCode = (int) status;
        context.Response.ContentType = "application/json";

        await context.Response.WriteAsync(JsonSerializer.Serialize(new
        {
            code = status == HttpStatusCode.OK ? 0 : (int) status,
            message,
            data
        }, Json), context.RequestAborted);
    }

    protected static async Task<T?> ReadBodyAsync<T>(HttpContext context) where T : class =>
        await JsonSerializer.DeserializeAsync<T>(context.Request.Body, Json, context.RequestAborted);

    protected static async Task BadRequestAsync(HttpContext context, string message) =>
        await UserMachineResponse.WriteAsync(context, HttpStatusCode.BadRequest, message);
}

/// <summary>Which players on this machine could open the selection.</summary>
public sealed class BatchPlayCandidatesHandler(ILogger<BatchPlayCandidatesHandler> logger)
    : BatchPlayHandlerBase(logger)
{
    public override string RouteKey => "POST /player/batch-play/candidates";

    public override async Task HandleAsync(HttpContext context, IReadOnlyDictionary<string, string> routeValues)
    {
        var model = await ReadBodyAsync<BatchPlayCandidatesInputModel>(context);

        if (model == null)
        {
            await BadRequestAsync(context, "No resources were given.");
            return;
        }

        await RunAsync(context, p => p.GetCandidatesAsync(model.ResourceIds, context.RequestAborted));
    }
}

/// <summary>Opens the selection in a player on this machine.</summary>
public sealed class BatchPlayResourcesHandler(ILogger<BatchPlayResourcesHandler> logger)
    : BatchPlayHandlerBase(logger)
{
    public override string RouteKey => "POST /player/batch-play";

    public override async Task HandleAsync(HttpContext context, IReadOnlyDictionary<string, string> routeValues)
    {
        var model = await ReadBodyAsync<BatchPlayInputModel>(context);

        if (model == null)
        {
            await BadRequestAsync(context, "No resources were given.");
            return;
        }

        await RunAsync(context, p => p.PlayAsync(model, context.RequestAborted));
    }
}

/// <summary>Which players on this machine could open a playlist.</summary>
public sealed class PlaylistBatchPlayCandidatesHandler(ILogger<PlaylistBatchPlayCandidatesHandler> logger)
    : BatchPlayHandlerBase(logger)
{
    public override string RouteKey => "GET /player/playlist/{playlistId:int}/batch-play/candidates";

    public override async Task HandleAsync(HttpContext context, IReadOnlyDictionary<string, string> routeValues)
    {
        if (!TryReadPlaylistId(routeValues, out var playlistId))
        {
            await BadRequestAsync(context, "No playlist id was given.");
            return;
        }

        await RunAsync(context, p => p.GetPlaylistCandidatesAsync(playlistId, context.RequestAborted));
    }

    internal static bool TryReadPlaylistId(IReadOnlyDictionary<string, string> routeValues, out int playlistId)
    {
        playlistId = 0;

        return routeValues.TryGetValue("playlistId", out var text) && int.TryParse(text, out playlistId);
    }
}

/// <summary>Opens a playlist in a player on this machine.</summary>
public sealed class PlaylistBatchPlayHandler(ILogger<PlaylistBatchPlayHandler> logger)
    : BatchPlayHandlerBase(logger)
{
    public override string RouteKey => "POST /player/playlist/{playlistId:int}/batch-play";

    public override async Task HandleAsync(HttpContext context, IReadOnlyDictionary<string, string> routeValues)
    {
        if (!PlaylistBatchPlayCandidatesHandler.TryReadPlaylistId(routeValues, out var playlistId))
        {
            await BadRequestAsync(context, "No playlist id was given.");
            return;
        }

        var model = await ReadBodyAsync<PlaylistBatchPlayInputModel>(context);

        if (model == null)
        {
            await BadRequestAsync(context, "No player was chosen.");
            return;
        }

        await RunAsync(context,
            p => p.PlayPlaylistAsync(playlistId, model.PlayerKey, context.RequestAborted));
    }
}
