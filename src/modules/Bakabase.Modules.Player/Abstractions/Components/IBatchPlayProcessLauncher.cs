namespace Bakabase.Modules.Player.Abstractions.Components;

/// <summary>
/// Launches a player process. Abstracted so the batch-play orchestration is
/// testable without spawning real processes.
/// </summary>
public interface IBatchPlayProcessLauncher
{
    /// <summary>
    /// Starts the player. Throws when the process cannot be started (e.g.
    /// executable missing); does not wait for the player to exit.
    /// </summary>
    Task LaunchAsync(string executablePath, string arguments, CancellationToken ct);
}
