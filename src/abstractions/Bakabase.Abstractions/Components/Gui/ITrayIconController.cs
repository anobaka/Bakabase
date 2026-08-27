namespace Bakabase.Abstractions.Components.Gui;

public interface ITrayIconController
{
    void SetTrayIcon(bool isRunning);

    /// <summary>
    /// Registers / unregisters the tray icon with the shell. Hide it on any code path that is
    /// about to end the process without an orderly GUI shutdown — most notably the Velopack
    /// update-restart, which ends in <c>Environment.Exit</c>. Skipping that leaves Windows
    /// painting a dead icon until the user happens to hover over it.
    /// Idempotent and must never throw.
    /// </summary>
    void SetTrayIconVisible(bool visible);
}
