using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.Controls;

/// <summary>
/// A control that displays text, using an internal TextBlock for rendering.
/// </summary>
public partial class Label : Control, IVisualTreeHost
{
    public static readonly MewProperty<string> TextProperty =
        MewProperty<string>.Register<Label>(nameof(Text), string.Empty,
            MewPropertyOptions.AffectsLayout,
            static (self, _, _) => self.OnTextChanged());

    public static readonly MewProperty<TextAlignment> TextAlignmentProperty =
        MewProperty<TextAlignment>.Register<Label>(nameof(TextAlignment), TextAlignment.Left,
            MewPropertyOptions.AffectsRender,
            static (self, _, _) => self.OnTextAlignmentChanged());

    public static readonly MewProperty<TextAlignment> VerticalTextAlignmentProperty =
        MewProperty<TextAlignment>.Register<Label>(nameof(VerticalTextAlignment), TextAlignment.Center,
            MewPropertyOptions.AffectsRender,
            static (self, _, _) => self.OnVerticalTextAlignmentChanged());

    public static readonly MewProperty<TextWrapping> TextWrappingProperty =
        MewProperty<TextWrapping>.Register<Label>(nameof(TextWrapping), TextWrapping.NoWrap,
            MewPropertyOptions.AffectsLayout,
            static (self, _, _) => self.OnTextWrappingChanged());

    public static readonly MewProperty<TextTrimming> TextTrimmingProperty =
        MewProperty<TextTrimming>.Register<Label>(nameof(TextTrimming), TextTrimming.None,
            MewPropertyOptions.AffectsLayout,
            static (self, _, _) => self.OnTextTrimmingChanged());

    private readonly AccessText _accessText;
    private UIElement? _target;

    public Label()
    {
        _accessText = new() { Parent = this };
    }

    /// <summary>
    /// Gets or sets the target element that receives focus when this label's access key is activated.
    /// </summary>
    public UIElement? Target
    {
        get => _target;
        set => _target = value;
    }

    private void OnTextChanged() => _accessText.SetRawText(Text);

    private void OnTextAlignmentChanged() => _accessText.TextAlignment = TextAlignment;

    private void OnVerticalTextAlignmentChanged() => _accessText.VerticalTextAlignment = VerticalTextAlignment;

    private void OnTextWrappingChanged() => _accessText.TextWrapping = TextWrapping;

    private void OnTextTrimmingChanged() => _accessText.TextTrimming = TextTrimming;

    internal override void OnAccessKey()
    {
        if (_target != null)
            _target.OnAccessKey();
        else
            base.OnAccessKey();
    }

    protected override bool InvalidateOnMouseOverChanged => false;

    /// <summary>
    /// Gets or sets the text content.
    /// </summary>
    public string Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value ?? string.Empty);
    }

    /// <summary>
    /// Gets or sets the horizontal text alignment.
    /// </summary>
    public TextAlignment TextAlignment
    {
        get => GetValue(TextAlignmentProperty);
        set => SetValue(TextAlignmentProperty, value);
    }

    /// <summary>
    /// Gets or sets the vertical text alignment.
    /// </summary>
    public TextAlignment VerticalTextAlignment
    {
        get => GetValue(VerticalTextAlignmentProperty);
        set => SetValue(VerticalTextAlignmentProperty, value);
    }

    /// <summary>
    /// Gets or sets the text wrapping mode.
    /// </summary>
    public TextWrapping TextWrapping
    {
        get => GetValue(TextWrappingProperty);
        set => SetValue(TextWrappingProperty, value);
    }

    /// <summary>
    /// Gets or sets the text trimming mode.
    /// </summary>
    public TextTrimming TextTrimming
    {
        get => GetValue(TextTrimmingProperty);
        set => SetValue(TextTrimmingProperty, value);
    }

    protected override Size MeasureContent(Size availableSize)
    {
        if (string.IsNullOrEmpty(Text))
        {
            return Padding.HorizontalThickness > 0 || Padding.VerticalThickness > 0
                ? new Size(Padding.HorizontalThickness, Padding.VerticalThickness)
                : Size.Empty;
        }

        var contentSize = availableSize.Deflate(Padding);
        _accessText.Measure(contentSize);
        return _accessText.DesiredSize.Inflate(Padding);
    }

    protected override void ArrangeContent(Rect bounds)
    {
        base.ArrangeContent(bounds);
        _accessText.Arrange(bounds.Deflate(Padding));
    }

    protected override void OnRender(IGraphicsContext context)
    {
        base.OnRender(context);

        if (string.IsNullOrEmpty(Text))
        {
            return;
        }

        _accessText.Render(context);
    }

    bool IVisualTreeHost.VisitChildren(Func<Element, bool> visitor) => visitor(_accessText);

    protected override void OnDispose()
    {
        base.OnDispose();
    }
}
