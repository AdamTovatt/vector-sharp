using System.Runtime.CompilerServices;

namespace VectorSharp.Storage
{
    /// <summary>
    /// A min-heap optimized for top-K selection by similarity score.
    /// Uses separate arrays for items and priorities for better cache locality
    /// during priority comparisons.
    /// </summary>
    /// <typeparam name="T">The type of items stored in the heap.</typeparam>
    internal class MinHeap<T>
    {
        private readonly T[] _items;
        private readonly float[] _priorities;
        private int _count;
        private readonly int _maxSize;

        /// <summary>
        /// Initializes a new instance of the <see cref="MinHeap{T}"/> class with the specified maximum size.
        /// </summary>
        /// <param name="maxSize">The maximum number of items to keep in the heap.</param>
        public MinHeap(int maxSize)
        {
            _maxSize = maxSize;
            _items = new T[maxSize];
            _priorities = new float[maxSize];
            _count = 0;
        }

        /// <summary>
        /// Gets the minimum priority in the heap, or <see cref="float.MinValue"/> if the heap is empty.
        /// </summary>
        public float MinPriority
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _count > 0 ? _priorities[0] : float.MinValue;
        }

        /// <summary>
        /// Gets a value indicating whether the heap has reached its maximum capacity.
        /// </summary>
        public bool IsFull
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _count >= _maxSize;
        }

        /// <summary>
        /// Gets the current number of items in the heap.
        /// </summary>
        public int Count => _count;

        /// <summary>
        /// Adds an item with the given priority. If the heap is full, the item replaces
        /// the current minimum only if its priority is higher.
        /// </summary>
        /// <param name="item">The item to add.</param>
        /// <param name="priority">The priority (similarity score) of the item.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Add(T item, float priority)
        {
            if (_count < _maxSize)
            {
                _items[_count] = item;
                _priorities[_count] = priority;
                BubbleUp(_count);
                _count++;
            }
            else if (priority > _priorities[0])
            {
                _items[0] = item;
                _priorities[0] = priority;
                BubbleDown(0);
            }
        }

        /// <summary>
        /// Returns items in heap order (not sorted). Use <see cref="GetSortedDescending"/> for sorted results.
        /// </summary>
        /// <returns>A read-only span of the items in heap order.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ReadOnlySpan<T> GetItems()
        {
            return new ReadOnlySpan<T>(_items, 0, _count);
        }

        /// <summary>
        /// Returns items sorted by priority descending (highest priority first).
        /// </summary>
        /// <returns>An array of item-priority pairs sorted by priority descending.</returns>
        public (T Item, float Priority)[] GetSortedDescending()
        {
            (T Item, float Priority)[] result = new (T, float)[_count];
            for (int i = 0; i < _count; i++)
            {
                result[i] = (_items[i], _priorities[i]);
            }

            Array.Sort(result, (a, b) => b.Priority.CompareTo(a.Priority));
            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void BubbleUp(int index)
        {
            while (index > 0)
            {
                int parentIndex = (index - 1) / 2;
                if (_priorities[index] >= _priorities[parentIndex])
                    break;

                T tempItem = _items[index];
                _items[index] = _items[parentIndex];
                _items[parentIndex] = tempItem;

                float tempPriority = _priorities[index];
                _priorities[index] = _priorities[parentIndex];
                _priorities[parentIndex] = tempPriority;

                index = parentIndex;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void BubbleDown(int index)
        {
            while (true)
            {
                int leftChild = (2 * index) + 1;
                int rightChild = (2 * index) + 2;
                int smallest = index;

                if (leftChild < _count && _priorities[leftChild] < _priorities[smallest])
                    smallest = leftChild;

                if (rightChild < _count && _priorities[rightChild] < _priorities[smallest])
                    smallest = rightChild;

                if (smallest == index)
                    break;

                T tempItem = _items[index];
                _items[index] = _items[smallest];
                _items[smallest] = tempItem;

                float tempPriority = _priorities[index];
                _priorities[index] = _priorities[smallest];
                _priorities[smallest] = tempPriority;

                index = smallest;
            }
        }
    }
}
