using Ruffles.Time;

namespace Ruffles.BandwidthTracking
{
    /// <summary>
    /// A simple bandwidth tracker capturing an resetting average with an optional carry between resets.
    /// </summary>
    public class SimpleBandwidthTracker : IBandwidthTracker
    {
        private NetTime _startTime { get; set; }
        private int _sentBytes { get; set; }
        private int _lastPeriodBuffer { get; set; }

        private readonly int maxBytesPerSecond;
        private readonly int averageIntervalSeconds;
        private readonly float remainderCarry;

        private readonly object _lock = new object();

        /// <summary>
        /// Initializes a new instance of the <see cref="T:Ruffles.BandwidthTracking.SimpleBandwidthTracker"/> class.
        /// </summary>
        /// <param name="maxBytesPerSecond">The max bytes per second allowed.</param>
        /// <param name="averageIntervalSeconds">The amount of seconds before the average interval.</param>
        /// <param name="remainderCarry">The percentage of the remaining quota for the current average period that is carried to the next period on a reset.</param>
        public SimpleBandwidthTracker(int maxBytesPerSecond, int averageIntervalSeconds, float remainderCarry)
        {
            this.maxBytesPerSecond = maxBytesPerSecond;
            this.averageIntervalSeconds = averageIntervalSeconds;
            this.remainderCarry = remainderCarry;
        }

        public bool TrySend(int size)
        {
            lock (_lock)
            {
                double secondsSinceStart = (NetTime.Now - _startTime).TotalSeconds;
                double bytesPerSecond = (_sentBytes + size) / secondsSinceStart;

                bool allowPacket = bytesPerSecond <= maxBytesPerSecond + _lastPeriodBuffer;

                if (allowPacket)
                {
                    _sentBytes += size;
                }

                if (secondsSinceStart >= averageIntervalSeconds)
                {
                    // Give half of what was left of the previous period as a bonus to this period
                    _lastPeriodBuffer = (int)((maxBytesPerSecond - _sentBytes) * remainderCarry);
                    _sentBytes = 0;
                    _startTime = NetTime.Now;
                }

                return allowPacket;
            }
        }
    }
}
