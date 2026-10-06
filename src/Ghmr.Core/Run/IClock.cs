using System.Diagnostics;

namespace Ghmr.Core.Run;

public interface IClock
{
    DateTimeOffset UtcNow { get; }
    long Timestamp { get; }
    TimeSpan Elapsed(long startingTimestamp, long endingTimestamp);
}

public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
    public long Timestamp => Stopwatch.GetTimestamp();

    public TimeSpan Elapsed(long startingTimestamp, long endingTimestamp)
    {
        return Stopwatch.GetElapsedTime(startingTimestamp, endingTimestamp);
    }
}
