namespace BrowserWall.Core.Session;

/// <summary>
/// Fixed-tick schedule for reading the address bar (every 2 seconds): round k is due at <c>start + k × interval</c>.
/// After a round, wait until the next due time; if a round overruns, skip the missed ticks and take the next one,
/// without waiting an extra interval.
/// </summary>
public static class ReadSchedule
{
    /// <summary>The first due time after <paramref name="now"/> (exclusive).</summary>
    public static TimeSpan NextDue(TimeSpan start, TimeSpan now, TimeSpan interval)
    {
        if (interval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(interval), interval, "Interval must be greater than 0.");
        }

        if (now < start)
        {
            return start;
        }

        long k = (now - start).Ticks / interval.Ticks + 1;
        return start + TimeSpan.FromTicks(k * interval.Ticks);
    }
}
