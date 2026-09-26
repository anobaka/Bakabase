using Bakabase.Abstractions.Models.Db;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Models.Domain.Options;
using Bakabase.Abstractions.Services;
using Bakabase.Infrastructures.Components.App;
using Bakabase.Infrastructures.Components.Configurations.App;
using Bakabase.InsideWorld.Business;
using Bakabase.InsideWorld.Business.Components.DataSync.Feed;
using Bakabase.InsideWorld.Business.Components.Dependency.Abstractions;
using Bakabase.Modules.DataSync.Abstractions;
using Bakabase.Modules.DataSync.Canonical;
using Bakabase.Modules.DataSync.Wire;
using Bakabase.Modules.Federation.Peers;
using Bakabase.Modules.Federation.Identity;
using Bakabase.Modules.RemoteAccess.Abstractions.Models;
using Bakabase.Modules.RemoteAccess.Abstractions.Services;
using Bakabase.Modules.RemoteAccess.Components.Pairing;
using Bakabase.Modules.RemoteAccess.Services;
using Bakabase.Remoting.Components.Console;
using Bakabase.Service.Components;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Bootstrap.Components.Configuration.Abstractions;
using Bootstrap.Components.Orm;
using Microsoft.Extensions.FileProviders;
using System.Text.Json;
using System.Text.Json.Nodes;

// Each process owns its static AppService, independent SQLite database, options, keys and HTTP port.
// This executable is never packaged. It uses the production startup, routes, middleware and adapters.
if (args.Length < 2 || !int.TryParse(args[0], out var port) || !Path.IsPathFullyQualified(args[1]))
    throw new ArgumentException("Usage: Bakabase.Federation.TestHost <port> <absolute-empty-data-directory> [resource-count]");
var dataDirectory = args[1];
// Prepare the complete fixture settings before configuration providers/watchers
// exist. Saving ServerId and then Mode after startup let a delayed options reload
// temporarily replace the manager's new value with Disabled after "ready".
var remoteOptionsPath = Path.Combine(dataDirectory, "configs", "remote-access.json");
if (!File.Exists(remoteOptionsPath))
{
    Directory.CreateDirectory(Path.GetDirectoryName(remoteOptionsPath)!);
    var temporary = remoteOptionsPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
    try
    {
        await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(new
        {
            RemoteAccess = new RemoteAccessOptions
            {
                ServerId = Guid.NewGuid().ToString("N"),
                Mode = RemoteAccessMode.Enabled,
                RequirePairing = true
            }
        }));
        File.Move(temporary, remoteOptionsPath);
    }
    finally { if (File.Exists(temporary)) File.Delete(temporary); }
}
File.Delete(Path.Combine(dataDirectory, "ready"));
Environment.SetEnvironmentVariable("BAKABASE_FEDERATION_TEST_DATA_DIR", dataDirectory);
// Whatever harness starts this host, and whatever its environment says.
FixtureAnalytics.TurnOff();
AppDataAnchor.Use(new AppDataPathProfile("BAKABASE_FEDERATION_TEST_DATA_DIR", "Bakabase.Federation.Test", "Bakabase.Federation.Test"));
var count = args.Length > 2 ? int.Parse(args[2]) : 257;
// Optional: be the desktop app rather than a headless server. The value is where the
// harness's browser opens this host's UI — the "main window" whose origin UnifiedHost records.
var desktopWindow = Environment.GetEnvironmentVariable("BAKABASE_FEDERATION_TEST_DESKTOP_WINDOW");
if (desktopWindow != null && !(Uri.TryCreate(desktopWindow, UriKind.Absolute, out var window) &&
                               window.Scheme == Uri.UriSchemeHttp && window.IsLoopback && window.Port == port))
    throw new ArgumentException("The desktop window must be an http loopback address on this fixture's own port.");
