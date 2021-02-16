using Ruffles.Memory;

namespace Ruffles.Channeling.Channels.Shared
{
    internal struct PendingIncomingPacket : IMemoryReleasable
    {
        public bool IsAlloced => Memory != null && !Memory.IsDead;

        public HeapMemory Memory;

        public void DeAlloc(MemoryManager memoryManager)
        {
            if (IsAlloced)
            {
                memoryManager.DeAlloc(Memory);
                Memory = null;
            }
        }
    }

    internal struct PendingIncomingPacketFragmented : IMemoryReleasable
    {
        public bool IsAlloced => Fragments != null;

        public bool IsComplete
        {
            get
            {
                if (Fragments == null || Size == null || Fragments.VirtualCount < Size)
                {
                    return false;
                }

                for (int i = 0; i < Fragments.VirtualCount; i++)
                {
                    if (Fragments.Pointers[Fragments.VirtualOffset + i] == null)
                    {
                        return false;
                    }
                }

                return true;
            }
        }

        public uint TotalByteSize
        {
            get
            {
                uint byteSize = 0;

                if (!IsComplete)
                {
                    // TODO: Throw
                    return byteSize;
                }


                for (int i = 0; i < Fragments.VirtualCount; i++)
                {
                    byteSize += ((HeapMemory)Fragments.Pointers[Fragments.VirtualOffset + i]).VirtualCount;
                }

                return byteSize;
            }
        }

        public ushort? Size;
        public HeapPointers Fragments;

        public void DeAlloc(MemoryManager memoryManager)
        {
            if (IsAlloced)
            {
                for (int i = 0; i < Fragments.VirtualCount; i++)
                {
                    if (Fragments.Pointers[Fragments.VirtualOffset + i] != null)
                    {
                        memoryManager.DeAlloc((HeapMemory)Fragments.Pointers[Fragments.VirtualOffset + i]);
                        Fragments.Pointers[Fragments.VirtualOffset + i] = null;
                    }
                }

                memoryManager.DeAlloc(Fragments);
                Fragments = null;
            }
        }
    }
}
