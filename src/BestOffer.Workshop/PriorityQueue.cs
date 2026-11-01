namespace DeepSpaceBestOffer;

// Unity's .NET Standard profile predates PriorityQueue. The solver supplies a
// unique monotonic final tie-breaker, so heap arity cannot change dequeue order.
internal sealed class PriorityQueue<T, TPriority>
{
    readonly List<(T Item, TPriority Priority)> heap = new();
    static readonly Comparer<TPriority> comparer = Comparer<TPriority>.Default;
    public int Count => heap.Count;
    public void Enqueue(T item, TPriority priority)
    {
        int index = heap.Count;
        heap.Add((item, priority));
        while (index > 0)
        {
            int parent = (index - 1) / 2;
            if (comparer.Compare(priority, heap[parent].Priority) >= 0) break;
            heap[index] = heap[parent];
            index = parent;
        }
        heap[index] = (item, priority);
    }
    public bool TryDequeue(out T item, out TPriority priority)
    {
        if (heap.Count == 0) { item = default!; priority = default!; return false; }
        (item, priority) = heap[0];
        var tail = heap[heap.Count - 1];
        heap.RemoveAt(heap.Count - 1);
        if (heap.Count == 0) return true;
        int index = 0;
        while (index * 2 + 1 < heap.Count)
        {
            int child = index * 2 + 1;
            if (child + 1 < heap.Count && comparer.Compare(heap[child + 1].Priority, heap[child].Priority) < 0) child++;
            if (comparer.Compare(tail.Priority, heap[child].Priority) <= 0) break;
            heap[index] = heap[child];
            index = child;
        }
        heap[index] = tail;
        return true;
    }
}
