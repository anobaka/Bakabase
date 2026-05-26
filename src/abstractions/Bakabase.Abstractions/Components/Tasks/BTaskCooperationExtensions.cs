using System.Threading.Tasks;

namespace Bakabase.Abstractions.Components.Tasks;

/// <summary>
/// Cooperative cancellation helpers for task bodies. Sprinkle
/// <c>await args.YieldAsync()</c> through tight loops so pause and stop
/// requests reach the task body without waiting for the next natural await.
/// </summary>
public static class BTaskCooperationExtensions
{
    /// <summary>
    /// One-shot checkpoint: throws if cancellation was requested and waits if
    /// the task has been paused. Equivalent to writing both lines by hand —
    /// kept short so authors actually use it.
    /// </summary>
    public static async Task YieldAsync(this BTaskArgs args)
    {
        args.CancellationToken.ThrowIfCancellationRequested();
        await args.PauseToken.WaitWhilePausedAsync(args.CancellationToken);
    }
}
