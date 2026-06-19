namespace NovelEngine.Editor;

internal sealed class BoundedCache<TKey, TValue>
    where TKey : notnull
{
    private readonly int _capacity;
    private readonly Dictionary<TKey, LinkedListNode<Entry>> _entries;
    private readonly LinkedList<Entry> _recentEntries = [];

    public BoundedCache(int capacity, IEqualityComparer<TKey>? comparer = null)
    {
        if (capacity <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(capacity),
                "Cache capacity must be positive.");
        }

        _capacity = capacity;
        _entries = new Dictionary<TKey, LinkedListNode<Entry>>(comparer);
    }

    public int Count => _entries.Count;

    public void Clear()
    {
        _entries.Clear();
        _recentEntries.Clear();
    }

    public bool TryGetValue(TKey key, out TValue value)
    {
        if (!_entries.TryGetValue(key, out var node))
        {
            value = default!;
            return false;
        }

        _recentEntries.Remove(node);
        _recentEntries.AddFirst(node);
        value = node.Value.Value;
        return true;
    }

    public void Set(TKey key, TValue value)
    {
        if (_entries.TryGetValue(key, out var existing))
        {
            existing.Value = new Entry(key, value);
            _recentEntries.Remove(existing);
            _recentEntries.AddFirst(existing);
            return;
        }

        var node = new LinkedListNode<Entry>(new Entry(key, value));
        _entries[key] = node;
        _recentEntries.AddFirst(node);
        Trim();
    }

    private void Trim()
    {
        while (_entries.Count > _capacity && _recentEntries.Last is not null)
        {
            var node = _recentEntries.Last;
            _recentEntries.RemoveLast();
            _entries.Remove(node.Value.Key);
        }
    }

    private readonly record struct Entry(TKey Key, TValue Value);
}
