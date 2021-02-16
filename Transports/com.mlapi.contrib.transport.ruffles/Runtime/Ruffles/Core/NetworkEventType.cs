namespace Ruffles.Core
{
    /// <summary>
    /// Enum describing the type of NetworkEvent.
    /// </summary>
    public enum NetworkEventType
    {
        /// <summary>
        /// Nothing occured.
        /// </summary>
        Nothing,
        /// <summary>
        /// A connection was established.
        /// </summary>
        Connect,
        /// <summary>
        /// A connection was disconnected.
        /// </summary>
        Disconnect,
        /// <summary>
        /// A connection timed out.
        /// </summary>
        Timeout,
        /// <summary>
        /// A connection sent data.
        /// </summary>
        Data,
        /// <summary>
        /// An endpoint sent unconnected data.
        /// </summary>
        UnconnectedData,
        /// <summary>
        /// An endpoint sent broadcast data.
        /// </summary>
        BroadcastData,
        /// <summary>
        /// A packet was acked by the remote.
        /// </summary>
        AckNotification
    }
}
