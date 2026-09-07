using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Bakabase.Abstractions.Components.Tasks;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Models.View;
using Bakabase.Infrastructures.Components.Configurations.App;
using Bakabase.Infrastructures.Components.Gui;
using Bakabase.Service.Components;
using Bakabase.Resources;
using Bakabase.Windows;
using Bootstrap.Components.Configuration.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Bakabase.Components;

public enum ExitTrigger
{
    /// <summary>The user closed the main window. Honours the configured close behaviour.</summary>
    WindowClose,

    /// <summary>The user picked "Exit" in the tray menu. Always means quit.</summary>
    TrayMenu
}

/// <summary>
/// Owns the whole "the user asked to leave" flow: deciding what closing means, asking when
/// that is genuinely a question, and then winding the app down in an order that does not
/// throw away in-flight work.
///
/// This deliberately replaces <c>AppHost.TryToExit</c> rather than calling it. That method
/// lives in the read-only Bakabase.Infrastructures submodule and, on the tray path, calls
/// <c>Shutdown()</c> and then *falls through* into its own close-behaviour switch — so a tray
/// exit with CloseBehavior.Prompt pops a confirmation dialog while Avalonia is already tearing
/// down. It also hides the window before the "tasks are still running" check, so cancelling
/// left the app hidden with no way back. Both entry points we control (the main window's
/// Closing handler and the tray menu item) route here instead.
/// </summary>
public sealed class ExitCoordinator(App app, AvaloniaGuiAdapter gui)
{
    /// <summary>
    /// How long a shutdown may look instantaneous before we put a window on screen. Below
    /// this a progress window would be a flash of chrome; above it, silence reads as a hang.
    /// </summary>
    private static readonly TimeSpan ProgressWindowDelay = TimeSpan.FromMilliseconds(400);

    /// <summary>
    /// How long we wait for critical work before offering "Quit now". The wait itself is
    /// unbounded on purpose — a critical task is, by definition, one whose interruption
    /// loses data, so the user gets to make that call rather than a timer.
    /// </summary>
    private static readonly TimeSpan ForceQuitOfferDelay = TimeSpan.FromSeconds(6);

    /// <summary>
    /// Ceiling on that otherwise-unbounded wait when there is no window to offer "Quit now" on
    /// — a display that never came up, or one that has gone away. Without it a headless or
    /// broken-display session could never quit.
    /// </summary>
    private static readonly TimeSpan NoUiCriticalTaskTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Ceiling on stopping the web host itself. Unlike background tasks this should always be
    /// quick; if a hosted service wedges, exiting must not become impossible.
    /// </summary>
    private static readonly TimeSpan HostStopTimeout = TimeSpan.FromSeconds(15);

    private static readonly TimeSpan TaskPollInterval = TimeSpan.FromMilliseconds(400);

    /// <summary>Number of blocking task names we bother collecting for the UI.</summary>
    private const int MaxCollectedTaskNames = 8;

    /// <summary>Serialises exit requests so a second click cannot open a second dialog.</summary>
    private readonly SemaphoreSlim _gate = new(1, 1);

    private volatile bool _shuttingDown;

