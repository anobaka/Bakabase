using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models;
using Bakabase.InsideWorld.Models.Constants;

namespace Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Components
{
    /// <summary>
    /// What a pre-check concluded about one task, without starting it.
    /// </summary>
    public enum DownloadTaskPrecheckOutcome
    {
        /// <summary>Nothing special — schedule it normally.</summary>
        Run = 0,

        /// <summary>
        /// Runnable, but only once nothing in <see cref="Run"/> is left. Used to drain the tasks a
        /// source considers more valuable first.
        /// </summary>
        Defer = 1,

        /// <summary>
        /// There is provably nothing left to download. The scheduler completes it outright instead of
        /// spending a start/stop cycle (and the network round-trips inside it) to reach the same answer.
        /// </summary>
        AlreadySatisfied = 2
    }

    /// <param name="Outcome">What to do with the task.</param>
    /// <param name="Reason">Short, loggable explanation. Not shown to the user.</param>
    public readonly record struct DownloadTaskPrecheckVerdict(DownloadTaskPrecheckOutcome Outcome, string? Reason = null);

    /// <summary>
    /// A source-specific pass over the <em>whole</em> candidate set, run once per scheduling pass.
    ///
    /// The point is to answer per-source questions in bulk. Answering them one task at a time means
    /// paying a full start/stop lifecycle — downloader creation, background task, database write,
    /// UI push, and usually a network request — for every task, just to find out it had nothing to
    /// do. With a thousand tasks that walk dominates the run; here the same answer costs one
    /// directory listing.
    ///
    /// Implementations must be cheap and side-effect free: no network calls, no writes. Anything a
    /// pre-check cannot decide locally it should leave as <see cref="DownloadTaskPrecheckOutcome.Run"/>
    /// and let the downloader settle.
    /// </summary>
    public interface IDownloadTaskPrecheck
    {
        ThirdPartyId ThirdPartyId { get; }

        Task<IReadOnlyDictionary<int, DownloadTaskPrecheckVerdict>> EvaluateAsync(
            IReadOnlyList<DownloadTask> candidates, CancellationToken ct);
    }
}
