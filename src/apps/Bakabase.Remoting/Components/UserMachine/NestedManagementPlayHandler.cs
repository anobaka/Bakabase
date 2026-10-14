using System.Net;
using Microsoft.AspNetCore.Http;

namespace Bakabase.Remoting.Components.UserMachine;

/// <summary>
/// A managed device's view cannot launch an application through another manager.
/// The app's own unified library binds native opening to its own pairing and profiles.
/// </summary>
public sealed class NestedManagementPlayHandler : IUserMachineHandler
{
    public string RouteKey => "POST /federation/local/management/{serverId}/play";

    public Task HandleAsync(HttpContext context, IReadOnlyDictionary<string, string> routeValues) =>
        UserMachineResponse.WriteAsync(context, HttpStatusCode.Conflict,
            "Return to this app's own library and select the source device to open a player on this computer.");
}

public sealed class NestedManagementDirectoryHandler : IUserMachineHandler
{
    public string RouteKey => "POST /federation/local/management/{serverId}/directory";

    public Task HandleAsync(HttpContext context, IReadOnlyDictionary<string, string> routeValues) =>
        UserMachineResponse.WriteAsync(context, HttpStatusCode.Conflict,
            "Return to this app's own library and select the source device to open its folder on this computer.");
}

public sealed class NestedManagementBatchCandidatesHandler : IUserMachineHandler
{
    public string RouteKey => "POST /federation/local/management/{serverId}/batch-play/candidates";
    public Task HandleAsync(HttpContext context, IReadOnlyDictionary<string, string> routeValues) =>
        UserMachineResponse.WriteAsync(context, HttpStatusCode.Conflict,
            "Return to this app's own library and select the source device to use this computer's players.");
}

public sealed class NestedManagementBatchPlayHandler : IUserMachineHandler
{
    public string RouteKey => "POST /federation/local/management/{serverId}/batch-play";
    public Task HandleAsync(HttpContext context, IReadOnlyDictionary<string, string> routeValues) =>
        UserMachineResponse.WriteAsync(context, HttpStatusCode.Conflict,
            "Return to this app's own library and select the source device to use this computer's players.");
}
