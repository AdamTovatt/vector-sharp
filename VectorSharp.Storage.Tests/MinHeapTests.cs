namespace VectorSharp.Storage.Tests
{
    [TestClass]
    public class MinHeapTests
    {
        [TestMethod]
        public void Add_SingleItem_ReturnsIt()
        {
            MinHeap<string> heap = new MinHeap<string>(5);
            heap.Add("item1", 0.5f);

            Assert.AreEqual(1, heap.Count);
            ReadOnlySpan<string> items = heap.GetItems();
            Assert.AreEqual("item1", items[0]);
        }

        [TestMethod]
        public void Add_BelowCapacity_ReturnsAllItems()
        {
            MinHeap<string> heap = new MinHeap<string>(5);
            heap.Add("a", 0.1f);
            heap.Add("b", 0.2f);
            heap.Add("c", 0.3f);

            Assert.AreEqual(3, heap.Count);
            Assert.IsFalse(heap.IsFull);
        }

        [TestMethod]
        public void Add_AtCapacity_ReplacesMinWhenHigherPriority()
        {
            MinHeap<string> heap = new MinHeap<string>(3);
            heap.Add("a", 0.1f);
            heap.Add("b", 0.2f);
            heap.Add("c", 0.3f);

            Assert.IsTrue(heap.IsFull);

            // Add item with higher priority than the current minimum
            heap.Add("d", 0.5f);

            Assert.AreEqual(3, heap.Count);

            // The minimum should no longer be 0.1
            Assert.IsTrue(heap.MinPriority >= 0.2f);
        }

        [TestMethod]
        public void Add_AtCapacity_IgnoresWhenLowerPriority()
        {
            MinHeap<string> heap = new MinHeap<string>(3);
            heap.Add("a", 0.3f);
            heap.Add("b", 0.4f);
            heap.Add("c", 0.5f);

            float minBefore = heap.MinPriority;

            // Add item with priority lower than the minimum
            heap.Add("d", 0.1f);

            Assert.AreEqual(3, heap.Count);
            Assert.AreEqual(minBefore, heap.MinPriority);
        }

        [TestMethod]
        public void GetSortedDescending_ReturnsSortedByPriorityDescending()
        {
            MinHeap<string> heap = new MinHeap<string>(5);
            heap.Add("low", 0.1f);
            heap.Add("high", 0.9f);
            heap.Add("mid", 0.5f);
            heap.Add("midhigh", 0.7f);
            heap.Add("midlow", 0.3f);

            (string Item, float Priority)[] sorted = heap.GetSortedDescending();

            Assert.AreEqual(5, sorted.Length);
            Assert.AreEqual("high", sorted[0].Item);
            Assert.AreEqual(0.9f, sorted[0].Priority);
            Assert.AreEqual("midhigh", sorted[1].Item);
            Assert.AreEqual("mid", sorted[2].Item);
            Assert.AreEqual("midlow", sorted[3].Item);
            Assert.AreEqual("low", sorted[4].Item);
        }

        [TestMethod]
        public void MinPriority_EmptyHeap_ReturnsMinValue()
        {
            MinHeap<string> heap = new MinHeap<string>(5);

            Assert.AreEqual(float.MinValue, heap.MinPriority);
        }

        [TestMethod]
        public void MinPriority_ReturnsSmallestPriority()
        {
            MinHeap<string> heap = new MinHeap<string>(5);
            heap.Add("a", 0.5f);
            heap.Add("b", 0.2f);
            heap.Add("c", 0.8f);

            Assert.AreEqual(0.2f, heap.MinPriority);
        }

        [TestMethod]
        public void IsFull_ReturnsTrueAtCapacity()
        {
            MinHeap<string> heap = new MinHeap<string>(2);
            Assert.IsFalse(heap.IsFull);

            heap.Add("a", 0.1f);
            Assert.IsFalse(heap.IsFull);

            heap.Add("b", 0.2f);
            Assert.IsTrue(heap.IsFull);
        }

        [TestMethod]
        public void Empty_ReturnsNoItems()
        {
            MinHeap<string> heap = new MinHeap<string>(5);

            Assert.AreEqual(0, heap.Count);
            Assert.AreEqual(0, heap.GetItems().Length);
            Assert.AreEqual(0, heap.GetSortedDescending().Length);
        }

        [TestMethod]
        public void Capacity_One_WorksCorrectly()
        {
            MinHeap<string> heap = new MinHeap<string>(1);
            heap.Add("first", 0.3f);
            Assert.IsTrue(heap.IsFull);

            heap.Add("second", 0.5f);
            Assert.AreEqual(1, heap.Count);
            Assert.AreEqual("second", heap.GetItems()[0]);

            heap.Add("third", 0.1f);
            Assert.AreEqual(1, heap.Count);
            Assert.AreEqual("second", heap.GetItems()[0]);
        }

        [TestMethod]
        public void LargeCapacity_MaintainsCorrectTopK()
        {
            int capacity = 10;
            MinHeap<int> heap = new MinHeap<int>(capacity);

            // Add 100 items with priorities 0-99
            for (int i = 0; i < 100; i++)
            {
                heap.Add(i, i);
            }

            Assert.AreEqual(capacity, heap.Count);

            // The top 10 should be items 90-99
            (int Item, float Priority)[] sorted = heap.GetSortedDescending();
            Assert.AreEqual(10, sorted.Length);

            for (int i = 0; i < 10; i++)
            {
                Assert.AreEqual(99 - i, sorted[i].Item);
                Assert.AreEqual(99 - i, sorted[i].Priority);
            }
        }
    }
}