// Optional: record every request this host receives, before any of its own middleware.
var requestLog = Environment.GetEnvironmentVariable("BAKABASE_FEDERATION_TEST_REQUEST_LOG");
if (requestLog != null && !Path.IsPathFullyQualified(requestLog))
    throw new ArgumentException("The request log must be an absolute file path.");
// Optional: the name this server gives itself to other devices, in place of the machine's.
var serverName = Environment.GetEnvironmentVariable("BAKABASE_FEDERATION_TEST_SERVER_NAME");
if (serverName != null && !System.Text.RegularExpressions.Regex.IsMatch(serverName, "^[a-z][a-z0-9-]{0,31}$"))
    throw new ArgumentException("The server name must be a short lowercase label.");
// Optional: "loopback" makes the only address this server offers other devices the loopback address it listens on.
// Otherwise it offers this machine's interface addresses, where a fixture never listens, so a device that connects
// back to an address it was offered (a two-way read-back, §7.2.4 of the data sync spec) could never reach it.
var addresses = Environment.GetEnvironmentVariable("BAKABASE_FEDERATION_TEST_ADDRESSES");
if (addresses is not (null or "loopback"))
    throw new ArgumentException("The only fixture address choice is \"loopback\".");
// Optional: the host serves the custom property of this name to its data sync readers as a record of a newer schema.
var futureSchema = Environment.GetEnvironmentVariable(FutureSchemaFeedPageWriter.Variable);
if (futureSchema != null && string.IsNullOrWhiteSpace(futureSchema))
    throw new ArgumentException("The future-schema property must be named.");
var host = new FederationTestHost(port, dataDirectory, count, desktopWindow, requestLog, serverName,
    addresses == "loopback" ? [new RemoteAccessAddress($"http://127.0.0.1:{port}", "loopback")] : null, futureSchema);
await host.Start([]);

