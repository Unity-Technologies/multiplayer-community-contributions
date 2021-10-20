using Ruffles.Time;

namespace Ruffles.Messaging
{
    internal struct MessageStatus
    {
        public bool HasAcked;
        public byte Attempts;
        public NetTime LastAttempt;
    }
}
