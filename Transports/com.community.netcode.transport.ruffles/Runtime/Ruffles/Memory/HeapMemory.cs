using Ruffles.Exceptions;

namespace Ruffles.Memory
{
    internal class HeapMemory : ManagedMemory
    {
        public byte[] Buffer
        {
            get
            {
                if (IsDead)
                {
                    throw new MemoryException("Cannot access dead memory");
                }

                return _buffer;
            }
        }

        public uint VirtualOffset { get; set; }
        public uint VirtualCount { get; set; }

        internal override string LeakedType => nameof(HeapMemory);
        internal override string LeakedData => "[PointerLength=" + _buffer.Length + "] [VirtualOffset=" + VirtualOffset + "] [VirtualCount=" + VirtualCount + "]";

        private byte[] _buffer;

        public HeapMemory(uint size)
        {
            _buffer = new byte[size];
            VirtualOffset = 0;
            VirtualCount = size;
        }

        public void EnsureSize(uint size)
        {
            if (_buffer.Length < size)
            {
                byte[] oldBuffer = _buffer;

                _buffer = new byte[size];

                System.Buffer.BlockCopy(oldBuffer, 0, _buffer, 0, oldBuffer.Length);
            }
        }
    }
}
