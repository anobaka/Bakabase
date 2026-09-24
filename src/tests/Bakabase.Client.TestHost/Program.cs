using Bakabase.Client.Remoting.Components;
using Bakabase.Infrastructures.Components.App;
using Bakabase.Infrastructures.Components.Gui;
using Bakabase.Infrastructures.Components.SystemService;
using Bakabase.Modules.RemoteAccess.Components.Discovery.Clients;

// A real legacy ClientHost/ClientStartup listener for browser migration tests. Never shipped.
// The shell is inert: this exercises HTTP forwarding/pairing/export, not a native installer or GUI.
if (args.Length != 2 || !int.TryParse(args[0], out var port) || port is < 1024 or > 65535 ||
    !Path.IsPathFullyQualified(args[1]))
    throw new ArgumentException("Usage: Bakabase.Client.TestHost <port> <absolute-empty-data-directory>");
var directory = args[1];
if (Directory.Exists(directory) && Directory.EnumerateFileSystemEntries(directory).Any())
    throw new ArgumentException("The client fixture requires a new, empty data directory.");
Environment.SetEnvironmentVariable("BAKABASE_CLIENT_DATA_DIR", directory);
FixtureAnalytics.TurnOff();
AppDataAnchor.Use(AppDataPathProfile.Client);
await new ClientFixtureHost(port, directory).Start([]);

sealed class ClientFixtureHost(int port, string directory) : ClientHost(new TestGui(), new TestSystem())
{
    protected override string? SingleInstanceId => null;
    protected override IReadOnlyList<int>? OverrideListeningPorts() => [port];
    protected override IHostBuilder CreateHostBuilder(params string[] args) => base.CreateHostBuilder(args)
        .ConfigureServices(services => services.AddSingleton<IServerDiscovery, NoDiscovery>());
    protected override async Task ExecuteCustomProgress(IServiceProvider services)
    {
        await base.ExecuteCustomProgress(services);
        if (AppService.DefaultAppDataDirectory != directory)
            throw new InvalidOperationException("The legacy client adopted another product's data directory.");
        FixtureAnalytics.Verify(services.GetRequiredService<IConfiguration>());
        services.GetRequiredService<AppService>().NotAcceptTerms = false;
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "ready"), port.ToString());
        Console.WriteLine($"CLIENT_TEST_READY {port}");
    }
}

sealed class NoDiscovery : IServerDiscovery
{
    public Task<IReadOnlyList<DiscoveredServer>> DiscoverAsync(TimeSpan timeout, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<DiscoveredServer>>([]);
}

sealed class TestSystem : ISystemService
{
    public UiTheme UiTheme => UiTheme.FollowSystem;
    public string Language => "en-US";
    public event Func<UiTheme, Task>? OnUiThemeChange { add { } remove { } }
}

sealed class TestGui : IGuiAdapter
{
    public bool MainWebViewVisible => true;
    public void ShowFatalErrorWindow(string message, string title = "Fatal Error") => Console.Error.WriteLine($"{title}: {message}");
    public void ShowInitializationWindow(string processName, string? detail = null, double? fraction = null) { }
    public void DestroyInitializationWindow() { }
    public void ShowMainWebView(string url, string title, Func<Task> onClosing) { }
    public void SetMainWindowTitle(string title) { }
    public void Shutdown() { }
    public void Hide() { }
    public void Show() { }
    public void ShowConfirmationDialogOnFirstTimeExiting(Func<CloseBehavior, bool, Task> onClosed) { }
    public bool ShowConfirmDialog(string message, string caption) => false;
    public void ChangeUiTheme(UiTheme theme) { }
    public byte[]? GetIcon(IconType type, string? path) => null;
    public IWebViewSession CreateWebViewSession(WebViewSessionOptions options) => CancelledWebViewSession.Instance;
}
