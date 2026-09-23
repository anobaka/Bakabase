using System;
using System.Collections.Generic;

namespace Bakabase.InsideWorld.Business.Components.Downloader.Components;

/// <summary>
/// Estimates from progress observed in this run, never from persisted progress or wall-clock dates.
/// The first sample is a baseline so resuming an existing download does not count as new work.
/// </summary>
internal sealed class DownloadTimeEstimator(TimeProvider? timeProvider = null)
{
    private static readonly TimeSpan MinimumObservation = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan SamplingInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan ObservationWindow = TimeSpan.FromMinutes(2);
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;
    private readonly object _sync = new();
    private readonly LinkedList<(long Timestamp, decimal Progress)> _samples = new();
    private (long Timestamp, decimal Progress)? _latest;

    public void Reset()
    {
        lock (_sync)
        {
            _samples.Clear();
            _latest = null;
        }
    }

    public void Report(decimal progress)
    {
        lock (_sync)
        {
            if (progress < 0 || progress >= 100)
            {
                Reset();
                return;
            }

            // A source may discover more work or restart its progress for a new phase.
            if (_latest.HasValue && progress < _latest.Value.Progress)
            {
                Reset();
            }

            // Repeated heartbeats are not evidence that any work has been completed.
            if (_latest.HasValue && progress == _latest.Value.Progress)
            {
                return;
            }

            var now = _timeProvider.GetTimestamp();
            _latest = (now, progress);

            // Bound memory even when a source reports progress for every received chunk.
            if (_samples.Last == null ||
                _timeProvider.GetElapsedTime(_samples.Last.Value.Timestamp, now) >= SamplingInterval)
            {
                _samples.AddLast(_latest.Value);
            }

            // Keep one sample preceding the window boundary to measure slow, file-based sources.
            while (_samples.First?.Next is { } next &&
                   _timeProvider.GetElapsedTime(next.Value.Timestamp, now) >= ObservationWindow)
            {
                _samples.RemoveFirst();
            }
        }
    }

    public double? EstimateRemainingSeconds()
    {
        lock (_sync)
        {
            if (_latest == null || _samples.First == null)
            {
                return null;
            }

            var now = _timeProvider.GetTimestamp();
            var latest = _latest.Value;
            var baseline = _samples.First.Value;
            var observed = _timeProvider.GetElapsedTime(baseline.Timestamp, latest.Timestamp);
            if (latest.Progress <= baseline.Progress || observed < MinimumObservation ||
                _timeProvider.GetElapsedTime(latest.Timestamp, now) > ObservationWindow)
            {
                return null;
            }

            // Include time spent waiting for the next update; a stalled task must not count down
            // to zero while its progress stands still.
            var elapsed = _timeProvider.GetElapsedTime(baseline.Timestamp, now).TotalSeconds;
            return Math.Ceiling((double)(100 - latest.Progress) * elapsed /
                                (double)(latest.Progress - baseline.Progress));
        }
    }
}
