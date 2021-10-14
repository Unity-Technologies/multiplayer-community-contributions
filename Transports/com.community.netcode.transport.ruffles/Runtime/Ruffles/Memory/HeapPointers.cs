using System;
using Ruffles.Exceptions;

namespace Ruffles.Memory
{
    internal class HeapPointers : ManagedMemory
    {
        public object[] Pointers
        {
            get
            {
                if (IsDead)
                {
                    throw new MemoryException("Cannot access dead memory");
                }

                return _pointers;
            }
        }

        public uint VirtualOffset { get; set; }
        public uint VirtualCount { get; set; }

        internal override string LeakedType => nameof(HeapPointers);
        internal override string LeakedData => "[PointerLength=" + _pointers.Length + "] [VirtualOffset=" + VirtualOffset + "] [VirtualCount=" + VirtualCount + "]";

        private object[] _pointers;

        public HeapPointers(uint size)
        {
            _pointers = new object[size];
            VirtualOffset = 0;
            VirtualCount = size;
        }

        public void EnsureSize(uint size)
        {
            if (_pointers.Length < size)
            {
                object[] oldBuffer = _pointers;

                _pointers = new object[size];

                Array.Copy(oldBuffer, 0, _pointers, 0, oldBuffer.Length);
            }
        }
    }
}
