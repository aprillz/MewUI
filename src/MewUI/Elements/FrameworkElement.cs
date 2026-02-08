namespace Aprillz.MewUI.Controls;

using Aprillz.MewUI.Rendering;

/// <summary>
/// Base class for elements with size, margin, alignment, and data binding support.
/// </summary>
public abstract class FrameworkElement : UIElement, IDisposable
{
    private List<Action<Theme, FrameworkElement>>? _themeCallbacks;
    private Size _lastArrangedSize;
    private bool _hasArrangedSize;

    private bool _disposed;
    private double _minHeight = double.NaN;

    /// <summary>
    /// Occurs when the element's size changes.
    /// </summary>
    public event Action<SizeChangedEventArgs>? SizeChanged;
    protected virtual double DefaultMinHeight => 0;

    /// <summary>
    /// Gets or sets the explicit width. Use double.NaN for automatic sizing.
    /// </summary>
    public double Width
    {
        get;
        set
        {
            if (SetDouble(ref field, value))
            {
                InvalidateMeasure();
            }
        }
    } = double.NaN;

    /// <summary>
    /// Gets or sets the explicit height. Use double.NaN for automatic sizing.
    /// </summary>
    public double Height
    {
        get;
        set
        {
            if (SetDouble(ref field, value))
            {
                InvalidateMeasure();
            }
        }
    } = double.NaN;

    /// <summary>
    /// Gets or sets the minimum width.
    /// </summary>
    public double MinWidth
    {
        get;
        set
        {
            if (SetDouble(ref field, value))
            {
                InvalidateMeasure();
            }
        }
    }

