using Bakabase.InsideWorld.Business.Components.DataSync.Runtime;

namespace Bakabase.Tests.DataSync;

/// <summary>
/// One clock for every host of a test (§13.7 "simulated clock"): the runtime's <see cref="IDataSyncClock"/> and the
/// persistence layer's <see cref="TimeProvider"/> read the same time, which only the test moves.
/// </summary>
internal sealed class DataSyncTestClock(DateTime start) : TimeProvider, IDataSyncClock
{
    private long _ticks = start.Ticks;

    public override DateTimeOffset GetUtcNow() => new(new DateTime(Interlocked.Read(ref _ticks), DateTimeKind.Utc));

    public DateTime UtcNow => GetUtcNow().UtcDateTime;

    public void Advance(TimeSpan by) => Interlocked.Add(ref _ticks, by.Ticks);
}
