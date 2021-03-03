using System;
using Ruffles.Memory;
using Ruffles.Utils;

namespace Ruffles.Collections
{
    internal class HeapableFixedDictionary<T> where T : IMemoryReleasable
    {
        private struct Element
        {
            public int Index;
            public T Value;
        }

        private readonly Element[] _array;

        private readonly MemoryManager _memoryManager;

        public HeapableFixedDictionary(int size, MemoryManager memoryManager)
        {
            _array = new Element[size];

            for (int i = 0; i < _array.Length; i++)
            {
                _array[i] = new Element()
                {
                    Index = -1,
                    Value = default(T)
                };
            }

            _memoryManager = memoryManager;
        }

        public bool CanSet(int index)
        {
            return _array[NumberUtils.WrapMod(index, _array.Length)].Index == -1;
        }

        public bool CanUpdateOrSet(int index)
        {
            int currentIndex = _array[NumberUtils.WrapMod(index, _array.Length)].Index;

            return currentIndex == -1 || currentIndex == index;
        }

        public void Set(int index, T value)
        {
            int arrayIndex = NumberUtils.WrapMod(index, _array.Length);
            int currentIndex = _array[arrayIndex].Index;

            if (currentIndex != -1)
            {
                throw new ArgumentOutOfRangeException(nameof(index), index, "Cannot set a when value already exists");
            }

            _array[arrayIndex] = new Element()
            {
                Index = index,
                Value = value
            };
        }

        public void Update(int index, T value)
        {
            int arrayIndex = NumberUtils.WrapMod(index, _array.Length);
            int currentIndex = _array[arrayIndex].Index;

            if (currentIndex != index)
            {
                throw new ArgumentOutOfRangeException(nameof(index), index, "Cannot update when value is not the same");
            }

            _array[arrayIndex] = new Element()
            {
                Index = index,
                Value = value
            };
        }

        public void Remove(int index)
        {
            int arrayIndex = NumberUtils.WrapMod(index, _array.Length);

            if (_array[arrayIndex].Index != index)
            {
                throw new ArgumentOutOfRangeException(nameof(index), index, "Cannot remove wrapped index");
            }

            _array[arrayIndex] = new Element()
            {
                Index = -1,
                Value = default(T)
            };
        }

        public bool Contains(int index)
        {
            return _array[NumberUtils.WrapMod(index, _array.Length)].Index == index;
        }

        public bool TryGet(int index, out T value)
        {
            int arrayIndex = NumberUtils.WrapMod(index, _array.Length);

            if (_array[arrayIndex].Index == index)
            {
                value = _array[arrayIndex].Value;
                return true;
            }

            value = default(T);
            return false;
        }

        public void Release()
        {
            for (int i = 0; i < _array.Length; i++)
            {
                if (_array[i].Value != null && _array[i].Value.IsAlloced)
                {
                    _array[i].Value.DeAlloc(_memoryManager);
                }

                _array[i] = new Element()
                {
                    Index = -1,
                    Value = default(T)
                };
            }
        }
    }
}
