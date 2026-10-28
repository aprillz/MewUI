using System.Numerics;

namespace Aprillz.MewUI.Controls;

/// <summary>
/// Scales a single child to fill this element, as WPF <c>Viewbox</c> does: the child is measured at its natural size
/// and drawn scaled by <see cref="Stretch"/> and <see cref="StretchDirection"/>, centered in this element.
/// Hit testing and coordinate conversion follow the scale.
/// </summary>
public sealed class Viewbox : FrameworkElement, IVisualTreeHost
{
    public static readonly MewProperty<UIElement?> ChildProperty =
        MewProperty<UIElement?>.Register<Viewbox>(nameof(Child), null,
            MewPropertyOptions.AffectsLayout,
            static (self, oldValue, newValue) => self.OnChildChanged(oldValue, newValue));

    public static readonly MewProperty<Stretch> StretchProperty =
        MewProperty<Stretch>.Register<Viewbox>(nameof(Stretch), Stretch.Uniform,
            MewPropertyOptions.AffectsLayout);

    public static readonly MewProperty<StretchDirection> StretchDirectionProperty =
        MewProperty<StretchDirection>.Register<Viewbox>(nameof(StretchDirection), StretchDirection.Both,
            MewPropertyOptions.AffectsLayout);

    /// <summary>Gets or sets the child element.</summary>
    public UIElement? Child
    {
        get => GetValue(ChildProperty);
        set => SetValue(ChildProperty, value);
    }

    /// <summary>
    /// Gets or sets how the child is scaled to fill this element; the default is
    /// <see cref="Stretch.Uniform"/>.
    /// </summary>
    public Stretch Stretch
    {
        get => GetValue(StretchProperty);
        set => SetValue(StretchProperty, value);
    }

    /// <summary>
    /// Gets or sets whether the scale may enlarge the child, shrink it, or both; the default is
    /// <see cref="StretchDirection.Both"/>.
    /// </summary>
    public StretchDirection StretchDirection
    {
        get => GetValue(StretchDirectionProperty);
        set => SetValue(StretchDirectionProperty, value);
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

    protected override Size MeasureContent(Size availableSize)
    {
        if (Child == null)
        {
            return Size.Empty;
        }

        Child.Measure(Size.Infinity);
        var childSize = Child.DesiredSize;
        var scale = ComputeScaleFactor(availableSize, childSize, Stretch, StretchDirection);
        return new Size(childSize.Width * scale.Width, childSize.Height * scale.Height);
    }

    protected override void ArrangeContent(Rect bounds)
    {
        if (Child == null)
        {
            return;
        }

        var childSize = Child.DesiredSize;
        Child.Arrange(new Rect(bounds.X, bounds.Y, childSize.Width, childSize.Height));
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
        if (Child == null)
        {
            return Matrix3x2.Identity;
        }

        var bounds = Bounds;
        var childSize = Child.DesiredSize;
        var scale = ComputeScaleFactor(bounds.Size, childSize, Stretch, StretchDirection);
        double left = bounds.X + (bounds.Width - childSize.Width * scale.Width) / 2;
        double top = bounds.Y + (bounds.Height - childSize.Height * scale.Height) / 2;

        return
            Matrix3x2.CreateTranslation((float)-bounds.X, (float)-bounds.Y) *
            Matrix3x2.CreateScale((float)scale.Width, (float)scale.Height) *
            Matrix3x2.CreateTranslation((float)left, (float)top);
    }

    /// <summary>The horizontal and vertical scale of WPF <c>Viewbox.ComputeScaleFactor</c>.</summary>
    private static Size ComputeScaleFactor(Size availableSize, Size contentSize, Stretch stretch, StretchDirection stretchDirection)
    {
        double scaleX = 1.0;
        double scaleY = 1.0;

        bool isConstrainedWidth = !double.IsPositiveInfinity(availableSize.Width);
        bool isConstrainedHeight = !double.IsPositiveInfinity(availableSize.Height);

        if (stretch != Stretch.None && (isConstrainedWidth || isConstrainedHeight))
        {
            scaleX = TransformHost.IsZero(contentSize.Width) ? 0.0 : availableSize.Width / contentSize.Width;
            scaleY = TransformHost.IsZero(contentSize.Height) ? 0.0 : availableSize.Height / contentSize.Height;

            if (!isConstrainedWidth)
            {
                scaleX = scaleY;
            }
            else if (!isConstrainedHeight)
            {
                scaleY = scaleX;
            }
            else if (stretch == Stretch.Uniform)
            {
                double minScale = Math.Min(scaleX, scaleY);
                scaleX = minScale;
                scaleY = minScale;
            }
            else if (stretch == Stretch.UniformToFill)
            {
                double maxScale = Math.Max(scaleX, scaleY);
                scaleX = maxScale;
                scaleY = maxScale;
            }

            if (stretchDirection == StretchDirection.UpOnly)
            {
                scaleX = Math.Max(scaleX, 1.0);
                scaleY = Math.Max(scaleY, 1.0);
            }
            else if (stretchDirection == StretchDirection.DownOnly)
            {
                scaleX = Math.Min(scaleX, 1.0);
                scaleY = Math.Min(scaleY, 1.0);
            }
        }

        return new Size(scaleX, scaleY);
    }

    bool IVisualTreeHost.VisitChildren(Func<Element, bool> visitor)
        => Child == null || visitor(Child);
}
