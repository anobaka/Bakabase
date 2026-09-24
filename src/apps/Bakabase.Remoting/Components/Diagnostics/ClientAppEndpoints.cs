using System.Text.Json;
using System.Text.Json.Serialization;
using Bakabase.Remoting.Components.UserMachine;
using Bakabase.Infrastructures.Components.App;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Bakabase.Remoting.Components.Diagnostics;

/// <summary>
/// Where this client keeps its own things, under <c>/client/app</c>.
/// </summary>
/// <remarks>
/// <c>/app/info</c> is forwarded and describes the server: its data directory, its
/// cache, its version. Every path in it belongs to another machine and cannot be
/// opened from here. These are this client's, and they are worth showing for the same
/// reason the server's are — somebody has to be able to find them when asked to.
/// </remarks>
public static class ClientAppEndpoints
{
    public const string Prefix = "/client/app";

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// The directories this client will open, by the names it publishes them under.
    /// </summary>
    /// <remarks>
    /// A name rather than a path, and that is the point: the caller is a page, and a
    /// page that could name a path could name any path. The client resolves the name
    /// itself, so the only directories it will ever reveal are its own.
    /// </remarks>
    public static IReadOnlyDictionary<string, Func<AppService, string>> Directories { get; } =
        new Dictionary<string, Func<AppService, string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["data"] = app => app.AppDataDirectory,
            ["log"] = app => app.AppInfo.LogPath,
            // Where an installation of Locale Emulator has to sit for this client to use
            // it: the settings list of dependent components is the server's, and the copy
            // that launches a work is the one on this machine.
            ["components"] = app => app.ComponentsPath
        };

    public static void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet($"{Prefix}/info", async (HttpContext context) =>
        {
            var app = Resolve(context);

            await WriteAsync(context, new
            {
                version = AppService.CoreVersion.ToString(),
                available = app != null,
                dataDirectory = app == null ? null : Directories["data"](app),
                logDirectory = app == null ? null : Directories["log"](app),
                componentsDirectory = app == null ? null : Directories["components"](app)
            });
        });

        endpoints.MapPost($"{Prefix}/open", async (HttpContext context, ILoggerFactory loggers) =>
        {
            var name = context.Request.Query["directory"].ToString();
            var app = Resolve(context);

            if (app == null || !Directories.TryGetValue(name, out var resolve))
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                await WriteAsync(context, new {opened = false});

                return;
            }

            await OpenAsync(context, resolve(app), loggers);
        });
    }

    /// <summary>
    /// Reveals one of this client's own directories, or answers 404 when it does not
    /// exist yet — a client that has never written a log, or never installed a
    /// component.
    /// </summary>
    internal static async Task OpenAsync(HttpContext context, string? directory, ILoggerFactory loggers)
    {
        if (directory == null || !Directory.Exists(directory))
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            await WriteAsync(context, new {opened = false});

            return;
        }

        try
        {
            context.RequestServices.GetRequiredService<IShellOpener>().Reveal(directory, false);
            await WriteAsync(context, new {opened = true});
        }
        catch (Exception e)
        {
            loggers.CreateLogger(typeof(ClientAppEndpoints))
                .LogError(e, "Failed to open {Directory}", directory);

            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            await WriteAsync(context, new {opened = false});
        }
    }

    /// <summary>
    /// The application service, or null in a host that has none — a test host, which
    /// has no directories of its own either.
    /// </summary>
    internal static AppService? Resolve(HttpContext context)
    {
        try
        {
            return context.RequestServices.GetService(typeof(AppService)) as AppService;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    internal static async Task WriteAsync(HttpContext context, object payload)
    {
        context.Response.ContentType = "application/json";

        await context.Response.WriteAsync(JsonSerializer.Serialize(new
        {
            code = context.Response.StatusCode == StatusCodes.Status200OK ? 0 : context.Response.StatusCode,
            data = payload
        }, Json), context.RequestAborted);
    }
}
