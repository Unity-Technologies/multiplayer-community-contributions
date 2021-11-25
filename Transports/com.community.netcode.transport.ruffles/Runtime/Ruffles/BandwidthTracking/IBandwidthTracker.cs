namespace Ruffles.BandwidthTracking
{
    /// <summary>
    /// An interface for creating bandwidth trackers.
    /// </summary>
    public interface IBandwidthTracker
    {
        /// <summary>
        /// Asks the tracker if it can send a certain amount of bytes.
        /// </summary>
        /// <returns><c>true</c>, if send was allowed, <c>false</c> otherwise.</returns>
        /// <param name="size">The requested size in bytes.</param>
        bool TrySend(int size);
    }
}
