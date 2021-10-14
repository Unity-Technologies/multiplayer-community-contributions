using System;
using Ruffles.Memory;
using Ruffles.Time;

namespace Ruffles.Channeling.Channels.Shared
{
    internal struct PendingOutgoingPacket : IMemoryReleasable
    {
        public bool IsAlloced => Memory != null;

        public HeapMemory Memory;
        public NetTime LastSent;
        public NetTime FirstSent;
        public ushort Attempts;
        public ulong NotificationKey;

        public void DeAlloc(MemoryManager memoryManager)
        {
            if (IsAlloced)
            {
                memoryManager.DeAlloc(Memory);
                Memory = null;
            }
        }
    }

    internal struct PendingOutgoingPacketSequence : IMemoryReleasable
    {
        public bool IsAlloced => Memory != null;

        public ushort Sequence;
        public HeapMemory Memory;
        public NetTime LastSent;
        public NetTime FirstSent;
        public ushort Attempts;
        public ulong NotificationKey;

        public void DeAlloc(MemoryManager memoryManager)
        {
            if (IsAlloced)
            {
                memoryManager.DeAlloc(Memory);
                Memory = null;
            }
        }
    }

    internal struct PendingOutgoingPacketFragmented : IMemoryReleasable
    {
        public bool IsAlloced => Fragments != null;

        public ulong NotificationKey;
        public HeapPointers Fragments;

        public void DeAlloc(MemoryManager memoryManager)
        {
            if (IsAlloced)
            {
                for (int i = 0; i < Fragments.VirtualCount; i++)
                {
                    if (Fragments.Pointers[Fragments.VirtualOffset + i] != null)
                    {
                        PendingOutgoingFragment fragment = (PendingOutgoingFragment)Fragments.Pointers[Fragments.VirtualOffset + i];

                        // Dealloc the fragment
                        fragment.DeAlloc(memoryManager);
                        Fragments.Pointers[Fragments.VirtualOffset + i] = null;
                    }
                }

                // Dealloc the pointers
                memoryManager.DeAlloc(Fragments);
                Fragments = null;
            }
        }
    }

    internal struct PendingOutgoingFragment : IMemoryReleasable
    {
        public bool IsAlloced => Memory != null;

        public HeapMemory Memory;
        public NetTime LastSent;
        public NetTime FirstSent;
        public ushort Attempts;

        public void DeAlloc(MemoryManager memoryManager)
        {
            if (IsAlloced)
            {
                memoryManager.DeAlloc(Memory);
                Memory = null;
            }
        }
    }
}
