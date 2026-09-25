using Microsoft.Extensions.Logging;

namespace Bakabase.Modules.ThirdParty.ThirdParties.Bilibili.Download;

/// <summary>
/// Progress of one page (0–100): downloading 0–<see cref="DownloadEnd"/>, merging up to <see cref="MergeEnd"/>,
/// captions and the final move up to 100.
/// </summary>
/// <remarks>
/// Video and audio download concurrently and report from their own threads. Every report goes through one
/// gate: the value is computed in one place (<see cref="Slot"/> counters are updated with
/// <see cref="Interlocked"/>), never lower than the last one reported, reported at most every
/// <c>interval</c> (unless forced), and the sink is never entered twice at once — a report arriving while
/// the sink is busy is dropped (single flight), a forced one waits. A report with the same value is still passed
/// on (after the interval): it is a sign of life that keeps the task watchdog quiet during long transfers and
/// merges.
/// </remarks>
public sealed class BilibiliPageProgress
{
    public const decimal DownloadEnd = 90m;
    public const decimal MergeEnd = 98m;
    public static readonly TimeSpan DefaultInterval = TimeSpan.FromMilliseconds(250);

    private readonly Func<decimal, Task>? _sink;
    private readonly TimeProvider _time;
    private readonly ILogger _logger;
    private readonly TimeSpan _interval;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _state = new();
    private readonly List<Slot> _slots = [];
    private decimal _last;
    private long? _lastAt;
    private bool _closed;

    public BilibiliPageProgress(Func<decimal, Task>? sink, TimeProvider time, ILogger logger,
        TimeSpan? interval = null)
    {
        _sink = sink;
        _time = time;
        _logger = logger;
        _interval = interval ?? DefaultInterval;
    }

    /// <summary>The last value handed to the sink.</summary>
    public decimal LastReported
    {
        get
        {
            lock (_state)
            {
                return _last;
            }
        }
    }

    /// <summary>A stream whose bytes count towards the download band.</summary>
    /// <param name="expectedBytes">Size estimate until the CDN reports the real total.</param>
    public Slot AddStream(long expectedBytes)
    {
        var slot = new Slot(this, Math.Max(1, expectedBytes));
        lock (_state)
        {
            _slots.Add(slot);
        }

        return slot;
    }

    /// <summary>The download band's value from the stream counters.</summary>
    public decimal DownloadValue
    {
        get
        {
            long done = 0, expected = 0;
            lock (_state)
            {
                foreach (var slot in _slots)
                {
                    var d = Interlocked.Read(ref slot.Done);
                    var e = Math.Max(Interlocked.Read(ref slot.Expected), d);
                    done += d;
                    expected += e;
                }
            }

            return expected <= 0 ? 0 : DownloadEnd * done / expected;
        }
    }

    /// <summary>A merge report: a fraction (0–1) of the merge band, or null for "still working".</summary>
    public Task ReportMergeAsync(double? fraction)
    {
        var value = fraction is { } f && double.IsFinite(f)
            ? DownloadEnd + (MergeEnd - DownloadEnd) * (decimal) Math.Clamp(f, 0, 1)
            : Math.Max(LastReported, DownloadEnd);
        return ReportAsync(value);
    }

    /// <summary>Passes <paramref name="value"/> on, subject to the gate (see the remarks).</summary>
    /// <param name="force">Wait for the gate and ignore the interval (phase changes, the final 100).</param>
    public async Task ReportAsync(decimal value, bool force = false)
    {
        if (_sink == null)
        {
            return;
        }

        if (!force)
        {
            lock (_state)
            {
                if (_lastAt is { } at && _time.GetElapsedTime(at) < _interval)
                {
                    return;
                }
            }

            if (!await _gate.WaitAsync(0))
            {
                return;
            }
        }
        else
        {
            await _gate.WaitAsync();
        }

        try
        {
            decimal reported;
            lock (_state)
            {
                if (_closed)
                {
                    return;
                }

                reported = Math.Round(Math.Clamp(Math.Max(value, _last), 0, 100), 2);
                _last = reported;
                _lastAt = _time.GetTimestamp();
            }

            await _sink(reported);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            _logger.LogWarning("Reporting download progress failed: {Error}", e.GetType().Name);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Reports <paramref name="final"/> (when given) and stops passing anything on: a transfer's report that
    /// arrives late must not reach the sink after the page is over (the caller has moved on to the next page).
    /// </summary>
    public async Task CloseAsync(decimal? final = null)
    {
        if (final is { } value)
        {
            await ReportAsync(value, true);
        }

        await _gate.WaitAsync();
        try
        {
            lock (_state)
            {
                _closed = true;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private void Pulse()
    {
        if (_sink == null)
        {
            return;
        }

        _ = PulseAsync();

        async Task PulseAsync()
        {
            try
            {
                await ReportAsync(DownloadValue);
            }
            catch
            {
                // Fire-and-forget: a cancelled sink must not surface as an unobserved exception.
            }
        }
    }

    /// <summary>Byte counters of one stream.</summary>
    public sealed class Slot
    {
        private readonly BilibiliPageProgress _owner;
        internal long Done;
        internal long Expected;

        internal Slot(BilibiliPageProgress owner, long expected)
        {
            _owner = owner;
            Expected = expected;
        }

        /// <summary>Matches <see cref="BilibiliCdnDownloader.DownloadAsync"/>'s progress callback.</summary>
        public void Report(long done, long? total)
        {
            Interlocked.Exchange(ref Done, Math.Max(0, done));
            if (total is > 0)
            {
                Interlocked.Exchange(ref Expected, total.Value);
            }

            _owner.Pulse();
        }

        /// <summary>The stream is on disk (possibly finished in an earlier run, without any report).</summary>
        public void Complete(long bytes)
        {
            var size = Math.Max(1, bytes);
            Interlocked.Exchange(ref Expected, size);
            Interlocked.Exchange(ref Done, size);
            _owner.Pulse();
        }
    }
}
