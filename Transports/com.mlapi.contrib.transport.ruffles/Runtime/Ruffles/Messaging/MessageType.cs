namespace Ruffles.Messaging
{
    internal enum MessageType : byte
    {
        ConnectionRequest,
        ChallengeRequest,
        ChallengeResponse,
        Hail,
        HailConfirmed,
        Heartbeat,
        Data,
        Disconnect,
        Ack,
        Merge,
        UnconnectedData,
        MTURequest,
        MTUResponse,
        Broadcast,
        // Unknown should never be sent down the wire. Keep it at the highest binary
        Unknown = 255
    }
}
