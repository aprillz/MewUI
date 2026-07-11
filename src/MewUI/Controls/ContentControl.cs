using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.Controls;

/// <summary>
/// A control that contains a single child element.
/// </summary>
public class ContentControl : Control
    , IVisualTreeHost
    , ILogicalTreeHost
{
    public static readonly MewProperty<Element?> ContentProperty =
        MewProperty<Element?>.Register<ContentControl>(nameof(Content), null,
            MewPropertyOptions.AffectsLayout,
            static (self, oldValue, newValue) => self.OnContentChanged(oldValue, newValue),
            validate: static (self, value) => self.ValidateContent(value));

    /// <summary>
    /// Gets or sets the content element.
    /// </summary>
    public Element? Content
    {
        get => GetValue(ContentProperty);
        set => SetValue(ContentProperty, value);
    }

    /// <summary>
    /// Rejects an invalid Content candidate before the value is committed.
    /// Derived classes add their own slot rules (e.g. an element cannot occupy two slots).
    /// </summary>
    /// <param name="candidate">The proposed content; null is always valid.</param>
    protected virtual void ValidateContent(Element? candidate)
        => ValidateLogicalChild(candidate);

    protected virtual void OnContentChanged(Element? oldValue, Element? newValue)
    {
        if (HasTemplateInstance)
        {
            // Templated: the control keeps logical ownership only; a ContentPresenter
            // in the template owns the visual attach.
            if (oldValue != null)
            {
                DetachLogicalChild(oldValue);
            }
            if (newValue != null)
            {
                AttachLogicalChild(newValue);
            }
            RefreshTemplatePresenters(ContentProperty);
        }
        else
        {
            ChangeLogicalChild(oldValue, newValue);
        }
    }

    protected override void OnLogicalChildTaken(Element child)
    {
        base.OnLogicalChildTaken(child);

        // A transfer-permitting host adopted our content: clear the slot so the record
        // does not keep pointing at a child that lives elsewhere now.
        if (ReferenceEquals(Content, child))
        {
            Content = null;
        }
    }

    private protected override void OnTemplateInstanceAttached()
    {
        base.OnTemplateInstanceAttached();

        // Release the compat visual link when no presenter took the content over;
        // the logical child stays owned without a visual position (invariant).
        var content = Content;
        if (content != null && content.Parent == this)
        {
            content.Parent = null;
        }
    }

    private protected override void OnTemplateInstanceDetached()
    {
        base.OnTemplateInstanceDetached();

        // Back on the non-template path: the control hosts its content visually again.
        var content = Content;
        if (content != null && content.Parent == null)
        {
            content.Parent = this;
        }
    }

    protected override Size MeasureContent(Size availableSize)
    {
        if (HasTemplateInstance)
        {
            return base.MeasureContent(availableSize);
        }

        if (Content == null)
        {
            return Size.Empty;
        }

        // Subtract padding
        var contentSize = availableSize.Deflate(Padding);

        Content.Measure(contentSize);
        return Content.DesiredSize.Inflate(Padding);
    }

    protected override void ArrangeContent(Rect bounds)
    {
        if (HasTemplateInstance)
        {
            base.ArrangeContent(bounds);
            return;
        }

        if (Content == null)
        {
            return;
        }

        // Arrange within padding
        var contentBounds = bounds.Deflate(Padding);
        Content.Arrange(contentBounds);
    }

    protected override void RenderSubtree(IGraphicsContext context)
    {
        if (HasTemplateInstance)
        {
            base.RenderSubtree(context);
            return;
        }

        Content?.Render(context);
    }

    bool IVisualTreeHost.VisitChildren(Func<Element, bool> visitor)
    {
        var templateRoot = TemplateVisualRoot;
        if (templateRoot != null)
        {
            return visitor(templateRoot);
        }

        return Content == null || visitor(Content);
    }

    bool ILogicalTreeHost.VisitLogicalChildren(Func<Element, bool> visitor)
        => Content == null || visitor(Content);
}
