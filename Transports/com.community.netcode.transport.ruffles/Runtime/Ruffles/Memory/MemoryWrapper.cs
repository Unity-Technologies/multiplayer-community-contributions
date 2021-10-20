using System;
using Ruffles.Exceptions;

namespace Ruffles.Memory
{
    internal class MemoryWrapper : ManagedMemory
    {
        public HeapMemory AllocatedMemory
        {
            get
            {
                if (IsDead)
                {
                    throw new MemoryException("Cannot access dead memory");
                }

                return allocatedMemory;
            }
            set
            {
                if (IsDead)
                {
                    throw new MemoryException("Cannot access dead memory");
                }

                allocatedMemory = value;
            }
        }

        public ArraySegment<byte>? DirectMemory
        {
            get
            {
                if (IsDead)
                {
                    throw new MemoryException("Cannot access dead memory");
                }

                return directMemory;
            }
            set
            {
                if (IsDead)
                {
                    throw new MemoryException("Cannot access dead memory");
                }

                directMemory = value;
            }
        }

        internal override string LeakedType => nameof(MemoryWrapper);
        internal override string LeakedData => "";

        private HeapMemory allocatedMemory;
        private ArraySegment<byte>? directMemory;
    }
}
