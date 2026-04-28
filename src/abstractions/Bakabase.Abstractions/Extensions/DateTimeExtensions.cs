namespace Bakabase.Abstractions.Extensions;

public static class DateTimeExtensions
{
    /// <summary>
    /// Drops sub-millisecond tick precision so the value aligns with typical <c>yyyy-MM-dd HH:mm:ss.fff</c> storage
    /// and comparisons against DB-round-tripped file timestamps.
    /// </summary>
    public static DateTime TruncateToMilliseconds(this DateTime value)
    {
        var ticks = value.Ticks;
        var trimmed = ticks - ticks % TimeSpan.TicksPerMillisecond;
        return new DateTime(trimmed, value.Kind);
    }
}