sealed class FederationTestHost(int port, string dataDirectory, int count, string? desktopWindow, string? requestLog,
        string? serverName, IReadOnlyList<RemoteAccessAddress>? addresses, string? futureSchema)
    : BakabaseHost(new NullGuiAdapter(), new NullSystemService())
{
    protected override string? SingleInstanceId => null;
    protected override IReadOnlyList<int>? OverrideListeningPorts() => [port];
    protected override string ListeningInterface => "127.0.0.1";

    protected override IHostBuilder CreateHostBuilder(params string[] args) => ComposeDesktop(base.CreateHostBuilder(args)
        .ConfigureServices(services =>
        {
            if (requestLog != null)
                // First, so it is the outermost middleware and sees requests the server refuses too.
                services.Insert(0, ServiceDescriptor.Singleton<IStartupFilter>(new RequestRecorder(requestLog)));
            // The fixture exercises media serving, never dependency installation or external downloads.
            services.RemoveAll<IDependentComponentService>();
            // A benchmark must not vary every result row's label with the CI VM
            // hostname. This optional fixture-only decorator preserves production
            // persistent identity, epochs, stores, grants and query byte limits.
            var fixtureName = Environment.GetEnvironmentVariable("BAKABASE_FEDERATION_TEST_NODE_NAME");
            if (fixtureName != null)
            {
                if (fixtureName is not ("benchmark-a" or "benchmark-b"))
                    throw new ArgumentException("Unknown benchmark node label.");
                services.RemoveAll<INodeIdentityProvider>();
                services.AddSingleton<INodeIdentityProvider>(provider => new BenchmarkNodeIdentityProvider(
                    new NodeIdentityProvider(provider.GetRequiredService<FederationStateStore>()), fixtureName));
            }
            if (serverName != null || addresses != null)
            {
                // Every fixture on one machine would otherwise share its name, so nothing a
                // test reads by name could tell one server from another; and offer addresses
                // where it does not listen.
                services.RemoveAll<IRemoteAccessService>();
                services.AddSingleton<RemoteAccessService>();
                services.AddSingleton<IRemoteAccessService>(provider =>
                    new FixtureRemoteAccess(provider.GetRequiredService<RemoteAccessService>(), serverName, addresses));
            }
            if (futureSchema != null)
            {
                // Replaces the production writer the data sync feed composes with (registered with TryAdd).
                services.RemoveAll<IDataSyncFeedPageWriter>();
                services.AddSingleton<IDataSyncFeedPageWriter>(new FutureSchemaFeedPageWriter(futureSchema));
            }
            services.AddSingleton<IStartupFilter, FederationTestStaticFiles>();
        }));

    /// <summary>
    /// What <c>Bakabase.App</c>'s UnifiedHost adds to this same server, and nothing else: the
    /// relay manager, appended after every server registration. Avalonia, the tray and the
    /// shell's switcher menu are not part of it; the harness's browser is the window.
    /// </summary>
    private IHostBuilder ComposeDesktop(IHostBuilder builder) => desktopWindow == null
        ? builder
        : builder.ConfigureServices(services => services.AddRemoteConsole());

    /// <summary>
    /// Records where the main window opens, exactly as UnifiedHost does: "back to this device"
    /// must land on that origin. The window here is the harness's browser, so its address is
    /// given rather than the one a debug build would pick (the frontend dev server's).
    /// </summary>
    protected override string OverrideFeAddress(string feAddress)
    {
        if (desktopWindow == null)
            return base.OverrideFeAddress(feAddress);
        var address = base.OverrideFeAddress(desktopWindow);
        try
        {
            Host.Services.GetService<RemoteConsoleLocalOrigin>()?.Set(address);
        }
        catch (ObjectDisposedException)
        {
        }
        return address;
    }

    protected override async Task ExecuteCustomProgress(IServiceProvider services)
    {
        await base.ExecuteCustomProgress(services);
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BakabaseDbContext>();
        if (!await db.ResourcesV2.AnyAsync())
        {
            var resourceOrm = scope.ServiceProvider.GetRequiredService<
                FullMemoryCacheResourceService<BakabaseDbContext, ResourceDbModel, int>>();
            var propertyOrm = scope.ServiceProvider.GetRequiredService<
                FullMemoryCacheResourceService<BakabaseDbContext, ReservedPropertyValue, int>>();
            // Deterministically cover background search-index warmup before seeding.
            // Raw DbContext inserts leave these already-loaded caches empty forever.
            if (await resourceOrm.GetByKey(1) != null || (await propertyOrm.GetAll()).Count != 0)
                throw new InvalidOperationException("A fresh fixture must begin with empty resource/property caches.");
            var fixtureMedia = Environment.GetEnvironmentVariable("BAKABASE_FEDERATION_TEST_MEDIA_FILE");
            if (fixtureMedia != null && (!Path.IsPathFullyQualified(fixtureMedia) || !File.Exists(fixtureMedia)))
                throw new ArgumentException("The optional test media must be an existing absolute file path.");
            var mediaPath = Path.Combine(dataDirectory, "fixture" +
                (fixtureMedia == null ? ".wav" : Path.GetExtension(fixtureMedia)));
            if (fixtureMedia != null)
                File.Copy(fixtureMedia, mediaPath, overwrite: false);
            // A valid PCM wave with predictable bytes exercises browser audio and HTTP Range.
            else using (var output = new BinaryWriter(File.Create(mediaPath)))
            {
                var payload = 16000;
                output.Write("RIFF"u8); output.Write(payload + 36); output.Write("WAVEfmt "u8);
                output.Write(16); output.Write((short)1); output.Write((short)1); output.Write(8000);
                output.Write(16000); output.Write((short)2); output.Write((short)16);
                output.Write("data"u8); output.Write(payload); output.Write(new byte[payload]);
            }
            var resources = new List<ResourceDbModel>();
            var properties = new List<ReservedPropertyValue>();
            for (var id = 1; id <= count; id++)
            {
                resources.Add(new ResourceDbModel
                {
                    Id = id, Path = id == 1 ? mediaPath : null, IsFile = id == 1,
                    Status = ResourceStatus.Active
                });
                properties.Add(new ReservedPropertyValue
                {
                    ResourceId = id, Scope = (int)PropertyValueScope.Manual,
                    Name = id == 1 ? "Shared title" : $"Title {id % 29:D2}"
                });
            }
            await resourceOrm.AddRange(resources);
            await propertyOrm.AddRange(properties);
            if (count > 0 && (await scope.ServiceProvider.GetRequiredService<IResourceService>().Get(1) == null ||
                (await propertyOrm.GetFirstOrDefault(value => value.ResourceId == 1))?.Name != "Shared title"))
                throw new InvalidOperationException("Seeded fixture data must be visible through production resource/property caches.");
        }
        var identity = await services.GetRequiredService<INodeIdentityProvider>().GetAsync();
        if (Environment.GetEnvironmentVariable("BAKABASE_FEDERATION_TEST_NODE_NAME") != null)
            await File.WriteAllTextAsync(Path.Combine(dataDirectory, "benchmark-node.json"), JsonSerializer.Serialize(new
            {
                machineName = Environment.MachineName,
                fixtureLabel = identity.Name,
                nodeId = identity.NodeId,
                libraryEpoch = identity.LibraryEpoch
            }));
        var remoteAccess = services.GetRequiredService<IRemoteAccessService>();
        if (remoteAccess.GetEffectiveMode() != RemoteAccessMode.Enabled || !remoteAccess.GetRequirePairing())
            throw new InvalidOperationException("The fixture requires preconfigured Enabled remote access with pairing.");
        FixtureAnalytics.Verify(services.GetRequiredService<IConfiguration>(),
            services.GetRequiredService<IBOptions<AppOptions>>().Value.EnableAnonymousDataTracking);
        if (serverName != null && (await remoteAccess.GetServerDescriptorAsync()).Name != serverName)
            throw new InvalidOperationException("The fixture's server name did not take.");
        await services.GetRequiredService<FederationPeerService>().SetSharingAsync(true);
        if (desktopWindow != null)
        {
            // Ready means the startup import has run and "this device" has an origin.
            await services.GetRequiredService<RemoteConsoleManager>().Startup;
            if (services.GetRequiredService<RemoteConsoleLocalOrigin>().Origin !=
                new Uri(desktopWindow).GetLeftPart(UriPartial.Authority))
                throw new InvalidOperationException("The desktop fixture did not record its window's origin.");
        }
        services.GetRequiredService<AppService>().NotAcceptTerms = false;
        File.WriteAllText(Path.Combine(dataDirectory, "ready"), port.ToString());
        Console.WriteLine($"FEDERATION_TEST_READY {port}");
    }
}

