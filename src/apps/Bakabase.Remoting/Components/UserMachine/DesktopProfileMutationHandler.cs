using System.Net;
using Microsoft.AspNetCore.Http;

namespace Bakabase.Remoting.Components.UserMachine;

/// <summary>
/// A managed device's page cannot change or open this desktop's data spaces.
/// This is a permanent boundary, not a feature waiting for a newer desktop version.
/// </summary>
public sealed class DesktopProfileMutationHandler(string routeKey) : IUserMachineHandler
{
    public string RouteKey => routeKey;

    public Task HandleAsync(HttpContext context, IReadOnlyDictionary<string, string> routeValues) =>
        UserMachineResponse.WriteAsync(context, HttpStatusCode.Conflict,
            "Return to this app's own view to manage or open its data spaces.");
}
