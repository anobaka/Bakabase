using Microsoft.AspNetCore.Http;

namespace Bakabase.Remoting.Components.UserMachine;

/// <summary>
/// Runs one user-machine endpoint here instead of forwarding it.
/// </summary>
/// <remarks>
/// These are the actions whose effect lands on whatever computer executes them —
/// launching a player, opening a folder, showing a window. On the server they would put
/// something on a screen nobody is watching, so the server refuses them and the client
/// runs them instead.
/// </remarks>
public interface IUserMachineHandler
{
    /// <summary>
    /// Which route this answers, in <see cref="UserMachineRoute.Key"/> form. Checked
    /// against <see cref="UserMachineRoutes"/> at startup, so a handler for a route
    /// nobody declared fails loudly rather than sitting there unreachable.
    /// </summary>
    string RouteKey { get; }

    Task HandleAsync(HttpContext context, IReadOnlyDictionary<string, string> routeValues);
}
