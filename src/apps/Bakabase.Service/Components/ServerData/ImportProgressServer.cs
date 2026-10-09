using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Bakabase.Service.Components.ServerData;

/// <summary>Database-free setup and read-only progress while the business host is stopped.</summary>
public sealed class ImportProgressServer : IAsyncDisposable
{
    public const string PagePath = "/app/data-path/import/progress";
    public const string StatusPath = PagePath + "/status";
    public const string TokenHeader = "X-Bakabase-Import-Token";
    public const string SetupPath = "/setup";
    public const string SetupTokenHeader = "X-Bakabase-Setup-Token";
    private static readonly string Page = ReadPage("ImportProgress");
    private static readonly string SetupPage = ReadPage("ServerSetup");
    private readonly WebApplication _app;
    public bool IsRunning { get; private set; }
    public string[] Addresses => _app.Urls.ToArray();
    // Desktop setup uses a temporary port; once ready, links back to the app must
    // reach its real listener. Headless setup hands off the same port instead.
    public string? ApplicationAddress { get; set; }
    public CancellationToken Stopping => _app.Lifetime.ApplicationStopping;

    private ImportProgressServer(WebApplication app) => _app = app;

    public static async Task<ImportProgressServer> StartAsync(ImportProgressStore store, string[] addresses,
        Func<Task<string?>>? directoryPicker = null)
    {
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions
        { Args = [], ContentRootPath = System.AppContext.BaseDirectory, EnvironmentName = "Production" });
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls(addresses);
        var app = builder.Build();
        var server = new ImportProgressServer(app);
        app.Run(async context =>
        {
            if (await TryHandleAsync(context, store, directoryPicker)) return;
            if (context.Request.Path == "/" && HttpMethods.IsGet(context.Request.Method))
            {
                if (server.ApplicationAddress is { } applicationAddress)
                {
                    SetHeaders(context);
                    context.Response.Redirect(applicationAddress);
                    return;
                }
                await WritePage(context, ServerSetupSession.Current is { Mode: "first-run", Submitted: false } ? SetupPage : Page);
                return;
            }
            SetHeaders(context);
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            context.Response.Headers.RetryAfter = "2";
            await context.Response.WriteAsJsonAsync(new { message = "AppData import maintenance is active." });
        });
        try
        {
            await app.StartAsync();
            server.IsRunning = true;
            return server;
        }
        catch { await app.DisposeAsync(); throw; }
    }

    // Called before the regular application gates too: the capability authorizes only
    // this operation's read-only state and survives imported pairing/identity changes.
    public static async Task<bool> TryHandleAsync(HttpContext context, ImportProgressStore store,
        Func<Task<string?>>? directoryPicker = null)
    {
        if (await TryHandleSetupAsync(context, directoryPicker)) return true;
        store = ServerSetupSession.Current is { Mode: "first-run", Submitted: true, Monitoring: { } selected }
            ? selected : ImportProgressStore.Current ?? store;
        var path = context.Request.Path;
        if (path != PagePath && path != StatusPath) return false;
        SetHeaders(context);
        if (!HttpMethods.IsGet(context.Request.Method))
        {
            context.Response.StatusCode = StatusCodes.Status405MethodNotAllowed;
            context.Response.Headers.Allow = "GET";
            return true;
        }
        if (path == PagePath)
        {
            await WritePage(context, Page);
            return true;
        }
        if (!store.TryReadAuthorized(context.Request.Headers[TokenHeader].ToString(), out var progress))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new { message = "A valid import monitoring token is required." });
            return true;
        }
        await context.Response.WriteAsJsonAsync(progress! with { AutomaticMaintenance = SetupProcessCoordinator.IsCoordinator || SetupChildConnection.Current != null }, ImportProgressStore.JsonOptions);
        return true;
    }

    private static async Task<bool> TryHandleSetupAsync(HttpContext context, Func<Task<string?>>? directoryPicker)
    {
        var path = context.Request.Path.Value;
        if (path is not (SetupPath or "/setup/status" or "/setup/validate" or "/setup/apply" or "/setup/local-session" or "/setup/browse" or "/setup/directories"
            or "/setup/draft" or "/setup/draft/clear" or "/setup/preflight" or "/setup/preflight/tree" or "/setup/preflight/preview")) return false;
        SetHeaders(context);
        var get = path is SetupPath or "/setup/status" or "/setup/directories" or "/setup/preflight/tree" ||
                  path is "/setup/draft" or "/setup/preflight" && HttpMethods.IsGet(context.Request.Method);
        if (get ? !HttpMethods.IsGet(context.Request.Method) : !HttpMethods.IsPost(context.Request.Method))
        {
            context.Response.StatusCode = StatusCodes.Status405MethodNotAllowed;
            context.Response.Headers.Allow = get ? "GET" : "POST";
            return true;
        }
        if (path == SetupPath) { await WritePage(context, SetupPage); return true; }
        var session = ServerSetupSession.Current;
        var token = context.Request.Headers[SetupTokenHeader].ToString();
        if (path == "/setup/local-session")
        {
            var container = string.Equals(Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER"), "true", StringComparison.OrdinalIgnoreCase);
            if (session is { Mode: "first-run", Submitted: false } && ServerSetupLocalAccess.CanClaim(context, container))
            {
                await context.Response.WriteAsJsonAsync(new { setupToken = session.Token });
                return true;
            }
        }
        if (path == "/setup/local-session" || session == null || !session.Authorize(token))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new { message = "Open setup using the link in the startup log or the authorized import entry in settings." });
            return true;
        }
        if (path == "/setup/status")
        {
            await context.Response.WriteAsJsonAsync(session.Read() with { CanBrowse = directoryPicker != null && session.Mode == "first-run" }, ImportProgressStore.JsonOptions);
            return true;
        }
        try
        {
            if (path == "/setup/draft" && get)
            {
                await context.Response.WriteAsJsonAsync(session.ReadDraft(token), ImportProgressStore.JsonOptions);
                return true;
            }
            if (path == "/setup/preflight" && get)
            {
                await context.Response.WriteAsJsonAsync(session.ReadPreflight(token), ImportProgressStore.JsonOptions);
                return true;
            }
            if (path == "/setup/preflight/tree")
            {
                var query = context.Request.Query;
                if (query.ContainsKey("offset") && !int.TryParse(query["offset"], out _)) throw new ArgumentException("Invalid tree offset.");
                var offset = int.TryParse(query["offset"], out var parsed) ? parsed : 0;
                await context.Response.WriteAsJsonAsync(session.ReadPathTree(query["scanId"].ToString(),
                    query.ContainsKey("parent") ? query["parent"].ToString() : null, offset, token), ImportProgressStore.JsonOptions);
                return true;
            }
            if (path == "/setup/draft/clear")
            {
                await context.Response.WriteAsJsonAsync(session.ClearDraft(token), ImportProgressStore.JsonOptions);
                return true;
            }
            if (path == "/setup/directories")
            {
                var listing = session.BrowseDirectories(token, context.Request.Query["path"].ToString(),
                    context.Request.Query.ContainsKey("newFolderName") ? context.Request.Query["newFolderName"].ToString() : null,
                    context.RequestAborted);
                await context.Response.WriteAsJsonAsync(listing, ImportProgressStore.JsonOptions);
                return true;
            }
            if (path == "/setup/browse")
            {
                if (directoryPicker == null || session.Mode != "first-run")
                {
                    context.Response.StatusCode = StatusCodes.Status409Conflict;
                    await context.Response.WriteAsJsonAsync(new { message = "Directory browsing is available in desktop setup only." });
                }
                else
                {
                    var selectedPath = await directoryPicker();
                    // The setup could have been committed while the native dialog was open.
                    if (!session.Authorize(token)) throw new UnauthorizedAccessException();
                    await context.Response.WriteAsJsonAsync(new { path = selectedPath });
                }
                return true;
            }
            if (context.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } limit)
                limit.MaxRequestBodySize = 2 * 1024 * 1024;
            if (path == "/setup/draft")
            {
                var draft = await context.Request.ReadFromJsonAsync<SetupDraftRequest>(ImportProgressStore.JsonOptions)
                            ?? throw new ArgumentException("A draft is required.");
                await context.Response.WriteAsJsonAsync(session.SaveDraft(draft, token), ImportProgressStore.JsonOptions);
                return true;
            }
            if (path == "/setup/preflight/preview")
            {
                var preview = await context.Request.ReadFromJsonAsync<SetupPreviewRequest>(ImportProgressStore.JsonOptions)
                              ?? throw new ArgumentException("A path preview is required.");
                await context.Response.WriteAsJsonAsync(session.PreviewPaths(preview, token), ImportProgressStore.JsonOptions);
                return true;
            }
            var request = await context.Request.ReadFromJsonAsync<ServerSetupSession.SetupRequest>(ImportProgressStore.JsonOptions)
                          ?? throw new ArgumentException("A setup request is required.");
            if (path == "/setup/preflight")
                await context.Response.WriteAsJsonAsync(session.StartPreflight(request, token,
                    string.Equals(context.Request.Query["force"], "true", StringComparison.OrdinalIgnoreCase)), ImportProgressStore.JsonOptions);
            else if (path == "/setup/validate")
                await context.Response.WriteAsJsonAsync(session.Validate(request, token), ImportProgressStore.JsonOptions);
            else
            {
                var result = session.Commit(request, token);
                var relay = context.Request.Headers["X-Bakabase-Setup-Relay"].ToString();
                var expectedRelay = SetupProcessCoordinator.RelaySecret;
                var relayed = expectedRelay != null && relay.Length == expectedRelay.Length &&
                    CryptographicOperations.FixedTimeEquals(System.Text.Encoding.UTF8.GetBytes(relay), System.Text.Encoding.UTF8.GetBytes(expectedRelay));
                // The business relay acknowledges after its public response has completed.
                if (!relayed) context.Response.OnCompleted(() => { session.SignalCommitted(); return Task.CompletedTask; });
                if (SetupProcessCoordinator.IsCoordinator)
                {
                    // A durable commit remains authorized even if the browser/relay disconnects.
                    // Normally the public response ACK wins; bound the delay if it never arrives.
                    _ = Task.Run(async () => { await Task.Delay(TimeSpan.FromSeconds(5)); session.SignalCommitted(); });
                }
                await context.Response.WriteAsJsonAsync(result, ImportProgressStore.JsonOptions);
            }
        }
        catch (UnauthorizedAccessException)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new { message = "The setup session expired or was already submitted." });
        }
        catch (ServerSetupDirectoryBrowser.AccessDeniedException e)
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(new { message = e.Message });
        }
        catch (DirectoryNotFoundException) when (path == "/setup/directories")
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            await context.Response.WriteAsJsonAsync(new { message = "This folder no longer exists or its drive is unavailable. Choose another folder." });
        }
        catch (Exception e) when (e is IOException or ArgumentException or InvalidOperationException or JsonException)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsJsonAsync(new { message = e.Message });
        }
        return true;
    }

    private static void SetHeaders(HttpContext context)
    {
        context.Response.Headers.CacheControl = "no-store";
        context.Response.Headers["Referrer-Policy"] = "no-referrer";
        context.Response.Headers["X-Content-Type-Options"] = "nosniff";
        context.Response.Headers["X-Frame-Options"] = "DENY";
        context.Response.Headers.ContentSecurityPolicy = "default-src 'none'; frame-ancestors 'none'; base-uri 'none'";
    }

    private static async Task WritePage(HttpContext context, string page)
    {
        SetHeaders(context);
        var nonce = Convert.ToBase64String(RandomNumberGenerator.GetBytes(24));
        context.Response.Headers.ContentSecurityPolicy = $"default-src 'none'; script-src 'nonce-{nonce}'; style-src 'nonce-{nonce}'; connect-src 'self'; frame-ancestors 'none'; base-uri 'none'; form-action 'none'";
        context.Response.ContentType = "text/html; charset=utf-8";
        await context.Response.WriteAsync(page.Replace("__NONCE__", nonce));
    }

    private static string ReadPage(string name)
    {
        using var stream = typeof(ImportProgressServer).Assembly.GetManifestResourceStream($"Bakabase.Service.{name}.html")
                           ?? throw new InvalidOperationException("Import progress page is missing.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    public Task WaitForShutdownAsync() => _app.WaitForShutdownAsync();
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (!IsRunning) return;
        IsRunning = false;
        await _app.StopAsync(cancellationToken);
    }
    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        await _app.DisposeAsync();
    }
}

// All StartingAsync callbacks run before any hosted service StartAsync, including
// Kestrel's port bind. Keep monitoring alive through all database migrations.
internal sealed class ImportProgressHandoff(Func<CancellationToken, Task> handoff) : IHostedLifecycleService
{
    public Task StartingAsync(CancellationToken cancellationToken) => handoff(cancellationToken);
    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
