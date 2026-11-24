using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Bakabase.Service.Components.Acquisition.Planning;

public sealed record AcquisitionThroughputRates(double? DownloadBytesPerSecond, double? ReadBytesPerSecond,
    double? WriteBytesPerSecond, bool DownloadPartial, bool FileIoPartial);

public sealed record AcquisitionTaskThroughput(int TaskId, string? Title, AcquisitionThroughputRates Rates);

public sealed record AcquisitionThroughputSample(string SampledAt, AcquisitionThroughputRates Totals,
    IReadOnlyList<AcquisitionTaskThroughput> Tasks);

public sealed record AcquisitionThroughputView(int IntervalSeconds, int WindowSeconds,
    AcquisitionThroughputSample? Latest, IReadOnlyList<AcquisitionThroughputSample> History);

/// <summary>One bounded server-side window shared by all clients, including those opening the page mid-transfer.</summary>
public sealed class AcquisitionThroughputHistory
{
    internal const int IntervalSeconds = 5;
    internal const int WindowSeconds = 300;
    private readonly object _gate = new();
    private readonly Queue<(DateTimeOffset At, AcquisitionThroughputSample Sample)> _samples = new();

    internal void Append(DateTimeOffset at, AcquisitionThroughputRates totals, IReadOnlyList<AcquisitionTaskThroughput> tasks)
    {
        // Include the timezone explicitly. Browser clients can use different local timezones from the server.
        var sample = new AcquisitionThroughputSample(at.UtcDateTime.ToString("O", CultureInfo.InvariantCulture), totals, tasks);
        lock (_gate)
        {
            while (_samples.Count != 0 && (_samples.Count >= WindowSeconds / IntervalSeconds ||
                       _samples.Peek().At <= at.AddSeconds(-WindowSeconds)))
                _samples.Dequeue();
            _samples.Enqueue((at, sample));
        }
    }

    public AcquisitionThroughputView Get(bool includeHistory)
    {
        lock (_gate)
        {
            var latest = _samples.Count == 0 ? null : _samples.Last().Sample;
            return new(IntervalSeconds, WindowSeconds,
                latest is null || includeHistory ? latest : latest with {Tasks = []},
                includeHistory ? _samples.Select(s => s.Sample).ToArray() : []);
        }
    }
}
