namespace Bakabase.Abstractions.Models.Domain.Constants;

public enum BTaskStatus
{
    NotStarted = 1,
    Running = 2,
    Paused = 3,
    Error = 4,
    Completed = 5,
    Cancelled = 6,
    Cancelling = 7,
    // Transitional state set the moment Pause() is called. Stays until the task
    // body reaches the next PauseToken.WaitWhilePausedAsync (where OnPause flips
    // it to Paused). Without it the chip lies about "Paused" while the task is
    // still doing CPU/IO work between yield points.
    Pausing = 8,
    // Transitional state set the moment Resume() is called. Flipped to Running
    // once the task body's WaitWhilePausedAsync returns (OnResume fires).
    Resuming = 9,
}