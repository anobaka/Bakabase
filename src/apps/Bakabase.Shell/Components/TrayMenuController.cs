using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using Bakabase.Abstractions.Components.Gui;
using Bakabase.Shell.Resources;
using Bakabase.Shell.Windows;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Shell.Components;

/// <summary>
/// Keeps the tray menu's labels in the app's language and fills its "Switch to" submenu: this
/// device and every server it manages, each pointing the main window at that server's UI.
/// </summary>
/// <remarks>
/// The web UI has its own switcher; this submenu is the way back when the window is showing a
/// server whose (older) UI has none. It exists only where the host registers an
/// <see cref="IMainViewSwitcher"/> — the legacy thin client does not, so there the menu keeps
/// its two original items — and only while there is somewhere to switch to.
/// <para>
/// That only works where the tray is actually shown. On a Linux desktop without a
/// StatusNotifierWatcher (<see cref="TrayIconAvailability"/>) the icon never appears, and a
/// second launch does not bring the running window forward either, so the same list is put in
/// a menu bar at the top of the main window instead
/// (<see cref="AvaloniaGuiAdapter.SetServerSwitchMenu"/>) for as long as the tray is missing.
/// </para>
/// <para>
/// Keeping the list current is the awkward part, because not every platform tells us the menu
/// is about to open:
/// <list type="bullet">
/// <item>macOS raises <see cref="NativeMenu.NeedsUpdate"/> from <c>menuNeedsUpdate:</c> and
/// applies our changes synchronously before the menu appears, so the list is rebuilt exactly
/// then.</item>
/// <item>Windows builds its popup from the live <see cref="NativeMenu.Items"/> on
/// <c>WM_RBUTTONUP</c>, and Linux's DBusMenu exporter answers <c>AboutToShow</c> with "nothing
/// changed" — neither raises any managed event first. There the list is polled instead: a
/// cheap, network-free <see cref="IMainViewSwitcher.ListTargets"/> on the thread pool every
/// <see cref="PollInterval"/>, touching the menu only when the result actually differs. The
/// same poll notices the tray appearing or going away, which is what moves the list between
/// the tray and the window.</item>
/// </list>
/// </para>
/// </remarks>
internal sealed class TrayMenuController : IDisposable
{
    /// <summary>
    /// How stale the Windows/Linux list may be. Adding a server happens in the main window, so
    /// by the time the user has reached for the tray the change has long been picked up.
    /// </summary>
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Ceiling on resolving a target: starting a relay is local work, but a wedged one must not
    /// leave a click pending forever.
    /// </summary>
    private static readonly TimeSpan ResolveTimeout = TimeSpan.FromSeconds(30);

    /// <summary>Longer names are cut so one server cannot make the whole menu unreadably wide.</summary>
    private const int MaxLabelLength = 60;

    private readonly NativeMenu _menu;
    private readonly NativeMenuItem _openItem;
    private readonly NativeMenuItem _exitItem;
    private readonly AvaloniaGuiAdapter _gui;
    private readonly Func<IServiceProvider?> _services;
    private readonly Func<bool> _isTrayShown;

    private readonly NativeMenuItem _switchItem;
    private readonly NativeMenu _switchMenu = new();
    private readonly NativeMenuItemSeparator _separator = new();

    private readonly Timer? _pollTimer;

    /// <summary>What the menu currently shows; compared before touching it. UI thread writes only.</summary>
    private volatile string? _renderedSignature;

    /// <summary>The click being resolved. Only touched on the UI thread.</summary>
    private CancellationTokenSource? _switchCts;

    /// <summary>Logs a failing <see cref="IMainViewSwitcher.ListTargets"/> once, not every poll.</summary>
    private volatile bool _listingFails;

    private volatile bool _disposed;

