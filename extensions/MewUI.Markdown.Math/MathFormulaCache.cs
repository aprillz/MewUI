namespace Aprillz.MewUI.Markdown;

/// <summary>
/// Keeps typeset formulas by source and style, so a document built again after an edit reuses the layouts and
/// recorded geometry of the formulas it still contains; the least recently used one is dropped past the capacity.
/// </summary>
internal sealed class MathFormulaCache
{
    internal const int DEFAULT_CAPACITY = 256;

    private readonly int _capacity;
    private readonly Dictionary<(string Source, bool Display), LinkedListNode<CachedFormula>> _formulas = new();
    private readonly LinkedList<CachedFormula> _recent = new();
    private readonly object _gate = new();

    public MathFormulaCache(int capacity = DEFAULT_CAPACITY)
    {
        _capacity = Math.Max(1, capacity);
    }

    /// <summary>Gets how many formulas are kept.</summary>
    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _formulas.Count;
            }
        }
    }

    /// <summary>Returns the kept formula for the source and style, creating it when there is none.</summary>
    public MathFormula Get(string source, bool display)
    {
        lock (_gate)
        {
            (string, bool) key = (source, display);
            if (_formulas.TryGetValue(key, out LinkedListNode<CachedFormula>? node))
            {
                _recent.Remove(node);
                _recent.AddLast(node);
                return node.Value.Formula;
            }

            if (_formulas.Count >= _capacity)
            {
                LinkedListNode<CachedFormula> oldest = _recent.First!;
                _recent.RemoveFirst();
                _formulas.Remove(oldest.Value.Key);
            }

            MathFormula formula = new(source, display);
            _formulas[key] = _recent.AddLast(new CachedFormula(key, formula));
            return formula;
        }
    }

    private readonly record struct CachedFormula((string Source, bool Display) Key, MathFormula Formula);
}
