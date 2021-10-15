namespace Ruffles.Connections
{
    /// <summary>
    /// Enum representing the connection state between two RuffleSockets.
    /// </summary>
    public enum ConnectionState : byte
    {
        /// <summary>
        /// The connection is not connected.
        /// </summary>
        Disconnected,
        /// <summary>
        /// The connection is established.
        /// </summary>
        Connected,
        /// <summary>
        /// The local peer has requested a connection.
        /// </summary>
        RequestingConnection,
        /// <summary>
        /// The local peer has requested a challenge to be solved.
        /// </summary>
        RequestingChallenge,
        /// <summary>
        /// The local peer is solving the challenge.
        /// </summary>
        SolvingChallenge
    }
}