sealed class BenchmarkNodeIdentityProvider(INodeIdentityProvider production, string name) : INodeIdentityProvider
{
    public async Task<NodeIdentity> GetAsync(CancellationToken cancellationToken = default) =>
        (await production.GetAsync(cancellationToken)) with { Name = name };
}

/// <summary>
/// The production remote-access service under another name, or at other addresses. The name — what
/// <c>server-info</c> and discovery announce, and so what every pairing records and every
/// switcher shows — is <see cref="Environment.MachineName"/> with no setting, which every
/// fixture on one machine shares. The addresses — what an invitation lists and a two-way
/// request offers to be read back at — are this machine's interfaces, where a fixture, which
/// listens on loopback only, cannot be reached. Everything else is the production service's answer.
/// </summary>
sealed class FixtureRemoteAccess(IRemoteAccessService production, string? name,
    IReadOnlyList<RemoteAccessAddress>? addresses) : IRemoteAccessService
{
    public RemoteAccessMode GetEffectiveMode() => production.GetEffectiveMode();
    public Task SetModeAsync(RemoteAccessMode? mode) => production.SetModeAsync(mode);
    public IReadOnlyList<RemoteAccessAddress> GetReachableAddresses() => addresses ?? production.GetReachableAddresses();
    public Task<string> GetOrCreateServerIdAsync() => production.GetOrCreateServerIdAsync();
    public bool GetAllowLiveTranscode() => production.GetAllowLiveTranscode();
    public Task SetAllowLiveTranscodeAsync(bool allow) => production.SetAllowLiveTranscodeAsync(allow);
    public bool GetRequirePairing() => production.GetRequirePairing();
    public Task SetRequirePairingAsync(bool require) => production.SetRequirePairingAsync(require);

    public async Task<RemoteAccessServerDescriptor> GetServerDescriptorAsync()
    {
        var descriptor = await production.GetServerDescriptorAsync();
        return name == null ? descriptor : descriptor with { Name = name };
    }
}

