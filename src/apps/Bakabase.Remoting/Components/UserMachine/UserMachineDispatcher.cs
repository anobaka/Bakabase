using System.Net;
using System.Text.Json;
using Bakabase.Remoting.Components.Forwarding;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Bakabase.Remoting.Components.UserMachine;

/// <summary>
/// Decides whether a request is this machine's to answer, and answers it if so.
/// </summary>
/// <remarks>
/// <para>
/// A route in the table with no handler is answered here rather than forwarded, and the
/// answer says which side is behind: the server would refuse it anyway, so a round trip
/// would only turn "this client cannot do that yet" into a less useful refusal that
/// blames the server.
/// </para>
/// <para>
/// Handlers are checked against the table at construction. A handler for a route nobody
/// declared would sit unreachable forever, which is the kind of mistake that only shows
/// up as a feature quietly not working.
/// </para>
/// </remarks>
public sealed class UserMachineDispatcher
{
    private readonly Dictionary<string, IUserMachineHandler> _handlers;
    private readonly ILogger<UserMachineDispatcher> _logger;

    public UserMachineDispatcher(IEnumerable<IUserMachineHandler> handlers,
        ILogger<UserMachineDispatcher> logger)
    {
        _logger = logger;
        _handlers = handlers.ToDictionary(h => h.RouteKey, StringComparer.OrdinalIgnoreCase);

        var undeclared = _handlers.Keys.Where(k => !UserMachineRoutes.Contains(k)).ToArray();

        if (undeclared.Length > 0)
        {
            throw new InvalidOperationException(
                $"These handlers answer routes that are not in {nameof(UserMachineRoutes)}, so nothing would " +
                $"ever reach them: {string.Join(", ", undeclared)}");
        }
    }

    /// <summary>Route keys this client can actually run. Reported by the status endpoint and tests.</summary>
    public IReadOnlyCollection<string> ImplementedRoutes => _handlers.Keys;

    /// <summary>
    /// True when this request was handled here. False means the server's to answer.
    /// </summary>
    public async Task<bool> TryHandleAsync(HttpContext context)
    {
        var match = UserMachineRoutes.Match(context.Request.Method, context.Request.Path.Value ?? string.Empty);

        if (match == null)
        {
            return false;
        }

        if (!_handlers.TryGetValue(match.Route.Key, out var handler))
        {
            _logger.LogInformation("No local handler for {Route}; telling the caller this client is behind.",
                match.Route.Key);

            await WriteNeedsNewerClient(context, match.Route.Key);
            return true;
        }

        await handler.HandleAsync(context, match.Values);
        return true;
    }

    private static async Task WriteNeedsNewerClient(HttpContext context, string route)
    {
        context.Response.StatusCode = (int) HttpStatusCode.NotImplemented;
        context.Response.ContentType = "application/json";
        context.Response.Headers["X-Bakabase-Client"] = ClientForwardingFailure.NeedsNewerClient.ToString();

        await context.Response.WriteAsync(JsonSerializer.Serialize(new
        {
            code = (int) HttpStatusCode.NotImplemented,
            message = $"This version of the Bakabase client cannot run {route} on your machine yet. " +
                      "Updating the client will enable it."
        }), context.RequestAborted);
    }
}
