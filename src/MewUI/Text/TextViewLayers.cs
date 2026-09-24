namespace Aprillz.MewUI.Text;

/// <summary>
/// One entry of a text view's draw order. Layers paint only; input reaches elements through the
/// host, so a layer never takes part in hit testing.
/// </summary>
public interface ITextViewLayer
{
    /// <summary>
    /// Paints this layer. A host may cache the result, so this is not guaranteed to run once per
    /// frame; a layer whose appearance changed must call
    /// <see cref="ITextViewHost.InvalidateLayer"/> rather than wait for the next call.
    /// </summary>
    void Draw(ITextRenderContext context, Rect viewportBounds);
}

/// <summary>Where a layer goes relative to the anchor it is inserted against.</summary>
public enum TextLayerPosition
{
    /// <summary>Under the anchor.</summary>
    Below,

    /// <summary>In place of the anchor, which stops drawing.</summary>
    Replace,

    /// <summary>Over the anchor.</summary>
    Above
}

/// <summary>
/// Draw order of a text view: the four built-in anchors plus whatever an extension inserted around
/// them. The host paints an anchor's own content when it is still present, so replacing one hands
/// that drawing to the caller.
/// </summary>
public sealed class TextViewLayerStack
{
    private readonly List<Entry> _entries;
    private ITextViewLayer[]? _layers;

    /// <summary>
    /// Creates the stack with the host's own drawing already in it. The host passes one layer per
    /// anchor, so its background, selection, glyph and caret passes are ordinary entries rather
    /// than a callback the stack has to ask for.
    /// </summary>
    public TextViewLayerStack(Func<TextViewLayerAnchor, ITextViewLayer> createBuiltIn)
    {
        ArgumentNullException.ThrowIfNull(createBuiltIn);
        _entries = new List<Entry>(4);
        foreach (var anchor in Enum.GetValues<TextViewLayerAnchor>())
        {
            _entries.Add(new Entry(anchor, createBuiltIn(anchor), IsAnchor: true));
        }
    }

    /// <summary>Raised when the order changed, so the host can repaint.</summary>
    public event Action? Changed;

    /// <summary>Layers in draw order, the host's built-in ones included.</summary>
    public IReadOnlyList<ITextViewLayer> Layers
        => _layers ??= _entries.Select(entry => entry.Layer).ToArray();

    /// <summary>Inserts <paramref name="layer"/> relative to <paramref name="anchor"/>.</summary>
    public void Insert(ITextViewLayer layer, TextViewLayerAnchor anchor, TextLayerPosition position)
    {
        ArgumentNullException.ThrowIfNull(layer);
        int index = FindAnchor(anchor);
        switch (position)
        {
            case TextLayerPosition.Below:
                _entries.Insert(index, new Entry(anchor, layer, IsAnchor: false));
                break;
            case TextLayerPosition.Replace:
                _entries[index] = new Entry(anchor, layer, IsAnchor: true);
                break;
            default:
                _entries.Insert(index + 1, new Entry(anchor, layer, IsAnchor: false));
                break;
        }
        _layers = null;
        Changed?.Invoke();
    }

    /// <summary>Draws every layer in order.</summary>
    public void Draw(ITextRenderContext context, Rect viewportBounds)
    {
        ArgumentNullException.ThrowIfNull(context);
        foreach (var entry in _entries)
        {
            entry.Layer.Draw(context, viewportBounds);
        }
    }

    /// <summary>
    /// Draws the layers of one anchor's group: those inserted below it, its own, those above. An insert
    /// lands next to its anchor, so the groups follow each other in anchor order and drawing them one
    /// after another draws the whole stack.
    /// </summary>
    internal void Draw(ITextRenderContext context, Rect viewportBounds, TextViewLayerAnchor anchor)
    {
        foreach (var entry in _entries)
        {
            if (entry.Anchor == anchor)
            {
                entry.Layer.Draw(context, viewportBounds);
            }
        }
    }

    private int FindAnchor(TextViewLayerAnchor anchor)
    {
        for (int index = 0; index < _entries.Count; index++)
        {
            if (_entries[index].IsAnchor && _entries[index].Anchor == anchor)
            {
                return index;
            }
        }
        return _entries.Count - 1;
    }

    // A replaced anchor keeps IsAnchor so later inserts still find the position by name.
    private readonly record struct Entry(TextViewLayerAnchor Anchor, ITextViewLayer Layer, bool IsAnchor);
}
