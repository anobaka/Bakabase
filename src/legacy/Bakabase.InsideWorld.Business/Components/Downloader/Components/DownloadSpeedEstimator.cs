using System;
using System.Collections.Generic;

namespace Bakabase.InsideWorld.Business.Components.Downloader.Components;

/// <summary>
/// Recent bytes written by this task's media transfers. Each transfer has its own baseline so
/// resumed .part files and a second DASH stream never appear as bytes received in this run.
/// </summary>
internal sealed class DownloadSpeedEstimator(TimeProvider? timeProvider = null)
{
    private static readonly TimeSpan Window = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan MinimumObservation = TimeSpan.FromSeconds(1);
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;
    private readonly object _sync = new();
    private readonly LinkedList<(long Timestamp, long Bytes)> _samples = new();
    private long? _firstByteAt;
    private int _generation;

    public Func<long, bool> CreateTransferReporter()
    {
        int generation;
        lock (_sync)
        {
            generation = _generation;
        }

        var reporterGate = new object();
        long? previous = null;
        return done =>
        {
            if (done < 0) return false;

            long delta = 0;
            lock (reporterGate)
            {
                if (previous is { } value && done > value) delta = done - value;
                // A discarded partial file can make this counter go backwards. Treat its new
                // position as the baseline instead of subtracting or carrying the old position.
                previous = done;
            }

            if (delta <= 0) return false;
            lock (_sync)
            {
                if (generation != _generation) return false;
                var now = _time.GetTimestamp();
                Prune(now);
                if (_samples.Count == 0) _firstByteAt = now;
                _samples.AddLast((now, delta));
                return true;
            }
        };
    }

    public double? EstimateBytesPerSecond()
    {
        lock (_sync)
        {
            var now = _time.GetTimestamp();
            Prune(now);
            if (_samples.Count == 0 || _firstByteAt is not { } first) return null;

            var observed = _time.GetElapsedTime(first, now);
            if (observed < MinimumObservation) return null;

            var seconds = Math.Min(observed.TotalSeconds, Window.TotalSeconds);
            double bytes = 0;
            foreach (var sample in _samples) bytes += sample.Bytes;
            return bytes / seconds;
        }
    }

    public bool HasRecentBytes
    {
        get
        {
            lock (_sync)
            {
                Prune(_time.GetTimestamp());
                return _samples.Count > 0;
            }
        }
    }

    public void Reset()
    {
        lock (_sync)
        {
            _generation++;
            _samples.Clear();
            _firstByteAt = null;
        }
    }

    private void Prune(long now)
    {
        while (_samples.First is { } first && _time.GetElapsedTime(first.Value.Timestamp, now) >= Window)
        {
            _samples.RemoveFirst();
        }

        if (_samples.Count == 0) _firstByteAt = null;
    }
}
