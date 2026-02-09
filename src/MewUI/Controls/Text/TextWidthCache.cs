using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.Controls.Text;

/// <summary>
/// Small bounded cache for measured text widths (NoWrap).
/// Intended for controls like ListBox/ComboBox that can measure many items per layout pass.
/// </summary>
internal sealed class TextWidthCache
{
    private readonly record struct Key(string Text, string Family, double Size, FontWeight Weight, uint Dpi);

    private readonly Dictionary<Key, Entry> _entries = new();
    private readonly LinkedList<Key> _lru = new();
    private int _capacity;

    private sealed class Entry
    {
        public double Width { get; set; }
        public LinkedListNode<Key> Node { get; }

        public Entry(double width, LinkedListNode<Key> node)
        {
            Width = width;
            Node = node;
        }
    }

    public TextWidthCache(int capacity = 256)
    {
        _capacity = Math.Max(0, capacity);
    }

    public void Clear()
    {
        _entries.Clear();
        _lru.Clear();
    }

    public void SetCapacity(int capacity)
    {
        _capacity = Math.Max(0, capacity);
        TrimToCapacity();
    }

    public double GetOrMeasure(IGraphicsContext context, IFont font, uint dpi, string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return 0;
        }

        var key = new Key(text, font.Family, font.Size, font.Weight, dpi);
        if (_entries.TryGetValue(key, out var entry))
        {
            Touch(entry.Node);
            return entry.Width;
        }

        double width = context.MeasureText(text, font).Width;
        Add(key, width);
        return width;
    }

    private void Add(Key key, double width)
    {
        if (_capacity == 0)
        {
            return;
        }

        if (_entries.TryGetValue(key, out var existing))
        {
            existing.Width = width;
            Touch(existing.Node);
            return;
        }

        var node = _lru.AddFirst(key);
        _entries[key] = new Entry(width, node);
        TrimToCapacity();
    }

    private void Touch(LinkedListNode<Key> node)
    {
        if (node.List != _lru || node == _lru.First)
        {
            return;
        }

        _lru.Remove(node);
        _lru.AddFirst(node);
    }

    private void TrimToCapacity()
    {
        while (_entries.Count > _capacity && _lru.Last != null)
        {
            var key = _lru.Last.Value;
            _lru.RemoveLast();
            _entries.Remove(key);
        }
    }
}

