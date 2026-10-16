using System.Net;
using System.Text.Json;
using Bakabase.Abstractions.Services;
using Bakabase.Remoting.Components.Connection;
using Microsoft.AspNetCore.Http;

namespace Bakabase.Remoting.Components.UserMachine;

/// <summary>A managed page may read the opening mode for its own resource, but never local executables or other servers' rules.</summary>
public sealed class ResourceProfilePlaybackHandler(ActiveConnection connection,
    ISourceResourceProfilePlaybackService? profiles = null) : IUserMachineHandler
{
    public string RouteKey => "GET /player/resource-profile";

    public async Task HandleAsync(HttpContext context, IReadOnlyDictionary<string, string> routeValues)
    {
        var source = context.Request.Query["sourceDeviceId"];
        if (connection.Server == null || source.Count != 1 || source[0] != connection.Server.ServerId ||
            context.Request.Query["resourceId"].Count != 1 ||
            !int.TryParse(context.Request.Query["resourceId"], out var resourceId) || resourceId <= 0)
        {
            await UserMachineResponse.WriteAsync(context, HttpStatusCode.BadRequest, "Choose a resource from this server.");
            return;
        }
        if (profiles == null)
        {
            await UserMachineResponse.WriteAsync(context, HttpStatusCode.NotImplemented,
                "Update this desktop app to use local resource profiles for another device.");
            return;
        }
        try
        {
            var epoch = context.Request.Query["libraryEpoch"];
            if (epoch.Count > 1) throw new ArgumentException("Invalid source library identity.");
            var matched = await profiles.ResolveAsync(connection.Server.ServerId,
                string.IsNullOrEmpty(epoch) ? null : epoch.ToString(), resourceId, context.RequestAborted);
            context.Response.ContentType = "application/json";
            context.Response.Headers.CacheControl = "no-store";
            await context.Response.WriteAsync(JsonSerializer.Serialize(new
            {
                code = 0, data = new {matched.OpeningMode, matched.ProfileIds}
            }, JsonSerializerOptions.Web), context.RequestAborted);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested) { }
        catch (Exception e) when (e is InvalidOperationException or HttpRequestException or ArgumentException or IOException)
        {
            await UserMachineResponse.WriteAsync(context, HttpStatusCode.Conflict,
                "The local resource profiles could not be matched. Reconnect the source device and retry.");
        }
    }
}

/// <summary>Coalesces the visible resource cards into one source filter evaluation.</summary>
public sealed class ResourceProfilesPlaybackHandler(ActiveConnection connection,
    ISourceResourceProfilePlaybackService? profiles = null) : IUserMachineHandler
{
    public string RouteKey => "POST /player/resource-profiles";
    private sealed record Input(string? SourceDeviceId, string? LibraryEpoch, int[]? ResourceIds);

    public async Task HandleAsync(HttpContext context, IReadOnlyDictionary<string, string> routeValues)
    {
        try
        {
            const int maximumBytes = 16 * 1024;
            var buffer = new byte[maximumBytes + 1];
            var length = 0;
            while (length < buffer.Length)
            {
                var read = await context.Request.Body.ReadAsync(buffer.AsMemory(length), context.RequestAborted);
                if (read == 0) break;
                length += read;
            }
            if (length > maximumBytes)
            {
                await UserMachineResponse.WriteAsync(context, HttpStatusCode.RequestEntityTooLarge, "Too many resources.");
                return;
            }
            var input = JsonSerializer.Deserialize<Input>(buffer.AsSpan(0, length), JsonSerializerOptions.Web);
            if (connection.Server == null || input?.SourceDeviceId != connection.Server.ServerId ||
                input.ResourceIds is not {Length: > 0 and <= 256} || input.ResourceIds.Any(id => id <= 0))
                throw new ArgumentException("Choose resources from this server.");
            if (profiles == null) throw new InvalidOperationException("Update this desktop app to use source resource profiles.");
            var matched = await profiles.ResolveManyAsync(connection.Server.ServerId, input.LibraryEpoch,
                input.ResourceIds.Distinct().ToArray(), context.RequestAborted);
            context.Response.ContentType = "application/json";
            context.Response.Headers.CacheControl = "no-store";
            await context.Response.WriteAsync(JsonSerializer.Serialize(new
            {
                code = 0, data = matched.Select(p => new {resourceId = p.Key, p.Value.OpeningMode, p.Value.ProfileIds})
            }, JsonSerializerOptions.Web), context.RequestAborted);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested) { }
        catch (Exception e) when (e is ArgumentException or JsonException)
        {
            await UserMachineResponse.WriteAsync(context, HttpStatusCode.BadRequest, "Choose valid resources from this server.");
        }
        catch (Exception e) when (e is InvalidOperationException or HttpRequestException or IOException)
        {
            await UserMachineResponse.WriteAsync(context, HttpStatusCode.Conflict,
                "The local resource profiles could not be matched. Check the source device and its profile filters before retrying.");
        }
    }
}
