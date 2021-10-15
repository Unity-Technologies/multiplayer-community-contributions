using System;
using System.Diagnostics;

namespace Ruffles.Time
{
    /// <summary>
    /// Represents a time in the highest possible accuracy.
    /// </summary>
    public struct NetTime : IComparable, IComparable<NetTime>, IEquatable<NetTime>
    {
        /// <summary>
        /// Gets whether or not the time supports high resolution.
        /// </summary>
        public static readonly bool HighResolution = Stopwatch.IsHighResolution;
        /// <summary>
        /// Gets the start date.
        /// </summary>
        public static readonly DateTime StartDate = DateTime.Now;

        private static readonly double MillisecondsPerTick = 1000d / Stopwatch.Frequency;
        private static readonly long StartTime = Stopwatch.GetTimestamp();

        /// <summary>
        /// Gets a new NetTime that represents the current time.
        /// </summary>
        /// <value>The now.</value>
        public static NetTime Now => new NetTime(Stopwatch.GetTimestamp() - StartTime);
        /// <summary>
        /// Gets a new NetTime that represents the start time.
        /// </summary>
        /// <value>The startTime.</value>
        public static NetTime MinValue => new NetTime(0);

        private readonly long InternalTicks;

        /// <summary>
        /// Gets the number of milliseconds since start.
        /// </summary>
        /// <value>The amount of milliseconds since start.</value>
        public long Milliseconds => (long)(InternalTicks * MillisecondsPerTick);
        /// <summary>
        /// Gets the time in DateTime format.
        /// </summary>
        /// <value>The time in DateTime format.</value>
        public DateTime Date => StartDate.AddMilliseconds(Milliseconds);

        private NetTime(long ticks)
        {
            InternalTicks = ticks;
        }

        /// <summary>
        /// Gets a new NetTime with the amount of milliseconds added.
        /// </summary>
        /// <returns>The new NetTime with added milliseconds.</returns>
        /// <param name="milliseconds">The amount of milliseconds to add.</param>
        public NetTime AddMilliseconds(double milliseconds)
        {
            return new NetTime(InternalTicks + (long)(milliseconds / MillisecondsPerTick));
        }

        public static TimeSpan operator -(NetTime t1, NetTime t2) => new TimeSpan(0, 0, 0, 0, (int)((t1.InternalTicks - t2.InternalTicks) * MillisecondsPerTick));
        public static bool operator ==(NetTime t1, NetTime t2) => t1.InternalTicks == t2.InternalTicks;
        public static bool operator !=(NetTime t1, NetTime t2) => t1.InternalTicks != t2.InternalTicks;
        public static bool operator <(NetTime t1, NetTime t2) => t1.InternalTicks < t2.InternalTicks;
        public static bool operator <=(NetTime t1, NetTime t2) => t1.InternalTicks <= t2.InternalTicks;
        public static bool operator >(NetTime t1, NetTime t2) => t1.InternalTicks > t2.InternalTicks;
        public static bool operator >=(NetTime t1, NetTime t2) => t1.InternalTicks >= t2.InternalTicks;

        public override int GetHashCode()
        {
            long ticks = InternalTicks;
            return unchecked((int)ticks) ^ (int)(ticks >> 32);
        }

        public override bool Equals(object obj)
        {
            if (obj is NetTime)
            {
                return InternalTicks == ((NetTime)obj).InternalTicks;
            }

            return false;
        }

        public int CompareTo(object obj)
        {
            if (obj == null) return 1;

            if (!(obj is NetTime))
            {
                throw new ArgumentException("Comparator has to be a NetTime", nameof(obj));
            }

            return Compare(this, (NetTime)obj);
        }

        private static int Compare(NetTime t1, NetTime t2)
        {
            long ticks1 = t1.InternalTicks;
            long ticks2 = t2.InternalTicks;

            if (ticks1 > ticks2) return 1;
            if (ticks1 < ticks2) return -1;

            return 0;
        }

        public int CompareTo(NetTime other)
        {
            return Compare(this, other);
        }

        public bool Equals(NetTime other)
        {
            return InternalTicks == other.InternalTicks;
        }
    }
}
