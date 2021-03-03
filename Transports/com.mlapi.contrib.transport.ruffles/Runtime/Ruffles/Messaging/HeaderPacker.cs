namespace Ruffles.Messaging
{
    // TODO: Future proof this. Enum.IsDefined is too slow. Maybe precompute array of valid types?
    internal static class HeaderPacker
    {
        internal static byte Pack(MessageType messageType)
        {
            switch (messageType)
            {
                case MessageType.ConnectionRequest:
                case MessageType.ChallengeRequest:
                case MessageType.ChallengeResponse:
                case MessageType.Hail:
                case MessageType.HailConfirmed:
                case MessageType.Heartbeat:
                case MessageType.Data:
                case MessageType.Disconnect:
                case MessageType.Ack:
                case MessageType.Merge:
                case MessageType.UnconnectedData:
                case MessageType.MTURequest:
                case MessageType.MTUResponse:
                case MessageType.Broadcast:
                    // First 4 bits is type
                    return (byte)(((byte)messageType) & 15);
                default:
                    // First 4 bits is type
                    return ((byte)MessageType.Unknown) & 15;
            }
        }

        internal static void Unpack(byte header, out MessageType messageType)
        {
            // Get first 4 bits
            byte type = (byte)(header & 15);

            switch (type)
            {
                case (byte)MessageType.ConnectionRequest:
                case (byte)MessageType.ChallengeRequest:
                case (byte)MessageType.ChallengeResponse:
                case (byte)MessageType.Hail:
                case (byte)MessageType.HailConfirmed:
                case (byte)MessageType.Heartbeat:
                case (byte)MessageType.Data:
                case (byte)MessageType.Disconnect:
                case (byte)MessageType.Ack:
                case (byte)MessageType.Merge:
                case (byte)MessageType.UnconnectedData:
                case (byte)MessageType.MTURequest:
                case (byte)MessageType.MTUResponse:
                case (byte)MessageType.Broadcast:
                    // First 4 bits is type
                    messageType = (MessageType)type;
                    return;
                default:
                    // First 4 bits is type
                    messageType = MessageType.Unknown;
                    return;
            }
        }
    }
}
