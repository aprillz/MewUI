using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.Controls;

public class ControlTemplate : Control, IVisualTreeHost
{
    private Element? _content;
    private Element? _root;
    private Element? _attachedVisualRoot;

    public Element? Child
    {
        get => Content;
        set => Content = value;
    }

    public Element? Content
    {
        get => _content;
        set
        {
            if (ReferenceEquals(_content, value))
            {
                return;
            }

            if (ReferenceEquals(value, this))
            {
                throw new InvalidOperationException("Cannot set Content to self.");
            }

            var oldValue = _content;
            _content = value;
            OnContentChanged(oldValue, value);
        }
    }

    protected Element? Root
    {
        get => _root;
        set
        {
            if (ReferenceEquals(_root, value))
            {
                return;
            }

            if (ReferenceEquals(value, this))
            {
                throw new InvalidOperationException("Cannot set Root to self.");
            }

            if (_root != null && _root.Parent == this)
            {
                _root.Parent = null;
            }

            _root = value;
            EnsureVisualRootAttached();
        }
    }

    protected virtual void OnContentChanged(Element? oldValue, Element? newValue)
    {
        if (Root != null)
        {
            return;
        }

        if (oldValue != null && oldValue.Parent == this)
        {
            oldValue.Parent = null;
        }

        if (newValue != null)
        {
            newValue.Parent = this;
        }
    }

    protected virtual Element? GetVisualRoot() => Root ?? Content;

    protected Element? EnsureVisualRootAttached()
    {
        var root = GetVisualRoot();
        if (ReferenceEquals(_attachedVisualRoot, root))
        {
            return root;
        }

        if (_attachedVisualRoot != null && _attachedVisualRoot.Parent == this)
        {
            _attachedVisualRoot.Parent = null;
        }

        _attachedVisualRoot = root;
        if (root != null && root.Parent == null)
        {
            root.Parent = this;
        }

        return root;
    }

    protected override Size MeasureContent(Size availableSize)
    {
        var root = EnsureVisualRootAttached();
        if (root == null)
        {
            return Size.Empty;
        }

        root.Measure(availableSize);
        return root.DesiredSize;
    }

    protected override void ArrangeContent(Rect bounds)
    {
        EnsureVisualRootAttached()?.Arrange(bounds);
    }

    protected override void RenderSubtree(IGraphicsContext context)
    {
        EnsureVisualRootAttached()?.Render(context);
    }

    protected override UIElement? OnHitTest(Point point)
    {
        if (!IsVisible || !IsHitTestVisible || !IsEffectivelyEnabled)
        {
            return null;
        }

        if (EnsureVisualRootAttached() is UIElement uiRoot)
        {
            var hit = uiRoot.HitTest(point);
            if (hit != null)
            {
                return hit;
            }
        }

        return Bounds.Contains(point) ? this : null;
    }

    bool IVisualTreeHost.VisitChildren(Func<Element, bool> visitor)
    {
        var root = EnsureVisualRootAttached();
        return root == null || visitor(root);
    }
}