/// <summary>
/// The data sync feed as a newer build would serve one custom property: every record of the property named
/// <c>BAKABASE_DATASYNC_TEST_FUTURE_SCHEMA</c> goes out one schema version ahead, with a member this build does not
/// know and the record hash that content has. Everything else is the production writer's, so the manifest's kind hash
/// covers exactly what the pages carry. A reader must hold such a record (<c>Held(NewerSchema)</c>) and apply the rest
/// of the pull (spec §13.9 step 7).
/// </summary>
sealed class FutureSchemaFeedPageWriter(string propertyName) : IDataSyncFeedPageWriter
{
    public const string Variable = "BAKABASE_DATASYNC_TEST_FUTURE_SCHEMA";

    public DataSyncWrittenKind WriteKind(string snapshotId, string kind, long sinceSeq,
        IReadOnlyList<DataSyncWireRecord> records, DataSyncLimits limits) =>
        DataSyncWireWriter.WriteKind(snapshotId, kind, sinceSeq,
            kind == DataSyncKindIds.CustomProperty ? records.Select(FromTheFuture).ToList() : records, limits);

    private DataSyncWireRecord FromTheFuture(DataSyncWireRecord record)
    {
        if (record.Content is not { } content || record.Chunks > 0 ||
            content["name"]?.GetValueKind() != JsonValueKind.String ||
            content["name"]!.GetValue<string>() != propertyName)
            return record;
        var future = (JsonObject) content.DeepClone();
        future["futureSetting"] = new JsonObject { ["addedIn"] = record.SchemaVersion + 1 };
        return record with { SchemaVersion = record.SchemaVersion + 1, Content = future, Hash = ContentHash.Of(future) };
    }
}

