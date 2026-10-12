using System.Net;
using System.Text.Json;
using Bakabase.Modules.Player.Abstractions.Models.Domain;
using Bakabase.Modules.Player.Abstractions.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Bakabase.Remoting.Components.UserMachine;

/// <summary>The same device-local settings as the desktop's own Service. These
/// routes are intercepted before any signed request could reach the managed server.</summary>
public sealed class GetDevicePlaybackPreferencesHandler(IDevicePlaybackPreferencesStore store,
    ILogger<GetDevicePlaybackPreferencesHandler> logger) : IUserMachineHandler
{
    public string RouteKey => "GET /player/device-playback/preferences";

    public async Task HandleAsync(HttpContext context, IReadOnlyDictionary<string, string> routeValues)
    {
        try
        {
            var preferences = await store.ReadAsync(context.RequestAborted);
            context.Response.ContentType = "application/json";
            context.Response.Headers.CacheControl = "no-store";
            await context.Response.WriteAsync(JsonSerializer.Serialize(new { code = 0, data = preferences },
                JsonSerializerOptions.Web), context.RequestAborted);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested) { }
        catch (Exception e)
        {
            logger.LogError(e, "Could not read device playback preferences");
            await UserMachineResponse.WriteAsync(context, HttpStatusCode.InternalServerError,
                "Device playback preferences could not be read.");
        }
    }
}

public sealed class SetDevicePlaybackPreferencesHandler(IDevicePlaybackPreferencesStore store,
    ILogger<SetDevicePlaybackPreferencesHandler> logger) : IUserMachineHandler
{
    private const int MaximumPayloadBytes = 16 * 1024;

    public string RouteKey => "PUT /player/device-playback/preferences";

    public async Task HandleAsync(HttpContext context, IReadOnlyDictionary<string, string> routeValues)
    {
        try
        {
            if (context.Request.ContentLength > MaximumPayloadBytes)
            {
                await TooLargeAsync(context);
                return;
            }

            // Also bound chunked requests: this JSON only holds five small choices.
            var buffer = new byte[MaximumPayloadBytes + 1];
            var size = 0;
            while (size < buffer.Length)
            {
                var read = await context.Request.Body.ReadAsync(buffer.AsMemory(size), context.RequestAborted);
                if (read == 0) break;
                size += read;
            }

            if (size > MaximumPayloadBytes)
            {
                await TooLargeAsync(context);
                return;
            }

            var preferences = JsonSerializer.Deserialize<DevicePlaybackPreferences>(buffer.AsSpan(0, size),
                JsonSerializerOptions.Web);
            ArgumentNullException.ThrowIfNull(preferences);
            preferences.Validate();
            await store.WriteAsync(preferences, context.RequestAborted);
            await UserMachineResponse.WriteAsync(context, HttpStatusCode.OK, null);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested) { }
        catch (Exception e) when (e is JsonException or ArgumentException)
        {
            await UserMachineResponse.WriteAsync(context, HttpStatusCode.BadRequest,
                "Choose valid device playback preferences.");
        }
        catch (Exception e)
        {
            logger.LogError(e, "Could not save device playback preferences");
            await UserMachineResponse.WriteAsync(context, HttpStatusCode.InternalServerError,
                "Device playback preferences could not be saved.");
        }
    }

    private static Task TooLargeAsync(HttpContext context) =>
        UserMachineResponse.WriteAsync(context, HttpStatusCode.RequestEntityTooLarge,
            "Device playback preferences are too large.");
}