    /// <summary>
    /// Gets or sets the minimum height.
    /// </summary>
    public double MinHeight
    {
        get => _minHeight >= 0 ? _minHeight : DefaultMinHeight;
        set
        {
            if (MinHeight == value) return;

            if (value < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(value), "MinHeight must be 0 or greater.");
            }
            _minHeight = value; InvalidateMeasure();
        }
    }

    /// <summary>
    /// Gets or sets the maximum width.
    /// </summary>
    public double MaxWidth
    {
        get;
        set
        {
            if (SetDouble(ref field, value))
            {
                InvalidateMeasure();
            }
        }
    } = double.PositiveInfinity;

    /// <summary>
    /// Gets or sets the maximum height.
    /// </summary>
    public double MaxHeight
    {
        get;
        set
        {
            if (SetDouble(ref field, value))
            {
                InvalidateMeasure();
            }
        }
    } = double.PositiveInfinity;

    /// <summary>
    /// Gets or sets the outer margin.
    /// </summary>
    public Thickness Margin
    {
        get;
        set
        {
            if (Set(ref field, value))
            {
                InvalidateMeasure();
            }
        }
    }

    /// <summary>
    /// Gets or sets the inner padding.
    /// </summary>
    public Thickness Padding
    {
        get;
        set
        {
            if (Set(ref field, value))
            {
                InvalidateMeasure();
            }
        }
    }

    /// <summary>
    /// Gets or sets the horizontal alignment within the parent.
    /// </summary>
    public HorizontalAlignment HorizontalAlignment
    {
        get;
        set
        {
            if (Set(ref field, value))
            {
                InvalidateArrange();
            }
        }
    } = HorizontalAlignment.Stretch;

    /// <summary>
    /// Gets or sets the vertical alignment within the parent.
    /// </summary>
    public VerticalAlignment VerticalAlignment
    {
        get;
        set
        {
            if (Set(ref field, value))
            {
                InvalidateArrange();
            }
        }
    } = VerticalAlignment.Stretch;

    /// <summary>
    /// Gets the actual width after layout.
    /// </summary>
    public double ActualWidth => Bounds.Width;

    /// <summary>
    /// Gets the actual height after layout.
    /// </summary>
    public double ActualHeight => Bounds.Height;

    protected Theme Theme => ThemeInternal;

    internal Theme ThemeInternal
    {
        get
        {
            if (Application.IsRunning)
            {
                return field ??= Application.Current.Theme;
            }
            else
            {
                return ThemeManager.GetDefaultTheme(ThemeManager.ResolveVariantForStartup(ThemeManager.Default));
            }
        }
        set;
    }

    /// <summary>
    /// Gets the content bounds (bounds minus padding).
    /// </summary>
    protected Rect ContentBounds => Bounds.Deflate(Padding);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        // Release extension-managed bindings (and any other UIElement-registered disposables).
        DisposeBindings();

        OnDispose();
    }

    internal void NotifyDpiChanged(uint oldDpi, uint newDpi)
    {
        OnDpiChanged(oldDpi, newDpi);

        InvalidateMeasure();
        InvalidateVisual();
    }

    internal void NotifyThemeChanged(Theme oldTheme, Theme newTheme)
    {
        ThemeInternal = newTheme;

        OnThemeChanged(oldTheme, newTheme);
        InvokeThemeCallbacks(newTheme);

        // Theme changes should cause a repaint even if no other input/layout happens.
        // This is especially important on platforms where theme notifications are not tied to OS paint messages.
        if (!ReferenceEquals(oldTheme, newTheme))
        {
            InvalidateMeasure();
            InvalidateVisual();
        }
    }

    internal void RegisterThemeCallback(Action<Theme, FrameworkElement> callback, bool invokeImmediately = true)
    {
        ArgumentNullException.ThrowIfNull(callback);

        _themeCallbacks ??= new List<Action<Theme, FrameworkElement>>(capacity: 1);
        _themeCallbacks.Add(callback);

        if (invokeImmediately)
        {
            callback(ThemeInternal, this);
        }
    }

    /// <summary>
    /// Called when the element is being disposed. Override to release resources.
    /// </summary>
    protected virtual void OnDispose()
    { }

    /// <summary>
    /// Snaps the border bounds to device pixels for crisp rendering.
    /// </summary>
    /// <param name="bounds">The bounds to snap.</param>
    /// <returns>Pixel-snapped bounds.</returns>
    protected Rect GetSnappedBorderBounds(Rect bounds)
    {
        var dpiScale = GetDpi() / 96.0;
        return LayoutRounding.SnapBoundsRectToPixels(bounds, dpiScale);
    }

    /// <summary>
    /// Called when the DPI changes.
    /// </summary>
    /// <param name="oldDpi">The old DPI value.</param>
    /// <param name="newDpi">The new DPI value.</param>
    protected virtual void OnDpiChanged(uint oldDpi, uint newDpi)
    {
    }

    /// <summary>
    /// Gets the graphics factory from the owning window, or the default factory.
    /// </summary>
    /// <returns>The graphics factory.</returns>
    protected IGraphicsFactory GetGraphicsFactory()
    {
        var root = FindVisualRoot();
        if (root is Window window)
        {
            return window.GraphicsFactory;
        }

        return Application.DefaultGraphicsFactory;
    }

    /// <summary>
    /// Gets the current DPI value.
    /// </summary>
    /// <returns>The DPI value.</returns>
    protected uint GetDpi() => GetDpiCached();

    /// <summary>
    /// Called when the theme changes.
    /// </summary>
    /// <param name="oldTheme">The old theme.</param>
    /// <param name="newTheme">The new theme.</param>
    protected virtual void OnThemeChanged(Theme oldTheme, Theme newTheme)
    { }

    /// <summary>
    /// Called when the element's size changes.
    /// </summary>
    /// <param name="e">Size change event arguments.</param>
    protected virtual void OnSizeChanged(SizeChangedEventArgs e)
    { }

    protected override void ArrangeCore(Rect finalRect)
    {
        var oldSize = _lastArrangedSize;
        var hadOldSize = _hasArrangedSize;

        base.ArrangeCore(finalRect);

        if (!IsVisible)
        {
            return;
        }

        var newSize = new Size(Bounds.Width, Bounds.Height);
        if (!hadOldSize || oldSize != newSize)
        {
            _lastArrangedSize = newSize;
            _hasArrangedSize = true;

            var args = new SizeChangedEventArgs(hadOldSize ? oldSize : Size.Empty, newSize);
            OnSizeChanged(args);
            SizeChanged?.Invoke(args);
        }
    }


    protected override Rect GetArrangedBounds(Rect finalRect)
    {
        var innerSlot = finalRect.Deflate(Margin);

        double availableWidth = Math.Max(0, innerSlot.Width);
        double availableHeight = Math.Max(0, innerSlot.Height);

        // If we have explicit size, use it; otherwise use desired (excluding margin)
        double arrangeWidth = !double.IsNaN(Width) ? Width : DesiredSize.Width - Margin.Left - Margin.Right;
        double arrangeHeight = !double.IsNaN(Height) ? Height : DesiredSize.Height - Margin.Top - Margin.Bottom;

        arrangeWidth = Math.Clamp(arrangeWidth, MinWidth, MaxWidth);
        arrangeHeight = Math.Clamp(arrangeHeight, MinHeight, MaxHeight);

        // Stretch fills only when Width is auto (NaN). If Width is explicitly set,
        // keep that size (clamped) and treat stretch as a left/top alignment within the slot.
        double width = (HorizontalAlignment == HorizontalAlignment.Stretch && double.IsNaN(Width))
            ? availableWidth
            : Math.Min(arrangeWidth, availableWidth);

        double height = (VerticalAlignment == VerticalAlignment.Stretch && double.IsNaN(Height))
            ? availableHeight
            : Math.Min(arrangeHeight, availableHeight);

        double x = innerSlot.X;
        if (HorizontalAlignment == HorizontalAlignment.Center)
        {
            x = innerSlot.X + (availableWidth - width) / 2;
        }
        else if (HorizontalAlignment == HorizontalAlignment.Right)
        {
            x = innerSlot.Right - width;
        }

        double y = innerSlot.Y;
        if (VerticalAlignment == VerticalAlignment.Center)
        {
            y = innerSlot.Y + (availableHeight - height) / 2;
        }
        else if (VerticalAlignment == VerticalAlignment.Bottom)
        {
            y = innerSlot.Bottom - height;
        }

        return new Rect(x, y, width, height);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        // Subtract margin from available size
        var marginWidth = Margin.Left + Margin.Right;
        var marginHeight = Margin.Top + Margin.Bottom;

        var constrainedSize = new Size(
            Math.Max(0, availableSize.Width - marginWidth),
            Math.Max(0, availableSize.Height - marginHeight)
        );

        // Apply explicit size constraints
        if (!double.IsNaN(Width))
        {
            constrainedSize = constrainedSize.WithWidth(Math.Clamp(Width, MinWidth, MaxWidth));
        }

        if (!double.IsNaN(Height))
        {
            constrainedSize = constrainedSize.WithHeight(Math.Clamp(Height, MinHeight, MaxHeight));
        }

        // Measure content
        var measured = MeasureContent(constrainedSize);

        // Apply min/max constraints
        var finalWidth = Math.Clamp(measured.Width, MinWidth, MaxWidth);
        var finalHeight = Math.Clamp(measured.Height, MinHeight, MaxHeight);

        // WPF-style explicit size: DesiredSize must honor Width/Height when specified.
        if (!double.IsNaN(Width))
        {
            finalWidth = Math.Clamp(Width, MinWidth, MaxWidth);
        }

        if (!double.IsNaN(Height))
        {
            finalHeight = Math.Clamp(Height, MinHeight, MaxHeight);
        }

        // Add margin back to desired size
        return new Size(finalWidth + marginWidth, finalHeight + marginHeight);
    }

    /// <summary>
    /// Measures the content. Override in derived classes.
    /// </summary>
    protected virtual Size MeasureContent(Size availableSize) => Size.Empty;

    protected override Size ArrangeOverride(Size finalSize)
    {
        ArrangeContent(Bounds);
        return finalSize;
    }

    /// <summary>
    /// Arranges the content. Override in derived classes.
    /// </summary>
    protected virtual void ArrangeContent(Rect bounds)
    { }

    private void InvokeThemeCallbacks(Theme theme)
    {
        if (_themeCallbacks == null)
        {
            return;
        }

        for (int i = 0; i < _themeCallbacks.Count; i++)
        {
            _themeCallbacks[i](theme, this);
        }
    }
}