/// <summary>
/// Writes a JSON line for every request this host receives, as it arrives: method, path, the
/// page's fetch metadata and <c>Origin</c>, whether it is a WebSocket handshake, whether the
/// query carried a relay switch ticket or the request a cookie, and what its device signature
/// verifies as against this host's own paired devices.
/// A second line with the same <c>id</c> gives the status this host answered with, written
/// before the answer leaves, so a client holding the answer finds the line already there.
/// </summary>
/// <remarks>
/// An observer only; it never changes a request. It lets a browser test see what reached a
/// server and how the server answered — which the server does not reveal itself: a relay on
/// the same machine is a loopback caller, and loopback callers skip device authentication; a
/// page's no-cors request gets an opaque answer. The verification uses its own nonce cache, so
/// it cannot consume a nonce the server's authenticator will later see. Neither the header nor
/// any key is written.
/// </remarks>
public sealed class RequestRecorder(string file) : IStartupFilter
{
    private readonly Lock _gate = new();
    private readonly NonceCache _nonces = new();
    private long _next;

    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        app.Use(async (context, following) =>
        {
            var request = context.Request;
            var id = Interlocked.Increment(ref _next);
            var query = request.QueryString.HasValue ? request.QueryString.Value![1..] : string.Empty;
            var header = request.Headers.Authorization.ToString();
            string signature = "none";
            string? device = null;
            if (RemoteRequestSignature.TryParseHeader(header) != null)
            {
                var bodyDigest = string.Empty;
                if (!HttpMethods.IsGet(request.Method) && !HttpMethods.IsHead(request.Method) &&
                    request.ContentLength is > 0 and <= RemoteRequestSignature.MaxHashedBodyBytes)
                {
                    // The same rule the server's gate applies when deciding what was signed.
                    request.EnableBuffering();
                    using var buffer = new MemoryStream((int) request.ContentLength!.Value);
                    await request.Body.CopyToAsync(buffer, context.RequestAborted);
                    request.Body.Position = 0;
                    bodyDigest = RemoteRequestSignature.HashBody(buffer.GetBuffer().AsSpan(0, (int) buffer.Length));
                }
                var result = new RemoteDeviceAuthenticator(
                        context.RequestServices.GetRequiredService<IRemoteDeviceService>(), _nonces)
                    .Authenticate(header, request.Method, request.Path.Value ?? string.Empty, query, bodyDigest);
                signature = result.Outcome.ToString();
                device = result.Outcome == DeviceAuthOutcome.Authenticated ? result.Device!.Id : null;
            }
            Write(new
            {
                id,
                method = request.Method,
                path = WithoutCapabilities(request.Path.Value),
                site = request.Headers["Sec-Fetch-Site"].ToString(),
                dest = request.Headers["Sec-Fetch-Dest"].ToString(),
                // The page the browser names — the only thing a WebSocket handshake from
                // Chromium says about who opened it, since it carries no fetch metadata.
                origin = request.Headers.Origin.ToString(),
                // Read from the request: the WebSocket feature is only installed inside the hub.
                websocket = request.Headers.Upgrade.Any(v =>
                    v?.Contains("websocket", StringComparison.OrdinalIgnoreCase) == true),
                ticket = query.Contains("__bakabase_switch", StringComparison.OrdinalIgnoreCase),
                cookie = request.Headers.Cookie.Count > 0,
                signature,
                device
            });
            context.Response.OnStarting(() =>
            {
                var headers = context.Response.Headers;
                Write(new
                {
                    id,
                    status = context.Response.StatusCode,
                    denial = headers["X-Bakabase-Remote-Access"].ToString(),
                    csp = headers.ContentSecurityPolicy.ToString()
                });
                return Task.CompletedTask;
            });
            await following();
        });
        next(app);
    };

    /// <summary>
    /// Paths whose next segment is a bearer ticket: whoever holds it may use it. The file is
    /// kept when a run fails, so the ticket is replaced by a placeholder.
    /// </summary>
    private static readonly string[] CapabilityPrefixes = ["/federation/local/media/"];

    private static string? WithoutCapabilities(string? path)
    {
        foreach (var prefix in CapabilityPrefixes)
        {
            if (path != null && path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return prefix + "{ticket}";
        }
        return path;
    }

    private void Write(object entry)
    {
        var line = JsonSerializer.Serialize(entry) + "\n";
        lock (_gate)
            File.AppendAllText(file, line);
    }
}

/// <summary>
/// Serves a production frontend build, as a release build of the server does and where it
/// does: behind the server's own pipeline, request gates included. Ahead of it, the UI's own
/// document skipped the rules a release build applies to it — a page on another site could
/// load it into a frame, and nothing added <c>frame-ancestors</c>.
/// </summary>
public sealed class FederationTestStaticFiles : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        next(app);
        var webRoot = Environment.GetEnvironmentVariable("BAKABASE_FEDERATION_TEST_WEB_ROOT");
        if (!string.IsNullOrEmpty(webRoot))
        {
            // Reached by whatever the server's endpoints did not answer, like UseSpa's "/".
            var files = new PhysicalFileProvider(Path.GetFullPath(webRoot));
            app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = files });
            app.UseStaticFiles(new StaticFileOptions { FileProvider = files });
        }
    };
}
