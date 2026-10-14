using System.Net;
using System.Text.Json;
using Bakabase.Modules.Player.Abstractions.Components;
using Bakabase.Modules.Player.Abstractions.Models.Domain;
using Bakabase.Modules.Player.Components;
using Bakabase.Modules.Player.Services;
using Bakabase.Remoting.Components.Connection;
using Microsoft.AspNetCore.Http;

namespace Bakabase.Remoting.Components.UserMachine;

public sealed class GetDevicePlaybackOverridesHandler(DevicePlaybackOverridesStore store,
    IPlayerExecutableLocator locator, ActiveConnection connection) : IUserMachineHandler
{
    public string RouteKey => "GET /player/device-playback/overrides";
    public async Task HandleAsync(HttpContext context, IReadOnlyDictionary<string, string> routeValues)
    {
        var target = context.Request.Query["target"].ToString();
        if (connection.Server == null || target != "server:" + connection.Server.ServerId)
        {
            await UserMachineResponse.WriteAsync(context, HttpStatusCode.Forbidden,
                "This window may only read opening rules for the server it is showing.");
            return;
        }
        context.Response.Headers.CacheControl = "no-store";
        await context.Response.WriteAsJsonAsync(new { code = 0,
            data = await DevicePlaybackOverrides.SnapshotAsync(store, locator, target, context.RequestAborted) },
            context.RequestAborted);
    }
}

public sealed class SetDevicePlaybackOverridesHandler(DevicePlaybackOverridesStore store,
    ActiveConnection connection) : IUserMachineHandler
{
    public string RouteKey => "PUT /player/device-playback/overrides";
    public async Task HandleAsync(HttpContext context, IReadOnlyDictionary<string, string> routeValues)
    {
        try
        {
            var bytes = new byte[16 * 1024 + 1];
            var size = 0;
            while (size < bytes.Length)
            {
                var count = await context.Request.Body.ReadAsync(bytes.AsMemory(size), context.RequestAborted);
                if (count == 0) break;
                size += count;
            }
            if (size == bytes.Length)
            {
                await UserMachineResponse.WriteAsync(context, HttpStatusCode.RequestEntityTooLarge, "Opening rules are too large.");
                return;
            }
            var rule = JsonSerializer.Deserialize<DevicePlaybackOverride>(bytes.AsSpan(0, size), JsonSerializerOptions.Web);
            if (rule == null) throw new ArgumentException("Choose valid opening rules.");
            rule.Validate();
            if (connection.Server == null || rule.Target != "server:" + connection.Server.ServerId)
            {
                await UserMachineResponse.WriteAsync(context, HttpStatusCode.Forbidden,
                    "This window may only change opening rules for the server it is showing.");
                return;
            }
            await store.SaveAsync(rule, context.RequestAborted);
            await UserMachineResponse.WriteAsync(context, HttpStatusCode.OK, null);
        }
        catch (Exception e) when (e is JsonException or ArgumentException)
        { await UserMachineResponse.WriteAsync(context, HttpStatusCode.BadRequest, "Choose valid device opening rules."); }
    }
}
