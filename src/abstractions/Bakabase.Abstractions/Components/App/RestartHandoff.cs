using System;
using System.Diagnostics;
using System.Globalization;

namespace Bakabase.Abstractions.Components.App;

/// <summary>
/// The handshake between a process that is restarting the app and the replacement it spawns.
/// </summary>
/// <remarks>
/// A plain restart spawns the replacement first and then asks the host to stop, because the
/// HTTP response has to get back to the frontend before the sockets come down. The
/// single-instance guard, meanwhile, refuses whoever finds the data directory still locked —
/// and that lock belongs to the old process until it is on its way out. Nothing ordered those
/// two events, so a replacement that started quickly enough mistook its own dying predecessor for
/// a live instance, wrote "SHOW" down its pipe and exited. The predecessor then exited too,
/// and the user was left with no app at all.
///
/// So the spawner names itself on the command line and the replacement waits for that process
/// to be gone before anything reaches the guard. The wait is bounded: a predecessor wedged
/// open by a long shutdown should not hold the replacement forever, and continuing is no worse
/// than the behaviour this replaces.
/// </remarks>
public static class RestartHandoff
{
    /// <summary>
    /// Command-line switch carrying the spawning process's id, as <c>--restart-after-pid=123</c>.
    /// </summary>
    public const string PredecessorPidSwitch = "--restart-after-pid";

    public static readonly TimeSpan DefaultMaxWait = TimeSpan.FromSeconds(30);

    public static string FormatArgument(int pid) =>
        $"{PredecessorPidSwitch}={pid.ToString(CultureInfo.InvariantCulture)}";

    public static int? TryReadPredecessorPid(string[]? args)
    {
        if (args == null) return null;

        const string separator = "=";
        var prefix = PredecessorPidSwitch + separator;

        foreach (var arg in args)
        {
            if (arg == null || !arg.StartsWith(prefix, StringComparison.Ordinal)) continue;

            if (int.TryParse(arg.AsSpan(prefix.Length), NumberStyles.Integer, CultureInfo.InvariantCulture,
                    out var pid) && pid > 0)
            {
                return pid;
            }
        }

        return null;
    }

    /// <summary>
    /// Blocks until the process named by <see cref="PredecessorPidSwitch"/> has exited.
    /// </summary>
    /// <returns>
    /// Null when this launch is not a restart and there was nothing to wait for; otherwise a
    /// line describing what happened, for the caller to log once its sink exists.
    /// </returns>
    public static string? WaitForPredecessor(string[]? args) => WaitForPredecessor(args, DefaultMaxWait);

    public static string? WaitForPredecessor(string[]? args, TimeSpan maxWait)
    {
        var pid = TryReadPredecessorPid(args);
        if (pid == null) return null;

        var stopwatch = Stopwatch.StartNew();
        try
        {
            using var predecessor = Process.GetProcessById(pid.Value);

            if (predecessor.WaitForExit((int) maxWait.TotalMilliseconds))
            {
                return $"Restart handoff: predecessor {pid} exited after {stopwatch.ElapsedMilliseconds}ms.";
            }

            return $"Restart handoff: predecessor {pid} was still alive after {maxWait.TotalSeconds:0}s. " +
                   "Starting anyway; the single-instance guard may refuse this process.";
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            // Not running any more — which is the state we were waiting for.
            return $"Restart handoff: predecessor {pid} had already exited.";
        }
    }
}
