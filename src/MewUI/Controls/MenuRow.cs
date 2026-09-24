using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.Controls;

/// <summary>The columns a <see cref="ContextMenu"/> measured, which every row lays its entry out in.</summary>
internal readonly record struct MenuRowColumns(
    double IconSize,
    bool HasIconColumn,
    double ShortcutWidth,
    bool HasShortcutColumn,
    Thickness ItemPadding,
    double ItemRadius);

/// <summary>
/// Draws one entry of a <see cref="ContextMenu"/>, so a change of that entry records this row alone.
/// The menu measures the columns, lays the rows out and takes all input; a row is never hit.
/// </summary>
internal sealed class MenuRow : Control, IVisualTreeHost
{
    // Triggers only: a style without setters keeps out the Control base style and its themed border.
    private static readonly bool _defaultStyleRegistered =
        DefaultStyles.Register<MenuRow>(static () => new Style(typeof(MenuRow))
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
                    Exclude = VisualStateFlags.Enabled,
                    Setters = [Setter.Create(ForegroundProperty, theme => theme.Palette.DisabledText)],
                },
            ],
        });

    private const double DISABLED_ICON_OPACITY = 0.5;

    private MenuEntry? _entry;
    private FrameworkElement? _icon;
    private bool _isHighlighted;
    private MenuRowColumns _columns;

    internal MenuRow()
    {
        IsHitTestVisible = false;
    }

    internal MenuEntry? Entry => _entry;

    internal MenuRowColumns Columns
    {
        get => _columns;
        set
        {
            if (_columns != value)
            {
                _columns = value;
                InvalidateVisual();
            }
        }
    }

    /// <summary>Takes a new entry and the icon built for it; a null entry releases the row.</summary>
    internal void Bind(MenuEntry? entry, FrameworkElement? icon)
    {
        if (!ReferenceEquals(_icon, icon))
        {
            if (_icon != null && ReferenceEquals(_icon.Parent, this))
            {
                _icon.Parent = null;
            }

            _icon = icon;
            if (icon != null)
            {
                icon.Parent = this;
            }
        }

        if (!ReferenceEquals(_entry, entry))
        {
            _entry = entry;
            InvalidateVisual();
        }

        UpdateIconOpacity();
        InvalidateVisualState();
    }

    /// <summary>Whether the menu shows this row as its current one: under the pointer, or holding the open sub-menu.</summary>
    internal void SetIsHighlighted(bool isHighlighted)
    {
        if (_isHighlighted != isHighlighted)
        {
            _isHighlighted = isHighlighted;
            InvalidateVisualState();
        }
    }

    /// <summary>Re-reads what changed on the bound item.</summary>
    internal void OnEntryChanged(MenuModelChange change)
    {
        if ((change & MenuModelChange.Enabled) != 0)
        {
            UpdateIconOpacity();
            InvalidateVisualState();
        }

        if ((change & (MenuModelChange.Text | MenuModelChange.Command | MenuModelChange.Shortcut | MenuModelChange.SubMenu)) != 0)
        {
            InvalidateVisual();
        }
    }

    private void UpdateIconOpacity()
    {
        if (_icon != null)
        {
            _icon.Opacity = _entry is MenuItem { IsEffectivelyEnabled: false } ? DISABLED_ICON_OPACITY : 1;
        }
    }

    protected override VisualState ComputeVisualState()
    {
        var flags = VisualStateFlags.None;
        if (_entry is not MenuItem item || item.IsEffectivelyEnabled)
        {
            flags |= VisualStateFlags.Enabled;
        }

        // Disabled rows highlight too: the pointer is on them, only activating them does nothing.
        if (_isHighlighted && _entry is MenuItem)
        {
            flags |= VisualStateFlags.Hot;
        }

        return new VisualState { Flags = flags };
    }

    protected override Size MeasureContent(Size availableSize) => Size.Empty;

    protected override void ArrangeContent(Rect bounds)
    {
        if (_icon == null)
        {
            return;
        }

        var paddedRow = bounds.Deflate(_columns.ItemPadding);
        double size = _columns.IconSize;
        _icon.Arrange(new Rect(
            paddedRow.X,
            paddedRow.Y + Math.Max(0, (paddedRow.Height - size) / 2),
            size,
            size));
    }

    protected override void OnRender(IGraphicsContext context)
    {
        if (Parent is not ContextMenu menu)
        {
            return;
        }

        var row = Bounds;
        if (_entry is MenuSeparator)
        {
            double dpiScale = GetDpi() / 96.0;
            double onePx = 1.0 / dpiScale;
            double separatorY = LayoutRounding.RoundToPixel(row.Y + (row.Height - onePx) / 2, dpiScale);
            context.FillRectangle(new Rect(row.X + 4, separatorY, row.Width - 8, onePx), Theme.Palette.ControlBorder);
            return;
        }

        if (_entry is not MenuItem item)
        {
            return;
        }

        var background = Background;
        if (background.A > 0)
        {
            double radius = _columns.ItemRadius;
            if (radius > 0)
            {
                context.FillRoundedRectangle(row, radius, radius, background);
            }
            else
            {
                context.FillRectangle(row, background);
            }
        }

        var foreground = Foreground;
        var chevronReserved = item.SubMenu != null ? ContextMenu.SubMenuGlyphAreaWidth : 0;
        var paddedRow = row.Deflate(_columns.ItemPadding);

        double textLeft = paddedRow.X;
        if (_columns.HasIconColumn)
        {
            textLeft += _columns.IconSize + ContextMenu.IconTextGap;
        }

        double textRight = paddedRow.Right - chevronReserved;
        if (_columns.HasShortcutColumn)
        {
            textRight -= _columns.ShortcutWidth + ContextMenu.ShortcutColumnGap;
        }

        var factory = GetGraphicsFactory();
        var style = GetTextRunStyle();
        uint dpi = GetDpi();

        var textRect = new Rect(textLeft, paddedRow.Y, Math.Max(0, textRight - textLeft), paddedRow.Height);
        menu.LastCaptionWidth = Math.Min(menu.LastCaptionWidth, textRect.Width);
        var showAccessKeys = GetValue(Window.ShowAccessKeysProperty);
        var parsed = item.GetParsedText();
        var textLayout = menu.TextLayouts.GetOrCreate(
            factory, parsed.displayText, dpi, in style, textRect.Width, textRect.Height);
        if (textLayout != null)
        {
            MenuTextLayouts.Draw(context, textLayout, textRect, foreground, showAccessKeys, parsed.underlineIndex);
        }

        var shortcutText = item.GetShortcutDisplayText();
        if (_columns.HasShortcutColumn && !string.IsNullOrEmpty(shortcutText))
        {
            double shortcutRight = paddedRow.Right - chevronReserved;
            double shortcutLeft = shortcutRight - _columns.ShortcutWidth;
            var shortcutRect = new Rect(shortcutLeft, paddedRow.Y, Math.Max(0, shortcutRight - shortcutLeft), paddedRow.Height);
            var shortcutLayout = menu.TextLayouts.GetOrCreate(
                factory,
                shortcutText,
                dpi,
                in style,
                shortcutRect.Width,
                shortcutRect.Height,
                TextAlignment.Right);
            if (shortcutLayout != null)
            {
                MenuTextLayouts.Draw(context, shortcutLayout, shortcutRect, foreground);
            }
        }

        if (item.SubMenu != null)
        {
            var center = new Point(paddedRow.Right - (ContextMenu.SubMenuGlyphAreaWidth / 2), paddedRow.Y + paddedRow.Height / 2);
            Glyph.Draw(context, center, size: 3, foreground, GlyphKind.ChevronRight);
        }
    }

    protected override void RenderSubtree(IGraphicsContext context) => _icon?.Render(context);

    internal override void WriteComposition(Rendering.Retained.CompositionPlanBuilder builder)
    {
        builder.Content(0);
        builder.Child(_icon);
    }

    bool IVisualTreeHost.VisitChildren(Func<Element, bool> visitor) => _icon == null || visitor(_icon);
}