    /// <param name="services">
    /// The host's container, or null while it is not built yet. Read on every refresh: the shell
    /// is wired before the host exists, and the switcher is an optional capability of it.
    /// </param>
    /// <param name="isTrayShown">
    /// Whether the desktop actually shows the tray icon right now. Read on every refresh, from
    /// any thread, so it must be cheap and thread-safe.
    /// </param>
    public TrayMenuController(
        NativeMenu menu,
        NativeMenuItem openItem,
        NativeMenuItem exitItem,
        AvaloniaGuiAdapter gui,
        Func<IServiceProvider?> services,
        Func<bool> isTrayShown)
    {
        _menu = menu;
        _openItem = openItem;
        _exitItem = exitItem;
        _gui = gui;
        _services = services;
        _isTrayShown = isTrayShown;
        _switchItem = new NativeMenuItem { Menu = _switchMenu };

        // Either event may be the one a platform raises; the signature check makes the second
        // one free. The submenu's own NeedsUpdate covers hovering it long after the root opened.
        _menu.NeedsUpdate += OnMenuNeedsUpdate;
        _menu.Opening += OnMenuNeedsUpdate;
        _switchMenu.NeedsUpdate += OnMenuNeedsUpdate;

        RefreshNow();

        if (!OperatingSystem.IsMacOS())
        {
            // One-shot and re-armed after each run, so a slow ListTargets can never stack up
            // overlapping callbacks.
            _pollTimer = new Timer(_ => Poll(), null, PollInterval, Timeout.InfiniteTimeSpan);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _pollTimer?.Dispose();
        CancelQuietly(_switchCts);

        _menu.NeedsUpdate -= OnMenuNeedsUpdate;
        _menu.Opening -= OnMenuNeedsUpdate;
        _switchMenu.NeedsUpdate -= OnMenuNeedsUpdate;
    }

    private void OnMenuNeedsUpdate(object? sender, EventArgs e) => RefreshNow();

    /// <summary>Synchronous refresh; UI thread only.</summary>
    private void RefreshNow()
    {
        if (_disposed)
        {
            return;
        }

        var snapshot = TakeSnapshot();
        if (snapshot != null)
        {
            Apply(snapshot);
        }
    }

    private void Poll()
    {
        try
        {
            if (_disposed)
            {
                return;
            }

            var snapshot = TakeSnapshot();
            if (snapshot != null && snapshot.Signature != _renderedSignature)
            {
                Dispatcher.UIThread.Post(() =>
                {
                    if (!_disposed)
                    {
                        Apply(snapshot);
                    }
                });
            }
        }
        catch (Exception e)
        {
            // A timer callback that throws takes the process with it.
            Serilog.Log.Warning(e, "Failed to refresh the tray menu");
        }
        finally
        {
            if (!_disposed)
            {
                try
                {
                    _pollTimer?.Change(PollInterval, Timeout.InfiniteTimeSpan);
                }
                catch (ObjectDisposedException)
                {
                    // Disposed between the check and the re-arm.
                }
            }
        }
    }

    /// <summary>
    /// What the menu should show right now. Null means "leave it as it is": the container is on
    /// its way out, or the switcher failed — neither is a reason to make the submenu flicker.
    /// Safe on any thread.
    /// </summary>
    private MenuSnapshot? TakeSnapshot()
    {
        var culture = TrayStrings.Culture;
        var trayShown = IsTrayShown();

        var switcher = TryResolveSwitcher(out var containerGone);
        if (containerGone)
        {
            return null;
        }

        if (switcher == null)
        {
            // Host not built yet, or a flavour without the capability.
            return new MenuSnapshot(culture, null, trayShown);
        }

        try
        {
            var targets = switcher.ListTargets();
            if (_listingFails)
            {
                _listingFails = false;
                Serilog.Log.Information("Tray server list is available again");
            }

            return new MenuSnapshot(culture, targets, trayShown);
        }
        catch (Exception e)
        {
            if (!_listingFails)
            {
                _listingFails = true;
                Serilog.Log.Warning(e, "Failed to list servers for the tray menu");
            }

            return null;
        }
    }

    /// <summary>
    /// Errs towards "not shown": the worst a wrong "no" costs is a redundant menu bar, while a
    /// wrong "yes" leaves the window with no way back.
    /// </summary>
    private bool IsTrayShown()
    {
        try
        {
            return _isTrayShown();
        }
        catch (Exception e)
        {
            Serilog.Log.Debug(e, "Could not tell whether the tray is shown");
            return false;
        }
    }

    private IMainViewSwitcher? TryResolveSwitcher(out bool containerGone)
    {
        containerGone = false;
        try
        {
            return _services()?.GetService<IMainViewSwitcher>();
        }
        catch (ObjectDisposedException)
        {
            containerGone = true;
        }
        catch (InvalidOperationException)
        {
            containerGone = true;
        }

        return null;
    }

    /// <summary>Brings the menu in line with <paramref name="snapshot"/>. UI thread only.</summary>
    private void Apply(MenuSnapshot snapshot)
    {
        if (snapshot.Signature == _renderedSignature)
        {
            return;
        }

        var culture = snapshot.Culture;
        _openItem.Header = TrayStrings.Open(culture);
        _exitItem.Header = TrayStrings.Exit(culture);
        _switchItem.Header = TrayStrings.SwitchTo(culture);

        // With only this device listed there is nowhere to switch to, and a one-entry submenu
        // would just be a detour on the way to "Exit".
        var targets = snapshot.Targets;
        if (targets != null && targets.Any(t => !t.IsLocal))
        {
            _switchMenu.Items.Clear();
            foreach (var target in targets)
            {
                var id = target.Id;
                var item = new NativeMenuItem(Label(target, culture));
                item.Click += (_, _) => SwitchTo(id);
                _switchMenu.Items.Add(item);
            }

            ShowSwitchItem();
        }
        else
        {
            HideSwitchItem();
            _switchMenu.Items.Clear();
        }

        // Without a tray nothing above can be reached, so the main window carries the list
        // instead. The submenu is kept current regardless, ready for a tray that turns up later.
        _gui.SetServerSwitchMenu(snapshot.TrayShown ? null : BuildWindowMenu(targets, culture));

        _renderedSignature = snapshot.Signature;
    }

    /// <summary>
    /// The same list for the main window's menu bar, or null when there is nowhere to switch
    /// to — the bar then stays hidden, exactly as the tray submenu does.
    /// </summary>
    private ServerSwitchMenu? BuildWindowMenu(IReadOnlyList<MainViewTarget>? targets, CultureInfo culture)
    {
        if (targets == null || !targets.Any(t => !t.IsLocal))
        {
            return null;
        }

        var entries = new List<ServerSwitchMenuEntry>(targets.Count);
        foreach (var target in targets)
        {
            var id = target.Id;
            entries.Add(new ServerSwitchMenuEntry(Label(target, culture), () => SwitchTo(id)));
        }

        return new ServerSwitchMenu(TrayStrings.SwitchTo(culture), entries);
    }

    /// <summary>Open, Switch to ▸, separator, Exit — the separator keeps Exit a deliberate click.</summary>
    private void ShowSwitchItem()
    {
        if (_menu.Items.Contains(_switchItem))
        {
            return;
        }

        var openIndex = _menu.Items.IndexOf(_openItem);
        var index = openIndex >= 0 ? openIndex + 1 : Math.Max(0, _menu.Items.IndexOf(_exitItem));
        _menu.Items.Insert(index, _switchItem);
        _menu.Items.Insert(index + 1, _separator);
    }

    private void HideSwitchItem()
    {
        _menu.Items.Remove(_switchItem);
        _menu.Items.Remove(_separator);
    }

    /// <summary>
    /// Resolves <paramref name="targetId"/> off the UI thread, then shows the main window on it.
    /// A later click supersedes an earlier one still resolving. Failures are logged and
    /// swallowed: a tray click has nobody to report them to, and the window simply stays put.
    /// </summary>
    private async void SwitchTo(string targetId)
    {
        using var cts = new CancellationTokenSource(ResolveTimeout);
        var superseded = _switchCts;
        _switchCts = cts;
        CancelQuietly(superseded);

        try
        {
            // Until the host has put up the main window there is nothing to navigate, and
            // resolving a managed server would start its relay for nobody.
            if (_gui.MainWindow == null || _disposed)
            {
                return;
            }

            var switcher = TryResolveSwitcher(out _);
            if (switcher == null)
            {
                return;
            }

            // Task.Run: the implementation may do its synchronous part (starting a relay) before
            // its first await, and none of that belongs on the UI thread.
            var url = await Task.Run(() => switcher.ResolveUrlAsync(targetId, cts.Token), cts.Token);

            if (cts.IsCancellationRequested || _disposed)
            {
                return;
            }

            if (string.IsNullOrEmpty(url))
            {
                Serilog.Log.Warning("Could not switch the main window to {TargetId}: unknown or unavailable",
                    targetId);
                return;
            }

            _gui.NavigateMainWebView(url, bringToFront: true);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            if (!_disposed && ReferenceEquals(_switchCts, cts))
            {
                Serilog.Log.Warning("Switching the main window to {TargetId} timed out after {Timeout}",
                    targetId, ResolveTimeout);
            }
        }
        catch (Exception e)
        {
            Serilog.Log.Warning(e, "Failed to switch the main window to {TargetId}", targetId);
        }
        finally
        {
            if (ReferenceEquals(_switchCts, cts))
            {
                _switchCts = null;
            }
        }
    }

    private static void CancelQuietly(CancellationTokenSource? cts)
    {
        try
        {
            cts?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // Its click already finished.
        }
    }

    private static string Label(MainViewTarget target, CultureInfo culture)
    {
        var name = target.Name?.Trim();
        if (string.IsNullOrEmpty(name) && !target.IsLocal)
        {
            name = target.Id;
        }

        if (name is { Length: > MaxLabelLength })
        {
            var cut = MaxLabelLength - 1;
            if (char.IsHighSurrogate(name[cut - 1]))
            {
                cut--;
            }

            name = name[..cut] + "…";
        }

        var label = target.IsLocal ? TrayStrings.ThisDevice(culture, name) : name!;

        // Every tray backend reads "_" as an access-key marker (Avalonia's MenuItem on Windows,
        // DBusMenu on Linux, and the macOS exporter strips it), so "my_nas" would lose its
        // underscore. Doubling it is the escape all three understand.
        return label.Replace("_", "__");
    }

    private sealed class MenuSnapshot(CultureInfo culture, IReadOnlyList<MainViewTarget>? targets, bool trayShown)
    {
        public CultureInfo Culture { get; } = culture;

        /// <summary>Null when there is no switcher to ask.</summary>
        public IReadOnlyList<MainViewTarget>? Targets { get; } = targets;

        /// <summary>False puts the switch list in the main window as well.</summary>
        public bool TrayShown { get; } = trayShown;

        /// <summary>Everything the rendered menus depend on, so an unchanged poll costs nothing.</summary>
        public string Signature { get; } = BuildSignature(culture, targets, trayShown);

        private static string BuildSignature(CultureInfo culture, IReadOnlyList<MainViewTarget>? targets,
            bool trayShown)
        {
            var sb = new StringBuilder(culture.Name).Append(trayShown ? "\ntray" : "\nno-tray");
            if (targets == null)
            {
                return sb.Append("\n-").ToString();
            }

            foreach (var t in targets)
            {
                sb.Append('\n').Append(t.Id).Append('\t').Append(t.Name).Append('\t').Append(t.IsLocal);
            }

            return sb.ToString();
        }
    }
}
