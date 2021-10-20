using System.Collections.Generic;
using Ruffles.Utils;

namespace Ruffles.Collections
{
    internal class SlidingSet<T>
    {
        private struct Element
        {
            public bool IsSet;
            public T Value;
        }

        private readonly Element[] _array;
        private readonly HashSet<T> _set = new HashSet<T>();
        private int _head;

        public SlidingSet(int size)
        {
            _array = new Element[size];

            for (int i = 0; i < _array.Length; i++)
            {
                _array[i] = new Element()
                {
                    IsSet = false,
                    Value = default(T)
                };
            }
        }

        public bool TrySet(T value)
        {
            if (_set.Contains(value))
            {
                return false;
            }

            int arrayIndex = NumberUtils.WrapMod(_head, _array.Length);

            if (_array[arrayIndex].IsSet)
            {
                _set.Remove(_array[arrayIndex].Value);
            }

            _array[arrayIndex] = new Element()
            {
                IsSet = true,
                Value = value
            };

            _set.Add(value);

            _head++;

            return true;
        }
    }
}
