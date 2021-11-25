using Ruffles.Memory;

namespace Ruffles.Channeling.Channels.Shared
{
    internal struct PendingSend
    {
        public HeapMemory Memory;
        public bool NoMerge;
        public ulong NotificationKey;
    }
}
