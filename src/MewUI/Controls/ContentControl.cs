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

    private static readonly MewPropertyKey<Element?> EffectiveContentPropertyKey =
        MewProperty<Element?>.RegisterReadOnly<ContentControl>(nameof(EffectiveContent), null,
            MewPropertyOptions.AffectsLayout);

    /// <summary>
    /// The element this control displays, which a derived class may substitute for
    /// <see cref="Content"/>. A template's <see cref="ContentPresenter"/> projects this slot.
    /// </summary>
    public static readonly MewProperty<Element?> EffectiveContentProperty =
        EffectiveContentPropertyKey.Property;

    // SelectEffectiveContent is virtual, so the first evaluation is deferred out of the constructor
    // to the first property change or measure, whichever comes first.
    private bool _effectiveContentInitialized;

    /// <summary>
    /// Gets or sets the content element.
    /// </summary>
    public Element? Content
    {
        get => GetValue(ContentProperty);
        set => SetValue(ContentProperty, value);
    }

    /// <summary>
    /// Gets the element this control displays. Equals <see cref="Content"/> unless a derived class
    /// substitutes another element.
    /// </summary>
    public Element? EffectiveContent => GetValue(EffectiveContentProperty);

    private protected override MewProperty<Element?>? DefaultContentSource => EffectiveContentProperty;

    /// <summary>
    /// Chooses the element to display. The default is <see cref="Content"/>; override to supply a
    /// substitute when the caller set no content.
    /// </summary>
    protected virtual Element? SelectEffectiveContent() => Content;

    /// <summary>
    /// Re-runs <see cref="SelectEffectiveContent"/> after one of its inputs changed.
    /// </summary>
    protected void InvalidateEffectiveContent()
    {
        _effectiveContentInitialized = true;
        CommitEffectiveContent();
    }

    private void EnsureEffectiveContent()
    {
        if (!_effectiveContentInitialized)
        {
            InvalidateEffectiveContent();
        }
    }

    private void CommitEffectiveContent()
    {
        var previous = EffectiveContent;
        var next = SelectEffectiveContent();
        if (ReferenceEquals(previous, next))
        {
            return;
        }

        // Exactly one host holds the visual link: a template's presenter, or this control.
        // Release the outgoing element before the incoming one claims it.
        if (previous != null && previous.Parent == this)
        {
            previous.Parent = null;
        }

        SetValue(EffectiveContentPropertyKey, next);

        if (HasTemplateInstance)
        {
            RefreshTemplatePresenters(EffectiveContentProperty);
        }
        else if (next != null)
        {
            next.Parent = this;
        }
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        // Before base: Control.ApplyTemplate wires presenters against the slot, so it must be filled.
        EnsureEffectiveContent();
        return base.MeasureOverride(availableSize);
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
        // Content is the logical slot only; the visual attach follows EffectiveContent, which may
        // hold a substitute rather than this value.
        if (oldValue != null)
        {
            DetachLogicalChild(oldValue);
        }

        if (newValue != null)
        {
            if (newValue.LogicalParent != this)
            {
                newValue.DetachFromCurrentLogicalOwner();
            }

            AttachLogicalChild(newValue);
        }

        InvalidateEffectiveContent();

        // A template may project the raw slot instead of the displayed one.
        RefreshTemplatePresenters(ContentProperty);
    }

    protected override void OnValueSourceChanged(MewProperty property)
    {
        base.OnValueSourceChanged(property);

        // The displayed element can depend on whether anyone supplied Content at all, so a tier
        // transition matters even when the value stayed equal (assigning null over an unset slot,
        // or clearing that assignment again).
        if (property.Id == ContentProperty.Id)
        {
            InvalidateEffectiveContent();
            RefreshTemplatePresenters(ContentProperty);
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
        var displayed = EffectiveContent;
        if (displayed != null && displayed.Parent == this)
        {
            displayed.Parent = null;
        }
    }

    private protected override void OnTemplateInstanceDetached()
    {
        base.OnTemplateInstanceDetached();

        // Back on the non-template path: the control hosts its content visually again.
        var displayed = EffectiveContent;
        if (displayed != null && displayed.Parent == null)
        {
            displayed.Parent = this;
        }
    }

    protected override Size MeasureContent(Size availableSize)
    {
        if (HasTemplateInstance)
        {
            return base.MeasureContent(availableSize);
        }

        var displayed = EffectiveContent;
        if (displayed == null)
        {
            return Size.Empty;
        }

        // The border is drawn inside the element box, so it takes space from the content the same way
        // padding does; leaving it out of the measure let it eat into the padding instead.
        var borderInset = GetBorderVisualInset();
        var contentSize = availableSize.Deflate(Padding).Deflate(borderInset);

        displayed.Measure(contentSize);
        return displayed.DesiredSize.Inflate(Padding).Inflate(borderInset);
    }

    protected override void ArrangeContent(Rect bounds)
    {
        if (HasTemplateInstance)
        {
            base.ArrangeContent(bounds);
            return;
        }

        var displayed = EffectiveContent;
        if (displayed == null)
        {
            return;
        }

        var contentBounds = bounds.Deflate(Padding).Deflate(GetBorderVisualInset());
        displayed.Arrange(contentBounds);
    }

    protected override void RenderSubtree(IGraphicsContext context)
    {
        if (HasTemplateInstance)
        {
            base.RenderSubtree(context);
            return;
        }

        EffectiveContent?.Render(context);
    }

    bool IVisualTreeHost.VisitChildren(Func<Element, bool> visitor)
    {
        var templateRoot = TemplateVisualRoot;
        if (templateRoot != null)
        {
            return visitor(templateRoot);
        }

        var displayed = EffectiveContent;
        return displayed == null || visitor(displayed);
    }

    bool ILogicalTreeHost.VisitLogicalChildren(Func<Element, bool> visitor)
        => Content == null || visitor(Content);
}
