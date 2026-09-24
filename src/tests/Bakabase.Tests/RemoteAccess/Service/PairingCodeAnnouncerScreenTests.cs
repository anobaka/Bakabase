using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Models.Domain.Options;
using Bakabase.Infrastructures.Components.Gui;
using Bakabase.Modules.RemoteAccess.Abstractions.Components;
using Bakabase.Modules.RemoteAccess.Abstractions.Models;
using Bakabase.Modules.RemoteAccess.Abstractions.Services;
using Bakabase.Modules.RemoteAccess.Components.Pairing;
using Bakabase.Modules.RemoteAccess.Services;
using Bakabase.Service.Components;
using Bakabase.Service.Components.RemoteAccess;
using Bakabase.TestKit.Implementations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Tests.RemoteAccess.Service;

/// <summary>
/// A pairing code goes to the log only where nobody has a screen to read one from: the
/// headless server. The desktop app shows and issues codes in its own window.
/// </summary>
/// <remarks>
/// Every case here is the lockout that makes a headless server print a code — remote access
/// on, pairing required, nobody paired — so the only thing that differs is the host.
/// </remarks>
[TestClass]
public class PairingCodeAnnouncerScreenTests
{
    private string _root = null!;
    private RemoteAccessService _access = null!;
    private RemoteDeviceService _devices = null!;
    private DataDirectory _directory = null!;

    private sealed class DataDirectory(string path) : IRemoteAccessDataDirectory
    {
        public string Path => path;

        public string Ensure()
        {
            Ensured = true;
            return Directory.CreateDirectory(path).FullName;
        }

        public bool Ensured { get; private set; }
    }

    private sealed class NoAddresses : IListeningAddressProvider
    {
        public IReadOnlyList<string> GetListeningAddresses() => [];
    }

    /// <summary>What the desktop app hands the Service: a real window's adapter.</summary>
    private sealed class DesktopGuiAdapter : GuiAdapter
    {
        public override void InvokeInGuiContext(Action action) => action();
        public override T InvokeInGuiContext<T>(Func<T> func) => func();

        public override void ShowFatalErrorWindow(string message, string title = "Fatal Error")
        {
        }

        public override void ShowInitializationWindow(string processName, string? detail = null, double? fraction = null)
        {
        }

        public override void DestroyInitializationWindow()
        {
        }

        public override void ShowMainWebView(string url, string title, Func<Task> onClosing)
        {
        }

        public override void SetMainWindowTitle(string title)
        {
        }

        public override bool MainWebViewVisible => true;

        public override void Shutdown()
        {
        }

        public override void Hide()
        {
        }

        public override void Show()
        {
        }

        public override void ShowConfirmationDialogOnFirstTimeExiting(Func<CloseBehavior, bool, Task> onClosed)
        {
        }

        public override bool ShowConfirmDialog(string message, string caption) => true;

        public override void ChangeUiTheme(UiTheme theme)
        {
        }

        public override byte[]? GetIcon(IconType type, string path) => null;

        public override IWebViewSession CreateWebViewSession(WebViewSessionOptions options) =>
            CancelledWebViewSession.Instance;
    }

    [TestInitialize]
    public async Task Setup()
    {
        _root = Path.Combine(Path.GetTempPath(), "bakabase-announcer-screen", Guid.NewGuid().ToString("N"));
        _access = new RemoteAccessService(
            new TestBOptionsManager<RemoteAccessOptions>(new RemoteAccessOptions()),
            new RemoteAccessDefaults(RemoteAccessMode.Disabled),
            new RemoteAccessHostInfo("1.2.3-test"),
            new NoAddresses(),
            NullLogger<RemoteAccessService>.Instance);
        _directory = new DataDirectory(Path.Combine(_root, "remote-access"));
        _devices = new RemoteDeviceService(new RemoteDeviceStore(_directory));

        // Locked out.
        await _access.SetModeAsync(RemoteAccessMode.Enabled);
        await _access.SetRequirePairingAsync(true);
    }

    [TestCleanup]
    public void Cleanup()
    {
        try
        {
            Directory.Delete(_root, true);
        }
        catch (Exception e) when (e is IOException or DirectoryNotFoundException)
        {
        }
    }

    private FirstDevicePairingCodeAnnouncer Announcer(IGuiAdapter? gui) =>
        new(_access, _devices, NullLogger<FirstDevicePairingCodeAnnouncer>.Instance, gui);

    [TestMethod]
    public async Task The_desktop_app_never_prints_a_code()
    {
        var announcer = Announcer(new DesktopGuiAdapter());

        Assert.IsTrue(announcer.HasScreen);
        Assert.IsFalse(await announcer.AnnounceIfLockedOutAsync());

        // No code was issued behind the window's back either — issuing one would also
        // void a code the user is in the middle of typing from the settings page.
        Assert.IsNull(_devices.GetPairingCodeStatus());
        Assert.IsFalse(_directory.Ensured, "nothing written");
    }

    [TestMethod]
    public async Task The_desktop_apps_background_check_does_not_even_start()
    {
        using var announcer = Announcer(new DesktopGuiAdapter());

        await announcer.StartAsync(CancellationToken.None);
        var ran = announcer.ExecuteTask;

        // Well short of the first re-check, so a loop would still be waiting on its timer.
        Assert.IsNotNull(ran);
        Assert.AreSame(ran, await Task.WhenAny(ran, Task.Delay(FirstDevicePairingCodeAnnouncer.CheckInterval / 3)),
            "returns rather than polling every 30 seconds");
        Assert.IsTrue(ran.IsCompletedSuccessfully);
        Assert.IsNull(_devices.GetPairingCodeStatus());
        await announcer.StopAsync(CancellationToken.None);
    }

    [TestMethod]
    public async Task A_headless_server_still_prints_one()
    {
        // What Bakabase.Service's own entry hands the host — the container, and a
        // development build started from that entry.
        var announcer = Announcer(new NullGuiAdapter());

        Assert.IsFalse(announcer.HasScreen);
        Assert.IsTrue(await announcer.AnnounceIfLockedOutAsync());
        Assert.IsNotNull(_devices.GetPairingCodeStatus());
    }

    [TestMethod]
    public async Task A_host_that_registers_no_gui_counts_as_headless()
    {
        var announcer = Announcer(null);

        Assert.IsFalse(announcer.HasScreen);
        Assert.IsTrue(await announcer.AnnounceIfLockedOutAsync());
    }

    [TestMethod]
    public void The_container_hands_the_announcer_the_hosts_gui_adapter()
    {
        // Registered the way AppHost registers the adapter and BakabaseStartup the
        // announcer, so the constructor's optional parameter is known to be filled in.
        FirstDevicePairingCodeAnnouncer Resolve(IGuiAdapter gui)
        {
            var services = new ServiceCollection();
            services.AddTransient(_ => gui);
            services.AddSingleton<IRemoteAccessService>(_access);
            services.AddSingleton<IRemoteDeviceService>(_devices);
            services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
            services.AddHostedService<FirstDevicePairingCodeAnnouncer>();
            return services.BuildServiceProvider().GetServices<IHostedService>()
                .OfType<FirstDevicePairingCodeAnnouncer>().Single();
        }

        Assert.IsTrue(Resolve(new DesktopGuiAdapter()).HasScreen);
        Assert.IsFalse(Resolve(new NullGuiAdapter()).HasScreen);
    }
}
