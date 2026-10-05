using System.Diagnostics;
using Bakabase.Abstractions.Components.Threading;
using Bootstrap.Components.Configuration.Abstractions;

namespace Bakabase.Modules.ThirdParty.ThirdParties.SoulPlus;

/// <summary>Applies the site's shared concurrency and pacing to its fingerprinted transport.</summary>
public sealed class SoulPlusRequestGate(IBOptions<ISoulPlusOptions> options)
{
    private readonly AsyncConcurrencyGate _capacity = new(() => options.Value.MaxConcurrency);
    private readonly object _pacing = new();
    private long? _lastStarted;

    public async Task<T> ExecuteAsync<T>(Func<Task<T>> send, CancellationToken ct = default)
    {
        using var lease = await _capacity.EnterAsync(ct).ConfigureAwait(false);
        await WaitForStartAsync(ct).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
        // The delegate must finish when transport work really ends, including on cancellation.
        // In particular, never use Task.WaitAsync(ct) around a non-cancellable native request.
        try
        {
            var result = await send().ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            return result;
        }
        catch (Exception) when (ct.IsCancellationRequested)
        {
            throw new OperationCanceledException(ct);
        }
    }

    private async Task WaitForStartAsync(CancellationToken ct)
    {
        while (true)
        {
            TimeSpan delay;
            lock (_pacing)
            {
                ct.ThrowIfCancellationRequested();
                delay = _lastStarted is { } started
                    ? TimeSpan.FromMilliseconds(Math.Max(0, options.Value.RequestInterval)) - Stopwatch.GetElapsedTime(started)
                    : TimeSpan.Zero;
                if (delay <= TimeSpan.Zero)
                {
                    _lastStarted = Stopwatch.GetTimestamp();
                    return;
                }
            }
            await Task.Delay(delay < TimeSpan.FromMilliseconds(250) ? delay : TimeSpan.FromMilliseconds(250), ct)
                .ConfigureAwait(false);
        }
    }
}
