using System.Threading;
using Ruffles.Utils;

namespace Ruffles.Collections
{
    // Operates on the same principle as the sliding window except overwrites and seeks are not allowed. It thus requires two heads.
    internal class ConcurrentCircularQueue<T>
    {
        private readonly int[] _indexes;
        private readonly T[] _array;
        private int _writeHead;
        private int _readHead;

        public int Count 
        {
            get
            {
                return _writeHead - _readHead;
            }
        }

        public ConcurrentCircularQueue(int size)
        {
            _array = new T[size];
            _indexes = new int[size];

            for (int i = 0; i < _indexes.Length; i++)
            {
                _indexes[i] = i;
            }
        }

        public void Enqueue(T element)
        {
            while (!TryEnqueue(element))
            {
                Thread.SpinWait(1);
            }
        }

        public T Dequeue()
        {
            while (true)
            {
                if (TryDequeue(out T element))
                {
                    return element;
                }
            }
        }

        public bool TryEnqueue(T element)
        {
            // Cache writeHead and try to assign in a loop instead of pre incrementing writeHead in order to safe against buffer wraparounds

            while (true)
            {
                int position = _writeHead;

                int arrayIndex = NumberUtils.WrapMod(position, _array.Length);

                if (_indexes[arrayIndex] == _writeHead && Interlocked.CompareExchange(ref _writeHead, position + 1, position) == position)
                {
                    _array[arrayIndex] = element;

                    Thread.MemoryBarrier();
                    _indexes[arrayIndex] = position + 1;

                    return true;
                }
                else if (_indexes[arrayIndex] < position)
                {
                    // Overflow, it cannot be assigned as a forward enqueue did not occur
                    return false;
                }
            }
        }

        public bool TryDequeue(out T element)
        {
            // Cache readHead and try to read in a loop instead of pre incrementing readHead in order to safe against buffer wraparounds

            while (true)
            {
                int position = _readHead;

                int arrayIndex = NumberUtils.WrapMod(position, _array.Length);

                if (_indexes[arrayIndex] == _readHead + 1 && Interlocked.CompareExchange(ref _readHead, position + 1, position) == position)
                {
                    element = _array[arrayIndex];
                    _array[arrayIndex] = default(T);

                    Thread.MemoryBarrier();
                    _indexes[arrayIndex] = position + _array.Length;

                    return true;
                }
                else if (_indexes[arrayIndex] < position + 1)
                {
                    element = default(T);

                    return false;
                }
            }
        }
    }
}