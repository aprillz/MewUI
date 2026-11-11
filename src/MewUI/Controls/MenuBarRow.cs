using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.Controls;

/// <summary>
/// Draws one top-level item of a <see cref="MenuBar"/>, so a change of that item records this row alone.
/// The bar lays the rows out and takes all input; a row is never hit.
/// </summary>
internal sealed class MenuBarRow : Control
{
    // Removes beforefieldinit: nothing reads the field below, so trimming would otherwise drop its
    // initializer and the registration with it.
    static MenuBarRow() { }

    // Triggers only: a style without setters keeps out the Control base style and its themed border.
    // Later triggers win, so the open item covers the hover.
    private static readonly bool _defaultStyleRegistered =
        DefaultStyles.Register<MenuBarRow>(static () => new Style(typeof(MenuBarRow))
        {
            Triggers =
            [
                new StateTrigger
                {
                    Match = VisualStateFlags.Hot,
                    Setters = [Setter.Create(BackgroundProperty, theme => theme.Palette.SelectionBackground.WithAlpha((byte)(0.6 * 255)))],
                },
                new StateTrigger
                {
                    Match = VisualStateFlags.Active,
                    Setters = [Setter.Create(BackgroundProperty, theme => theme.Palette.SelectionBackground)],
                },
                new StateTrigger
                {
                    Exclude = VisualStateFlags.Enabled,
                    Setters = [Setter.Create(ForegroundProperty, theme => theme.Palette.DisabledText)],
                },
            ],
        });

    private MenuItem? _item;
    private bool _isHot;
    private bool _isOpen;
    private double _itemRadius;

    internal MenuBarRow()
    {
        IsHitTestVisible = false;
    }

    internal MenuItem? Item => _item;

    internal double ItemRadius
    {
        get => _itemRadius;
        set
        {
            if (_itemRadius != value)
            {
                _itemRadius = value;
                InvalidateVisual();
            }
        }
    }

    /// <summary>Takes a new item; a null item releases the row.</summary>
    internal void Bind(MenuItem? item)
    {
        if (!ReferenceEquals(_item, item))
        {
            _item = item;
            InvalidateVisual();
        }

        InvalidateVisualState();
    }

    /// <summary>Whether the pointer is over this item.</summary>
    internal void SetIsHot(bool isHot)
    {
        if (_isHot != isHot)
        {
            _isHot = isHot;
            InvalidateVisualState();
        }
    }

    /// <summary>Whether this item holds the open menu.</summary>
    internal void SetIsOpen(bool isOpen)
    {
        if (_isOpen != isOpen)
        {
            _isOpen = isOpen;
            InvalidateVisualState();
        }
    }

    /// <summary>Re-reads the bound item after it changed.</summary>
    internal void OnItemChanged()
    {
        InvalidateVisualState();
        InvalidateVisual();
    }

    protected override VisualState ComputeVisualState()
    {
        var flags = VisualStateFlags.None;
        if (IsEffectivelyEnabled && (_item == null || _item.IsEffectivelyEnabled))
        {
            flags |= VisualStateFlags.Enabled;
        }

        // Disabled items highlight too: the pointer is on them, only opening them does nothing.
        if (_isHot)
        {
            flags |= VisualStateFlags.Hot;
        }

        if (_isOpen)
        {
            flags |= VisualStateFlags.Active;
        }

        return new VisualState { Flags = flags };
    }

    protected override Size MeasureContent(Size availableSize) => Size.Empty;

    protected override void OnRender(IGraphicsContext context)
    {
        if (Parent is not MenuBar bar || _item == null)
        {
            return;
        }

        var row = Bounds;
        var background = Background;
        if (background.A > 0)
        {
            if (_itemRadius > 0)
            {
                context.FillRoundedRectangle(row, _itemRadius, _itemRadius, background);
            }
            else
            {
                context.FillRectangle(row, background);
            }
        }

        var textRect = row.Deflate(new Thickness(MenuBar.ITEM_HORIZONTAL_PADDING, 0, MenuBar.ITEM_HORIZONTAL_PADDING, 0));
        var style = GetTextRunStyle();
        var showAccessKeys = GetValue(Window.ShowAccessKeysProperty);
        var parsed = _item.GetParsedText();
        var layout = bar.TextLayouts.GetOrCreate(
            GetGraphicsFactory(), parsed.displayText, GetDpi(), in style, textRect.Width, textRect.Height);
        if (layout != null)
        {
            MenuTextLayouts.Draw(context, layout, textRect, Foreground, showAccessKeys, parsed.underlineIndex);
        }
    }
}
