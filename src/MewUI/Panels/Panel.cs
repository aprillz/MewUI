using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.Controls;

/// <summary>
/// Base class for layout panels that contain multiple children.
/// </summary>
public abstract class Panel : FrameworkElement
    , IVisualTreeHost
{
    private readonly List<Element> _children = new();

    protected override bool InvalidateOnMouseOverChanged => false;

    public bool ClipToBounds
    {
        get; set
        {
            if (Set(ref field, value))
            {
                InvalidateVisual();
            }
        }
    }

    /// <summary>
    /// Gets the collection of child elements.
    /// </summary>
    public IReadOnlyList<Element> Children => _children;

    /// <summary>
    /// Adds a child element to the panel.
    /// </summary>
    public void Add(Element child)
    {
        ArgumentNullException.ThrowIfNull(child);

        child.Parent = this;
        _children.Add(child);
        OnChildAdded(child);
        InvalidateMeasure();
    }

    /// <summary>
    /// Adds multiple children to the panel.
    /// </summary>
    public void AddRange(params Element[] children)
    {
        foreach (var child in children)
        {
            Add(child);
        }
    }

    /// <summary>
    /// Removes a child element from the panel.
    /// </summary>
    public bool Remove(Element child)
    {
        if (_children.Remove(child))
        {
            child.Parent = null;
            OnChildRemoved(child);
            InvalidateMeasure();
            return true;
        }
        return false;
    }

    /// <summary>
    /// Removes all children from the panel.
    /// </summary>
    public void Clear()
    {
        foreach (var child in _children)
        {
            child.Parent = null;
            OnChildRemoved(child);
        }
        _children.Clear();
        InvalidateMeasure();
    }

    /// <summary>
    /// Gets the child at the specified index.
    /// </summary>
    public Element this[int index] => _children[index];

    /// <summary>
    /// Gets the number of children.
    /// </summary>
    public int Count => _children.Count;

    /// <summary>
    /// Inserts a child at the specified index.
    /// </summary>
    public void Insert(int index, Element child)
    {
        ArgumentNullException.ThrowIfNull(child);

        child.Parent = this;
        _children.Insert(index, child);
        OnChildAdded(child);
        InvalidateMeasure();
    }

    /// <summary>
    /// Removes the child at the specified index.
    /// </summary>
    public void RemoveAt(int index)
    {
        var child = _children[index];
        _children.RemoveAt(index);
        child.Parent = null;
        OnChildRemoved(child);
        InvalidateMeasure();
    }

    /// <summary>
    /// Called when a child is added.
    /// </summary>
    protected virtual void OnChildAdded(Element child) { }

    /// <summary>
    /// Called when a child is removed.
    /// </summary>
    protected virtual void OnChildRemoved(Element child) { }

    public override void Render(IGraphicsContext context)
    {
        base.Render(context);

        if (ClipToBounds)
        {
            context.Save();
            context.SetClip(Bounds);
        }
        try
        {
            foreach (var child in _children)
            {
                child.Render(context);
            }
        }
        finally
        {
            if (ClipToBounds)
            {
                context.Restore();
            }
        }
    }

    protected override UIElement? OnHitTest(Point point)
    {
        if (!IsVisible || !IsHitTestVisible || !IsEffectivelyEnabled)
        {
            return null;
        }

        // Hit test children in reverse order (top to bottom in visual order)
        for (int i = _children.Count - 1; i >= 0; i--)
        {
            if (_children[i] is UIElement uiChild)
            {
                var result = uiChild.HitTest(point);
                if (result != null)
                {
                    return result;
                }
            }
        }

        // Then check self
        if (Bounds.Contains(point))
        {
            return this;
        }

        return null;
    }

    bool IVisualTreeHost.VisitChildren(Func<Element, bool> visitor)
    {
        for (int i = 0; i < _children.Count; i++)
        {
            if (!visitor(_children[i])) return false;
        }
        return true;
    }
}
