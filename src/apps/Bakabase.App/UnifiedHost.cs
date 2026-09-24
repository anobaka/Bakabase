using Bakabase.Infrastructures.Components.Gui;
using Bakabase.Infrastructures.Components.SystemService;
using Bakabase.Remoting.Components.Console;
using Bakabase.Service.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Bakabase.App;

/// <summary>
/// The desktop app's host: this device's own server, plus what lets the same window show
/// and manage other servers.
/// </summary>
/// <remarks>
/// <para>
/// A subclass rather than a change to <see cref="BakabaseHost"/>, because that host is also
/// what a headless server runs, and the relays belong only where there is a window and a
/// user sitting at it. Adding them here keeps <c>Bakabase.Service</c> free of any reference
/// to the relay: it reaches all of this only through the contracts it resolves optionally.
/// </para>
/// <para>
/// Appended after the server's own registrations, so nothing here can displace one of
/// them — the console registers only types the server does not.
/// </para>
/// </remarks>
internal sealed class UnifiedHost(IGuiAdapter guiAdapter, ISystemService systemService)
    : BakabaseHost(guiAdapter, systemService)
{
    protected override IHostBuilder CreateHostBuilder(params string[] args) =>
        base.CreateHostBuilder(args).ConfigureServices(services => services.AddRemoteConsole());

    /// <summary>
    /// Also records where the main window is about to open, which is the one fact nothing
    /// else can reconstruct: "back to this device" has to land on exactly this origin, or
    /// the browser treats it as a different site and every setting it keeps looks lost.
    /// </summary>
    protected override string OverrideFeAddress(string feAddress)
    {
        var address = base.OverrideFeAddress(feAddress);

        try
        {
            Host.Services.GetService<RemoteConsoleLocalOrigin>()?.Set(address);
        }
        catch (ObjectDisposedException)
        {
            // Shutting down before the window ever opened; there is nowhere to go back to.
        }

        return address;
    }
}
