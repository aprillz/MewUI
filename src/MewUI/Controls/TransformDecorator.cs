using System.Numerics;

namespace Aprillz.MewUI.Controls;

/// <summary>
/// Specifies whether a <see cref="TransformDecorator"/> transform takes part in layout.
/// </summary>
public enum TransformMode
{
    /// <summary>Layout uses the untransformed child; the transform applies when drawing.</summary>
    Render,

    /// <summary>Layout uses the bounding box of the transformed child; translation is ignored.</summary>
    Layout,
}

/// <summary>
/// Hosts a single child drawn under an affine transform. Hit testing and coordinate conversion
/// (<see cref="Element.TranslatePoint"/>, mouse positions, popup anchors) follow the transform.
/// </summary>
public sealed class TransformDecorator : FrameworkElement, IVisualTreeHost
{
    public static readonly MewProperty<UIElement?> ChildProperty =
        MewProperty<UIElement?>.Register<TransformDecorator>(nameof(Child), null,
            MewPropertyOptions.AffectsLayout,
            static (self, oldValue, newValue) => self.OnChildChanged(oldValue, newValue));

    public static readonly MewProperty<Matrix3x2> TransformProperty =
        MewProperty<Matrix3x2>.Register<TransformDecorator>(nameof(Transform), Matrix3x2.Identity,
            MewPropertyOptions.AffectsRender,
            static (self, _, _) => self.OnTransformChanged());

    public static readonly MewProperty<Point> TransformOriginProperty =
        MewProperty<Point>.Register<TransformDecorator>(nameof(TransformOrigin), new Point(0.5, 0.5),
            MewPropertyOptions.AffectsRender);

    public static readonly MewProperty<TransformMode> ModeProperty =
        MewProperty<TransformMode>.Register<TransformDecorator>(nameof(Mode), TransformMode.Render,
            MewPropertyOptions.AffectsLayout);

    /// <summary>Gets or sets the child element.</summary>
    public UIElement? Child
    {
        get => GetValue(ChildProperty);
        set => SetValue(ChildProperty, value);
    }

    /// <summary>Gets or sets the transform applied to the child, in DIPs; the default is identity.</summary>
    public Matrix3x2 Transform
    {
        get => GetValue(TransformProperty);
        set => SetValue(TransformProperty, value);
    }

    /// <summary>
    /// Gets or sets the point the transform is applied about in <see cref="TransformMode.Render"/>
    /// mode, as a fraction of this element's size (0..1); the default is the center (0.5, 0.5).
    /// </summary>
    /// <remarks>
    /// <see cref="TransformMode.Layout"/> mode always uses the center.
    /// </remarks>
    public Point TransformOrigin
    {
        get => GetValue(TransformOriginProperty);
        set => SetValue(TransformOriginProperty, value);
    }

    /// <summary>
    /// Gets or sets whether the transform takes part in layout; the default is
    /// <see cref="TransformMode.Render"/>.
    /// </summary>
    /// <remarks>
    /// In <see cref="TransformMode.Layout"/> mode the child is measured at the largest untransformed size whose transformed
    /// bounding box fits the available size, as WPF <c>LayoutTransform</c> measures, and is arranged at its desired size
    /// centered in this element.
    /// </remarks>
    public TransformMode Mode
    {
        get => GetValue(ModeProperty);
        set => SetValue(ModeProperty, value);
    }

    private void OnChildChanged(UIElement? oldValue, UIElement? newValue)
    {
        if (ReferenceEquals(newValue, this))
        {
            throw new InvalidOperationException("Cannot set Child to self.");
        }

        if (oldValue != null)
        {
            oldValue.Parent = null;
        }

        if (newValue != null)
        {
            newValue.Parent = this;
        }
    }

    private void OnTransformChanged()
    {
        if (Mode == TransformMode.Layout)
        {
            InvalidateMeasure();
        }
    }

    protected override Size MeasureContent(Size availableSize)
    {
        if (Child == null)
        {
            return Size.Empty;
        }

        if (Mode == TransformMode.Layout)
        {
            var linear = TransformHost.WithoutTranslation(Transform);
            Child.Measure(TransformHost.FindMaximalAreaLocalSpaceRect(linear, availableSize));
            return TransformHost.TransformedSize(linear, Child.DesiredSize);
        }
        else
        {
            Child.Measure(availableSize);
            return Child.DesiredSize;
        }
    }

    protected override void ArrangeContent(Rect bounds)
    {
        if (Child == null)
        {
            return;
        }

        if (Mode == TransformMode.Layout)
        {
            var desired = Child.DesiredSize;
            double centerX = bounds.X + bounds.Width / 2;
            double centerY = bounds.Y + bounds.Height / 2;
            Child.Arrange(new Rect(
                centerX - desired.Width / 2,
                centerY - desired.Height / 2,
                desired.Width,
                desired.Height));
        }
        else
        {
            Child.Arrange(bounds);
        }
    }

    protected override bool TryGetChildTransform(out Matrix3x2 transform)
    {
        transform = ChildTransform();
        return !transform.IsIdentity;
    }

    protected override UIElement? OnHitTest(Point point)
        => TransformHost.HitTest(this, Child, ChildTransform(), point);

    private Matrix3x2 ChildTransform()
    {
        var bounds = Bounds;
        if (Mode == TransformMode.Layout)
        {
            return TransformHost.About(
                TransformHost.WithoutTranslation(Transform),
                bounds.X + bounds.Width / 2,
                bounds.Y + bounds.Height / 2);
        }
        else
        {
            var origin = TransformOrigin;
            return TransformHost.About(
                Transform,
                bounds.X + bounds.Width * origin.X,
                bounds.Y + bounds.Height * origin.Y);
        }
    }

    bool IVisualTreeHost.VisitChildren(Func<Element, bool> visitor)
        => Child == null || visitor(Child);
}