    /// <summary>
    /// Entry point for every user-initiated exit. Safe to call re-entrantly: while a prompt is
    /// open or a shutdown is running, further calls are no-ops.
    /// </summary>
    public async Task RequestExitAsync(ExitTrigger trigger)
    {
        if (_shuttingDown || !await _gate.WaitAsync(0))
        {
            return;
        }

        try
        {
            await RunAsync(trigger);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task RunAsync(ExitTrigger trigger)
    {
        // The tray's "Exit" is an explicit instruction, not a window-close gesture, so it
        // never means "minimize" no matter what the setting says.
        var behavior = trigger == ExitTrigger.TrayMenu
            ? CloseBehavior.Exit
            : ResolveCloseBehavior();

        var canMinimize = app.IsTrayIconAvailable;

        // A stored "minimize" from a machine that had a tray must not hide the window on one
        // that does not — on Linux there is no way to bring it back. Fall back to asking.
        if (behavior == CloseBehavior.Minimize && !canMinimize)
        {
            behavior = CloseBehavior.Prompt;
        }

        if (behavior == CloseBehavior.Minimize)
        {
            gui.Hide();
            return;
        }

        var busy = CollectBlockingTaskNames();

        if (behavior == CloseBehavior.Prompt)
        {
            var result = await ExitConfirmationDialog.PromptAsync(
                gui.MainWindow,
                new ExitPromptOptions(ExitPromptKind.Choice, canMinimize, busy));

            if (result.Remember && result.Choice != ExitChoice.Cancel)
            {
                await RememberChoiceAsync(result.Choice);
            }

            switch (result.Choice)
            {
                case ExitChoice.Cancel:
                    return;
                case ExitChoice.Minimize:
                    gui.Hide();
                    return;
            }
        }
        else if (busy.Count > 0)
        {
            // Exiting was already decided (setting or tray), so the only open question is
            // whether to do it while work that loses data is in flight.
            var result = await ExitConfirmationDialog.PromptAsync(
                gui.MainWindow,
                new ExitPromptOptions(ExitPromptKind.ConfirmBusy, AllowMinimize: false, busy));

            if (result.Choice != ExitChoice.Exit)
            {
                return;
            }
        }

        await ShutdownAsync();
    }

    /// <summary>
    /// <see cref="CloseBehavior.Cancel"/> is a dialog result, not a preference, but the
    /// options API will happily persist it — and <c>AppHost.TryToExit</c> treats it as
    /// "do nothing", which would leave the window impossible to close. Anything that is not a
    /// real preference falls back to asking.
    /// </summary>
    private CloseBehavior ResolveCloseBehavior()
    {
        var configured = TryGetService<IBOptionsManager<AppOptions>>()?.Value?.CloseBehavior;

        return configured is CloseBehavior.Exit or CloseBehavior.Minimize
            ? configured.Value
            : CloseBehavior.Prompt;
    }

    private async Task RememberChoiceAsync(ExitChoice choice)
    {
        var manager = TryGetService<IBOptionsManager<AppOptions>>();
        if (manager == null)
        {
            return;
        }

        var behavior = choice == ExitChoice.Minimize ? CloseBehavior.Minimize : CloseBehavior.Exit;

        try
        {
            await manager.SaveAsync(o => o.CloseBehavior = behavior);
        }
        catch (Exception e)
        {
            // Failing to remember a preference must never block the exit the user asked for.
            Serilog.Log.Warning(e, "Failed to persist close behavior {Behavior}", behavior);
        }
    }

    /// <summary>
    /// Critical tasks are the ones whose interruption loses data — exactly what the old
    /// <c>CheckIfAppCanExitSafely</c> string warned about, except now we can name them. Only these
    /// justify interrupting the user with a confirmation.
    /// </summary>
    private List<string> CollectBlockingTaskNames()
    {
        var taskManager = TryGetService<BTaskManager>();
        if (taskManager == null)
        {
            return [];
        }

        try
        {
            return taskManager.GetTasksViewModel()
                .Where(t => t.Level == BTaskLevel.Critical && t.Status.IsActive())
                .OrderByDescending(t => t.Percentage ?? 0)
                .Take(MaxCollectedTaskNames)
                .Select(Describe)
                .ToList();
        }
        catch (Exception e)
        {
            Serilog.Log.Warning(e, "Failed to enumerate running tasks while exiting");
            return [];
        }
    }

    /// <summary>
    /// Everything still running, critical or not.
    ///
    /// The progress window used to list critical tasks only — so on the common shutdown, where
    /// nothing is critical, it said "finishing background tasks…" over an empty panel while ordinary
    /// work (downloads, thumbnail generation, indexing) was being wound down behind it. Those are
    /// exactly the tasks a user wants named while they wait.
    /// </summary>
    private static ActiveTasks CollectActiveTasks(BTaskManager taskManager)
    {
        var active = taskManager.GetTasksViewModel()
            .Where(t => t.Status.IsActive())
            .OrderByDescending(t => t.Level == BTaskLevel.Critical)
            .ThenByDescending(t => t.Percentage ?? 0)
            .ToList();

        return new ActiveTasks(
            active.Take(MaxCollectedTaskNames)
                .Select(t => new ExitTaskLine(Describe(t), t.Level == BTaskLevel.Critical))
                .ToList(),
            active.Count,
            active.Count(t => t.Level == BTaskLevel.Critical));
    }

    /// <param name="Lines">The ones worth naming on screen.</param>
    /// <param name="Total">How many are running in total, which is what the count must report.</param>
    /// <param name="Critical">How many of those the shutdown is actually waiting for.</param>
    private readonly record struct ActiveTasks(IReadOnlyList<ExitTaskLine> Lines, int Total, int Critical);

    private static string Describe(BTaskViewModel task) =>
        task.Percentage is > 0 and < 100 ? $"{task.Name} ({task.Percentage}%)" : task.Name;

    /// <summary>
    /// Winds the app down for real. Everything here is best-effort: whatever happens, the
    /// last statement must be the one that actually ends the process.
    /// </summary>
    private async Task ShutdownAsync()
    {
        _shuttingDown = true;

        // From here on the submodule's ApplicationStopping -> IGuiAdapter.Shutdown callback
        // must not end the Avalonia lifetime out from under us: StopAsync triggers it, and
        // letting it through would kill the process mid-flush.
        gui.BeginDeferredShutdown();

        try
        {
            await RunWindDownAsync();
        }
        catch (Exception e)
        {
            Serilog.Log.Error(e, "Error while shutting down gracefully");
        }
        finally
        {
            try
            {
                Serilog.Log.CloseAndFlush();
            }
            catch
            {
                // Nothing left to log to.
            }

            // Unconditional. The latch set by BeginDeferredShutdown suppresses every other
            // route out of the process, so failing to clear it here would leave the app
            // impossible to quit — a far worse outcome than whatever went wrong above.
            gui.CompleteDeferredShutdown();
        }
    }

    private async Task RunWindDownAsync()
    {
        app.SetTrayIconVisible(false);
        gui.Hide();

        using var forceQuit = new CancellationTokenSource();
        ExitProgressWindow? progress = null;

        // The window only appears once the shutdown has proven slow, by which time the phase has
        // usually already moved on. Remembering the latest report lets the window open showing where
        // the shutdown actually is, rather than the heading it was built with.
        var latestPhase = ExitStrings.ClosingStoppingTasks;
        var latestTasks = default(ActiveTasks);

        try
        {
            var windDown = WindDownAsync(
                phase =>
                {
                    latestPhase = phase;
                    progress?.SetPhase(phase);
                },
                tasks =>
                {
                    latestTasks = tasks;
                    progress?.SetRemainingTasks(tasks.Lines, tasks.Total);
                },
                () =>
                {
                    progress?.ShowForceQuit();
                    return progress != null;
                },
                forceQuit.Token);

            // Only put a window on screen if the shutdown is slow enough to need one.
            if (await Task.WhenAny(windDown, Task.Delay(ProgressWindowDelay)) != windDown)
            {
                progress = TryShowProgressWindow(forceQuit);
                progress?.SetPhase(latestPhase);
                progress?.SetRemainingTasks(latestTasks.Lines ?? [], latestTasks.Total);
            }

            await windDown;
        }
        catch (OperationCanceledException)
        {
            // "Quit now" — the user accepted the consequences.
        }
        finally
        {
            if (progress != null)
            {
                try
                {
                    progress.AllowClose();
                    progress.Close();
                }
                catch (Exception e)
                {
                    Serilog.Log.Warning(e, "Failed to close the shutdown progress window");
                }
            }
        }
    }

    /// <summary>
    /// A display that has gone away (headless session, X11 dropped, compositor restart) must
    /// not abort the wind-down — the user just does not get to watch it or force it along.
    /// </summary>
    private static ExitProgressWindow? TryShowProgressWindow(CancellationTokenSource forceQuit)
    {
        try
        {
            var window = new ExitProgressWindow();
            window.ForceQuitRequested += () =>
            {
                // ReSharper disable once AccessToDisposedClosure
                try { forceQuit.Cancel(); } catch (ObjectDisposedException) { /* already gone */ }
            };
            window.Show();
            return window;
        }
        catch (Exception e)
        {
            Serilog.Log.Warning(e, "Could not show the shutdown progress window");
            return null;
        }
    }

    /// <summary>
    /// Stops background work, then the host, reporting progress as it goes.
    /// </summary>
    /// <param name="offerForceQuit">
    /// Called once the wait has gone on long enough to deserve an escape hatch; returns
    /// whether a window was actually there to show it on.
    /// </param>
    private async Task WindDownAsync(
        Action<string> setPhase,
        Action<ActiveTasks> setTasks,
        Func<bool> offerForceQuit,
        CancellationToken ct)
    {
        var host = app.Host?.Host;
        if (host == null)
        {
            // Quitting before the host ever came up — there is nothing to wind down.
            return;
        }

        var taskManager = TryGetService<BTaskManager>();
        if (taskManager != null)
        {
            setPhase(ExitStrings.ClosingStoppingTasks);
            await WaitForCriticalTasksAsync(taskManager, setTasks, offerForceQuit, ct);
        }

        ct.ThrowIfCancellationRequested();

        setPhase(ExitStrings.ClosingSavingData);

        // StopAsync runs the hosted services' shutdown; disposing the host afterwards is what
        // drains the DI container, and with it BTaskManager.DisposeAsync (which stops
        // non-critical tasks) and every IDisposable service holding a file or DB handle.
        // Today neither happens at all: desktop.Shutdown() ends Main while Host.RunAsync() is
        // still parked on a background task, so the process simply dies.
        using var stopCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        stopCts.CancelAfter(HostStopTimeout);

        // This is where the non-critical tasks are actually stopped, and it can take the whole
        // fifteen seconds. Keep naming what is still running instead of clearing the list and
        // leaving the user with a bare spinner for the longest part of the shutdown.
        using var reporting = StartReportingActiveTasks(taskManager, setTasks);

        try
        {
            await host.StopAsync(stopCts.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            Serilog.Log.Warning("Host did not stop within {Timeout}; exiting anyway", HostStopTimeout);
        }

        await DisposeHostAsync(host, ct);

        reporting.Stop();
        setTasks(default);
        setPhase(ExitStrings.ClosingDone);
    }

    /// <summary>
    /// Polls the task list in the background for as long as the returned handle lives. Every read is
    /// best-effort: the container is being torn down underneath it, so a failure means "stop
    /// reporting", never "stop shutting down".
    /// </summary>
    private static ActiveTaskReporter StartReportingActiveTasks(
        BTaskManager? taskManager, Action<ActiveTasks> setTasks)
    {
        var reporter = new ActiveTaskReporter();

        if (taskManager == null)
        {
            return reporter;
        }

        _ = Task.Run(async () =>
        {
            while (!reporter.Token.IsCancellationRequested)
            {
                try
                {
                    setTasks(CollectActiveTasks(taskManager));
                }
                catch (Exception)
                {
                    // The manager is gone (or going). Nothing left worth reporting.
                    return;
                }

                try
                {
                    await Task.Delay(TaskPollInterval, reporter.Token);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        });

        return reporter;
    }

    private sealed class ActiveTaskReporter : IDisposable
    {
        private readonly CancellationTokenSource _cts = new();

        public CancellationToken Token => _cts.Token;

        public void Stop()
        {
            try
            {
                _cts.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // Already stopped.
            }
        }

        public void Dispose()
        {
            Stop();
            _cts.Dispose();
        }
    }

    /// <summary>
    /// Disposal is bounded because <c>BTaskManager.DisposeAsync</c> waits for critical tasks
    /// without a timeout of its own. We have already given the user a say above; if something
    /// is still wedged here we stop waiting rather than making the app unquittable.
    /// </summary>
    private static async Task DisposeHostAsync(IHost host, CancellationToken ct)
    {
        var dispose = Task.Run(async () =>
        {
            if (host is IAsyncDisposable asyncDisposable)
            {
                await asyncDisposable.DisposeAsync();
            }
            else
            {
                host.Dispose();
            }
        }, CancellationToken.None);

        var completed = await Task.WhenAny(dispose, Task.Delay(HostStopTimeout, ct));
        if (completed != dispose)
        {
            Serilog.Log.Warning("Host disposal did not finish within {Timeout}; exiting anyway", HostStopTimeout);
            return;
        }

        // Surface a disposal fault in the log rather than swallowing it silently.
        await dispose;
    }

    private static async Task WaitForCriticalTasksAsync(
        BTaskManager taskManager,
        Action<ActiveTasks> setTasks,
        Func<bool> offerForceQuit,
        CancellationToken ct)
    {
        var waited = TimeSpan.Zero;
        var forceQuitOffered = false;

        while (!ct.IsCancellationRequested)
        {
            ActiveTasks active;
            try
            {
                // Report everything that is running, but keep waiting only for the critical ones —
                // those are the ones whose interruption loses data.
                active = CollectActiveTasks(taskManager);
            }
            catch (Exception e)
            {
                Serilog.Log.Warning(e, "Failed to poll running tasks while exiting");
                return;
            }

            setTasks(active);

            var remaining = active.Critical;

            if (remaining == 0)
            {
                return;
            }

            if (!forceQuitOffered && waited >= ForceQuitOfferDelay)
            {
                forceQuitOffered = offerForceQuit();

                if (!forceQuitOffered && waited >= NoUiCriticalTaskTimeout)
                {
                    // No window came up, so there is nobody to ask and no button to press.
                    // Waiting on unbounded critical work would leave a process that cannot be
                    // quit; a bounded grace period is the lesser evil.
                    Serilog.Log.Warning(
                        "Proceeding with shutdown after {Timeout} with {Count} critical task(s) still active and no UI to ask",
                        NoUiCriticalTaskTimeout, remaining);
                    return;
                }
            }

            await Task.Delay(TaskPollInterval, ct);
            waited += TaskPollInterval;
        }

        ct.ThrowIfCancellationRequested();
    }

    /// <summary>
    /// Service lookup that tolerates a half-built or already-disposed container — every caller
    /// here runs on an exit path, where throwing is worse than degrading.
    /// </summary>
    private T? TryGetService<T>() where T : class
    {
        try
        {
            return app.Host?.Host?.Services.GetService<T>();
        }
        catch (ObjectDisposedException)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }
}
