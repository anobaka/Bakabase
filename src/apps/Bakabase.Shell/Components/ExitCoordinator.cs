using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using Bakabase.Abstractions.Components.Tasks;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Models.View;
using Bakabase.Infrastructures.Components.Configurations.App;
using Bakabase.Infrastructures.Components.Gui;
using Bakabase.Shell.Resources;
using Bakabase.Shell.Windows;
using Bootstrap.Components.Configuration.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Bakabase.Shell.Components;

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
    ///
    /// Only applies when there is nothing to report. If work is still in flight the window goes up
    /// immediately: that is precisely the case where the user has just been told tasks are running
    /// and would otherwise be left staring at an empty desktop wondering whether anything happened.
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
    /// How long a task that has been *asked* to stop gets to actually stop before the shutdown
    /// moves on without it.
    ///
    /// Unlike the critical wait this is bounded: the task has already been told to go, so what is
    /// left is only letting it reach its next checkpoint and release the DB and file handles it
    /// holds before the container that owns them is disposed. A body that ignores cancellation
    /// must not be able to make the app unquittable. Matches
    /// <c>BTaskHandler.StopGraceTimeout</c>, which is where such a body already gets logged.
    /// </summary>
    private static readonly TimeSpan CancelledTaskDrainTimeout = TimeSpan.FromSeconds(10);

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
    private Action? _reservedUpdateLaunch;

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
            // A restart reservation can land between our first check and acquiring the
            // gate. Let its post-response path own the shutdown in that case.
            if (_reservedUpdateLaunch != null)
            {
                return;
            }

            await RunAsync(trigger);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Setup confirmation already authorizes this stop. Skip another exit-choice dialog,
    /// but keep the same task drain, database flush and host disposal used by a normal exit.
    /// The supervising process waits for actual process exit before touching the data.
    /// </summary>
    public async Task RequestMaintenanceExitAsync()
    {
        if (_shuttingDown) return;
        // A close-choice dialog may already own the gate. This is a durable maintenance
        // request, so wait for that dialog instead of silently dropping the only stop signal.
        await _gate.WaitAsync();
        try
        {
            if (!_shuttingDown && _reservedUpdateLaunch == null) await ShutdownAsync();
        }
        finally { _gate.Release(); }
    }

    /// <summary>
    /// Updates are an explicit request to quit. Keep the progress window up while tasks and
    /// the host stop, then start Velopack after the graceful wind-down.
    /// </summary>
    public bool TryReserveUpdateRestart(Action launchUpdater)
    {
        ArgumentNullException.ThrowIfNull(launchUpdater);

        if (_shuttingDown || !_gate.Wait(0))
        {
            return false;
        }

        try
        {
            if (_shuttingDown || _reservedUpdateLaunch != null)
            {
                return false;
            }

            _reservedUpdateLaunch = launchUpdater;
            return true;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task BeginReservedUpdateRestartAsync()
    {
        await _gate.WaitAsync();
        try
        {
            if (!_shuttingDown && _reservedUpdateLaunch is { } launchUpdater)
            {
                await ShutdownAsync(launchUpdater);
            }
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

    /// <summary>
    /// Everything running right now — or nothing, if the task manager cannot be reached. Used
    /// before the wind-down starts, to decide whether the progress window is worth showing at all.
    /// </summary>
    private ActiveTasks TryCollectActiveTasks()
    {
        var taskManager = TryGetService<BTaskManager>();
        if (taskManager == null)
        {
            return default;
        }

        try
        {
            return CollectActiveTasks(taskManager);
        }
        catch (Exception e)
        {
            Serilog.Log.Warning(e, "Failed to enumerate running tasks while exiting");
            return default;
        }
    }

    /// <param name="Lines">The ones worth naming on screen.</param>
    /// <param name="Total">How many are running in total, which is what the count must report.</param>
    /// <param name="Critical">How many of those the shutdown is actually waiting for.</param>
    private readonly record struct ActiveTasks(IReadOnlyList<ExitTaskLine> Lines, int Total, int Critical);

    private static string Describe(BTaskViewModel task) =>
        task.Percentage is > 0 and < 100 ? $"{task.Name} ({task.Percentage}%)" : task.Name;

    /// <summary>
    /// Winds the app down for real. Everything here is best-effort. If the updater cannot
    /// launch after the host stops, a native error window remains so the user can relaunch.
    /// </summary>
    private async Task ShutdownAsync(Action? launchUpdater = null)
    {
        _shuttingDown = true;
        var updaterLaunchFailed = false;

        // From here on the submodule's ApplicationStopping -> IGuiAdapter.Shutdown callback
        // must not end the Avalonia lifetime out from under us: StopAsync triggers it, and
        // letting it through would kill the process mid-flush.
        gui.BeginDeferredShutdown();

        try
        {
            await RunWindDownAsync(launchUpdater != null);
        }
        catch (Exception e)
        {
            Serilog.Log.Error(e, "Error while shutting down gracefully");
        }
        finally
        {
            if (launchUpdater != null)
            {
                try
                {
                    // The wind-down has completed or made its best effort. Velopack now waits
                    // for our process to exit before replacing files and relaunching.
                    launchUpdater();
                }
                catch (Exception e)
                {
                    updaterLaunchFailed = true;
                    Serilog.Log.Error(e, "Could not launch Velopack after shutdown");
                    try
                    {
                        gui.ShowFatalErrorWindow(
                            $"{ExitStrings.UpdateFailedBody}\n\n{e}",
                            ExitStrings.UpdateFailedTitle);
                    }
                    catch
                    {
                        // If even the error window cannot open, finish exiting rather than
                        // leave an invisible process with a stopped host.
                        updaterLaunchFailed = false;
                    }
                }
            }

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
            if (!updaterLaunchFailed)
            {
                gui.CompleteDeferredShutdown();
            }
        }
    }

    private async Task RunWindDownAsync(bool forUpdate = false)
    {
        using var forceQuit = new CancellationTokenSource();
        var progress = new ExitProgress(() =>
        {
            // ReSharper disable once AccessToDisposedClosure
            try { forceQuit.Cancel(); } catch (ObjectDisposedException) { /* already gone */ }
        }, forUpdate);

        // Whether anything is running decides how the window should be timed, and the answer is
        // already available here — waiting ProgressWindowDelay to find out only delays the one
        // case that needs the window.
        progress.SetTasks(TryCollectActiveTasks());

        if (forUpdate || progress.HasWorkToReport)
        {
            progress.TryShow();
        }

        // After the window, not before. Both of these are shell calls that can block the UI
        // thread — deregistering the tray icon talks to Explorer — and anything that happens
        // first is time the user spends looking at a desktop with nothing on it.
        app.SetTrayIconVisible(false);
        gui.Hide();

        try
        {
            // On the thread pool, deliberately. An async method runs inline on its caller until
            // its first pending await, and IHost.StopAsync opens by firing ApplicationStopping —
            // whose callbacks run synchronously on that thread, SignalR's connection manager
            // (tearing down the WebView's hub socket) among them. Run inline on the UI thread,
            // that froze the dispatcher for seconds at a time, so the progress window could not be
            // put on screen until the work it exists to narrate was already over. Everything below
            // reports through ExitProgress, which marshals to the UI thread itself.
            var windDown = Task.Run(() => WindDownAsync(
                progress.SetPhase,
                progress.SetTasks,
                progress.OfferForceQuit,
                forceQuit.Token));

            // Nothing to report, so only put a window on screen if the shutdown proves slow.
            if (!progress.ShowAttempted &&
                await Task.WhenAny(windDown, Task.Delay(ProgressWindowDelay)) != windDown)
            {
                progress.TryShow();
            }

            await windDown;
        }
        catch (OperationCanceledException)
        {
            // "Quit now" — the user accepted the consequences.
        }
        finally
        {
            progress.Close();
        }
    }

    /// <summary>
    /// Owns the shutdown progress window and the last thing the wind-down had to say.
    ///
    /// The two live on different threads — the wind-down reports from the thread pool, the window
    /// may only be created and closed on the UI thread, and either can come first — so what has
    /// been reported is kept here and replayed into the window if and when one appears.
    /// </summary>
    private sealed class ExitProgress(Action onForceQuitRequested, bool forUpdate)
    {
        private readonly object _lock = new();
        private ExitProgressWindow? _window;
        private bool _showAttempted;
        private string _phase = ExitStrings.ClosingStoppingTasks;
        private ActiveTasks _tasks;
        private bool _forceQuitOffered;

        /// <summary>Whether a window has been asked for — successfully or not.</summary>
        public bool ShowAttempted
        {
            get
            {
                lock (_lock)
                {
                    return _showAttempted;
                }
            }
        }

        public bool HasWorkToReport
        {
            get
            {
                lock (_lock)
                {
                    return _tasks.Total > 0;
                }
            }
        }

        /// <summary>
        /// Creates the window and replays whatever has been reported so far. Must be called on the
        /// UI thread, and only ever tries once: a display that has gone away (headless session,
        /// X11 dropped, compositor restart) must not abort the wind-down — the user just does not
        /// get to watch it or force it along.
        /// </summary>
        public void TryShow()
        {
            lock (_lock)
            {
                if (_showAttempted)
                {
                    return;
                }

                _showAttempted = true;
            }

            ExitProgressWindow window;

            try
            {
                window = new ExitProgressWindow(forUpdate);
                window.ForceQuitRequested += onForceQuitRequested;
                window.Show();
            }
            catch (Exception e)
            {
                Serilog.Log.Warning(e, "Could not show the shutdown progress window");
                return;
            }

            // Adopt and replay under the same lock: a report landing from the wind-down in between
            // would reach the window first and then be overwritten by this (older) replay.
            lock (_lock)
            {
                _window = window;

                window.SetPhase(_phase);
                window.SetRemainingTasks(_tasks.Lines ?? [], _tasks.Total);

                if (_forceQuitOffered)
                {
                    window.ShowForceQuit();
                }
            }
        }

        // Recording and forwarding stay under the lock throughout, so what the window is told can
        // never get out of order with what is remembered for a later replay. The window's own
        // setters only marshal to the UI thread, so nothing here waits on it.

        public void SetPhase(string phase)
        {
            lock (_lock)
            {
                _phase = phase;
                _window?.SetPhase(phase);
            }
        }

        public void SetTasks(ActiveTasks tasks)
        {
            lock (_lock)
            {
                _tasks = tasks;
                _window?.SetRemainingTasks(tasks.Lines ?? [], tasks.Total);
            }
        }

        /// <returns>Whether there is a window to offer it on.</returns>
        public bool OfferForceQuit()
        {
            lock (_lock)
            {
                _forceQuitOffered = true;
                _window?.ShowForceQuit();
                return _window != null;
            }
        }

        /// <summary>Takes the window down. A no-op when none was ever shown.</summary>
        public void Close()
        {
            ExitProgressWindow? window;

            lock (_lock)
            {
                window = _window;
                _window = null;
            }

            if (window == null)
            {
                return;
            }

            void CloseIt()
            {
                window.AllowClose();
                window.Close();
            }

            try
            {
                // Synchronously, not posted: the caller ends the Avalonia lifetime straight after
                // this, and a window that has not been told closing is allowed cancels its own
                // Closing event — which would cancel the app's shutdown along with it. Bounded,
                // because a dispatcher that has already gone must not make the app unquittable.
                if (Dispatcher.UIThread.CheckAccess())
                {
                    CloseIt();
                }
                else
                {
                    Dispatcher.UIThread.Invoke(CloseIt, DispatcherPriority.Send,
                        CancellationToken.None, TimeSpan.FromSeconds(2));
                }
            }
            catch (Exception e)
            {
                Serilog.Log.Warning(e, "Failed to close the shutdown progress window");
            }
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

            // Ask first, wait second. Non-critical tasks used to be cancelled only by
            // BTaskManager.DisposeAsync — which runs *after* the host has stopped, so for the
            // whole of that wait (up to HostStopTimeout) nothing had told them to stop and the
            // window showed them still working: "Preparing resource data (17%)" ticking over to
            // 18% ten seconds later, as if the shutdown were waiting for the task to finish.
            try
            {
                await taskManager.PrepareForShutdown();
            }
            catch (Exception e)
            {
                // Best-effort: whatever refuses to be asked is still cancelled by disposal.
                Serilog.Log.Warning(e, "Failed to ask background tasks to stop while exiting");
            }

            // Then let them finish stopping before anything they depend on is taken away. Stopping
            // the host is what disposes the DB contexts, HTTP clients and file handles a task body
            // is still holding between its last checkpoint and its next one.
            await WaitForTasksToWindDownAsync(taskManager, setTasks, offerForceQuit, ct);
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

        // Background work has normally gone quiet by now, but this can still take the whole fifteen
        // seconds, and anything that outstayed its grace above is still listed. Keep naming what is
        // running rather than clearing the list and leaving a bare spinner over the longest part of
        // the shutdown.
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

    /// <summary>
    /// Waits for background work to actually go quiet, having just asked all of it to stop.
    ///
    /// Two waits in one loop, because the two kinds of task mean different things here:
    /// <list type="bullet">
    /// <item>Tasks that were cancelled were told to stop, so all that is left is letting them
    /// reach their next checkpoint and release their DB and file handles before the container
    /// holding those is disposed. Bounded by <see cref="CancelledTaskDrainTimeout"/>.</item>
    /// <item>Critical tasks were deliberately <em>not</em> asked to stop — interrupting them is
    /// what loses data — so that wait stays unbounded and the user gets "Quit now" rather than a
    /// timer deciding for them.</item>
    /// </list>
    ///
    /// Waiting here rather than letting the cancelled tasks unwind alongside the host stop is the
    /// point: the host stop is what tears down the services they are still using.
    /// </summary>
    private static async Task WaitForTasksToWindDownAsync(
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
                active = CollectActiveTasks(taskManager);
            }
            catch (Exception e)
            {
                Serilog.Log.Warning(e, "Failed to poll running tasks while exiting");
                return;
            }

            setTasks(active);

            if (active.Total == 0)
            {
                return;
            }

            // Nothing left but tasks that have had their grace and not taken it. Whatever they are
            // doing between checkpoints, it is not worth holding the exit open for.
            if (active.Critical == 0 && waited >= CancelledTaskDrainTimeout)
            {
                Serilog.Log.Warning(
                    "Proceeding with shutdown after {Timeout} with {Count} task(s) still winding down",
                    CancelledTaskDrainTimeout, active.Total);
                return;
            }

            if (!forceQuitOffered && waited >= ForceQuitOfferDelay)
            {
                forceQuitOffered = offerForceQuit();

                if (!forceQuitOffered && active.Critical > 0 && waited >= NoUiCriticalTaskTimeout)
                {
                    // No window came up, so there is nobody to ask and no button to press.
                    // Waiting on unbounded critical work would leave a process that cannot be
                    // quit; a bounded grace period is the lesser evil.
                    Serilog.Log.Warning(
                        "Proceeding with shutdown after {Timeout} with {Count} critical task(s) still active and no UI to ask",
                        NoUiCriticalTaskTimeout, active.Critical);
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
