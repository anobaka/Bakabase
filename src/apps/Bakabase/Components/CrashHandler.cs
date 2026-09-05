using System;
using System.Threading.Tasks;

namespace Bakabase.Components;

/// <summary>
/// Last-resort reporting for exceptions that nothing else catches.
///
/// <c>AppHost.Start</c> already wraps its own body in a try/catch that logs and raises the
/// fatal-error window, so anything failing in there is both visible and recorded. The window
/// before it is neither: a throw in Avalonia's XAML load, in <c>App.Initialize</c>'s tray-icon
/// resolution, in the pending data-path relocation, or in <c>AppService</c>'s static
/// constructor ends the process with no window and no log line. That is precisely the "it
/// closes the instant I open it" report we cannot act on — the exception exists only in the
/// OS event log, which users do not think to open and we cannot walk every one of them
/// through.
///
/// Sentry is deliberately not called from here. <c>SentrySdk.Init</c> (see
/// <c>BakabaseStartup.ConfigureServices</c>) installs its own unhandled-exception integration,
/// so capturing again would duplicate every event it can already see; and before that init
/// runs there is no transport to send on anyway. What this class adds is the two sinks Sentry
/// cannot cover: the file log, and stderr.
/// </summary>
internal static class CrashHandler
{
    public static void Install()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            Report(e.ExceptionObject as Exception, e.ExceptionObject, "Unhandled exception",
                e.IsTerminating);

        // Left unobserved on purpose: since .NET 4.5 these do not terminate the process, so
        // calling SetObserved would change nothing today while quietly disarming the escalation
        // if ThrowUnobservedTaskExceptions is ever switched on. We only want them in the log.
        TaskScheduler.UnobservedTaskException += (_, e) =>
            Report(e.Exception, e.Exception, "Unobserved task exception", terminating: false);
    }

    /// <param name="detail">
    /// <c>UnhandledException</c> hands us <c>object</c>, not <c>Exception</c> — a throw from
    /// another language on the same runtime need not be one. Rendering it separately keeps
    /// those cases from being reported as an empty line.
    /// </param>
    private static void Report(Exception? exception, object? detail, string what, bool terminating)
    {
        // The two sinks get their own guard rather than sharing one: a console handle that
        // rejects writes must not be able to cost us the log entry, which is the sink that
        // actually reaches users.
        try
        {
            // Unconditional, and first. It is the only sink that works before the Serilog file
            // sink exists, and `Bakabase.exe 2> crash.txt` from a console is the one instruction
            // we can give a user whose app dies before it can write anywhere.
            Console.Error.WriteLine($"[Bakabase] {what}: {detail}");
        }
        catch
        {
            // No console to write to. The log below is the one that matters anyway.
        }

        try
        {
            // Silently discarded until AppService's static constructor has built the file sink
            // (Serilog's default logger is a no-op). Program.Main forces that to happen before
            // Avalonia starts, so in practice this lands in {AppData}/logs/AppLog_*.log right
            // after the startup trace that shows how far the launch got.
            Serilog.Log.Fatal(exception, "{What} (terminating: {Terminating}) {Detail}", what,
                terminating, exception == null ? detail : null);

            if (terminating)
            {
                // The file sink is unbuffered, so this is belt-and-braces — but the process is
                // about to die either way, and a truncated final entry is the one we need most.
                Serilog.Log.CloseAndFlush();
            }
        }
        catch
        {
            // A crash reporter that throws turns a diagnosable crash into a mystery. There is
            // nowhere left to report this to, so drop it.
        }
    }
}
