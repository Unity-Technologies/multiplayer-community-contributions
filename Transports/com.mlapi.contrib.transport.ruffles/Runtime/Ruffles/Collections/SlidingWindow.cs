using Ruffles.Utils;

namespace Ruffles.Collections
{
    internal class SlidingWindow<T>
    {
        private struct Element
        {
            public int Index;
            public T Value;
        }

        private readonly Element[] _array;

        public SlidingWindow(int size)
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
        }

        public void Set(int index, T value)
        {
            int arrayIndex = NumberUtils.WrapMod(index, _array.Length);
            int currentIndex = _array[arrayIndex].Index;

            _array[arrayIndex] = new Element()
            {
                Index = index,
                Value = value
            };
        }

        public void Unset(int index)
        {
            int arrayIndex = NumberUtils.WrapMod(index, _array.Length);

            if (_array[arrayIndex].Index == index)
            {
                _array[arrayIndex] = new Element()
                {
                    Index = -1,
                    Value = default(T)
                };
            }
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
    }
}
