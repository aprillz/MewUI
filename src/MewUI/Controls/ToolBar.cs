using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.Controls;

/// <summary>
/// Rows of command groups. Rows are the <see cref="Bands"/> themselves, never a consequence of the
/// width: a band that runs out of room hides its trailing groups behind its own chevron, so the
/// toolbar's height is what the application asked for and does not change as the window resizes.
/// </summary>
public sealed partial class ToolBar : Control, IVisualTreeHost
{
    static ToolBar() { }

    private static readonly bool _defaultStyleRegistered =
        DefaultStyles.Register<ToolBar>(DefaultStyles.CreateToolBarStyle);

    /// <summary>How entries show their commands unless the entry overrides it.</summary>
    public static readonly MewProperty<CommandPresentationMode> ItemPresentationProperty =
        MewProperty<CommandPresentationMode>.Register<ToolBar>(nameof(ItemPresentation),
            CommandPresentationMode.Icon,
            MewPropertyOptions.AffectsLayout,
            static (self, _, _) => self.OnBandsChanged());

    /// <summary>
    /// Whether groups may be dragged into a different place. This is a capability, not a mode: a press
    /// still runs the entry, and only a drag from a group's grip moves it.
    /// </summary>
    public static readonly MewProperty<bool> CanReorderGroupsProperty =
        MewProperty<bool>.Register<ToolBar>(nameof(CanReorderGroups), false,
            MewPropertyOptions.AffectsLayout);

    /// <summary>Gap between entries inside one group.</summary>
    private const double ENTRY_SPACING = 2;

    /// <summary>Margin between a group's plate and the band around it, and between two plates.</summary>
    private const double GROUP_MARGIN = 2;

    /// <summary>Inset of an entry inside its own plate.</summary>
    private const double GROUP_PADDING = 2;

    /// <summary>Gap on either side of a label, which has no button face to keep it off its neighbours.</summary>
    private const double LABEL_MARGIN = 4;

    /// <summary>
    /// Width of a band's overflow chevron. Narrower than an entry because it carries an 8 DIP glyph and
    /// nothing else; its height is the entry height, so it lines up with the entries beside it.
    /// </summary>
    private const double CHEVRON_WIDTH = 12;

    // 4 DIPs, the same gesture threshold the drag/drop router uses.
    private const double DRAG_THRESHOLD = 4.0;

    private readonly List<ToolBarBand> _bands = new();
    private readonly List<BandVisual> _visuals = new();
    private bool _visualsValid;

    /// <summary>Gets the bands, top to bottom.</summary>
    public IList<ToolBarBand> Bands => _bandsView ??= new BandCollection(this);

    private BandCollection? _bandsView;

    /// <inheritdoc cref="ItemPresentationProperty"/>
    public CommandPresentationMode ItemPresentation
    {
        get => GetValue(ItemPresentationProperty);
        set => SetValue(ItemPresentationProperty, value);
    }

    /// <inheritdoc cref="CanReorderGroupsProperty"/>
    public bool CanReorderGroups
    {
        get => GetValue(CanReorderGroupsProperty);
        set => SetValue(CanReorderGroupsProperty, value);
    }

    /// <summary>Raised after a drag has moved a group, once <see cref="Bands"/> holds the new layout.</summary>
    public event Action? GroupsReordered;

    /// <summary>Gets whether a group is currently being dragged.</summary>
    public bool IsReordering => _drag.Group != null;

    /// <summary>
    /// Height of a group's plate: an entry of the standard control height, plus the plate's own padding.
    /// The plate is what surrounds an entry, so its height is that entry's and not a metric of its own.
    /// </summary>
    private double GroupHeight => Theme.Metrics.BaseControlHeight + (GROUP_PADDING * 2);

    /// <summary>Height of one band: a plate plus the margin above and below it.</summary>
    private double BandHeight => GroupHeight + (GROUP_MARGIN * 2);

    /// <summary>
    /// Whether the current drag is aimed past the last band. The toolbar then makes room for the band the
    /// drop would open, so the mark stands on a row that is actually there.
    /// </summary>
    private bool HasPendingBand => _drag.Group != null && _drag.Target.Band >= _visuals.Count;

    internal IReadOnlyList<BandVisual> VisualsInternal => _visuals;

    internal void OnBandsChanged()
    {
        _visualsValid = false;
        InvalidateMeasure();
        InvalidateVisual();
    }

    #region Materialization

    private void EnsureVisuals()
    {
        if (_visualsValid)
        {
            return;
        }

        foreach (var visual in _visuals)
        {
            visual.Detach(this);
        }

        _visuals.Clear();

        foreach (var band in _bands)
        {
            var visual = new BandVisual(band);
            visual.Build(this);
            _visuals.Add(visual);
        }

        _visualsValid = true;
    }

    private CommandPresentationMode ResolvePresentation(ToolBarItem item)
        => item.Presentation ?? ItemPresentation;

    private Element CreateEntryControl(ToolBarEntry entry)
    {
        switch (entry)
        {
            case ToolBarToggleItem toggle:
                // A toggle carries no CommandPresentationMode, which lives on Button, so its content is
                // materialized here from the same command presentation a button would have used.
                var toggleButton = new ToolBarToggleButton
                {
                    Command = toggle.Command,
                    Content = BuildCommandContent(toggle.Command, ResolvePresentation(toggle)),
                    IsChecked = toggle.IsChecked,
                };
                toggleButton.CheckedChanged += isChecked => toggle.IsChecked = isChecked;
                return toggleButton;

            case ToolBarSplitItem split:
                return new SplitButton
                {
                    Command = split.Command,
                    Content = BuildCommandContent(split.Command, ResolvePresentation(split)),
                    DropDownMenu = split.DropDownMenu,
                };

            case ToolBarMenuItem menu:
                return new DropDownButton
                {
                    DropDownMenu = menu.DropDownMenu,
                    Content = BuildIconTextContent(menu.Icon, menu.Text),
                };

            case ToolBarItem item:
                // Built here rather than through the button's own CommandPresentationMode: the mode comes
                // from the entry or the toolbar, and an entry with no icon has to fall back to text.
                return new ToolBarButton
                {
                    Command = item.Command,
                    Content = BuildCommandContent(item.Command, ResolvePresentation(item)),
                };

            case ToolBarSplitter:
                return new ToolBarSplitterElement();

            case ToolBarLabelItem label:
                return new TextBlock
                {
                    Text = label.Text,
                    Margin = new Thickness(LABEL_MARGIN, 0, LABEL_MARGIN, 0),
                }.CenterVertical();

            case ToolBarHost host:
                return host.Content ?? new Border();

            default:
                return new Border();
        }
    }

    private Element? BuildCommandContent(Command? command, CommandPresentationMode presentation)
    {
        if (command == null || presentation == CommandPresentationMode.None)
        {
            return null;
        }

        bool showIcon = presentation is CommandPresentationMode.Icon or CommandPresentationMode.TextAndIcon;
        bool showText = presentation is CommandPresentationMode.Text or CommandPresentationMode.TextAndIcon;

        // An icon-only entry whose command has no icon would render empty, so it falls back to text.
        if (showIcon && command.Icon == null)
        {
            showText = true;
        }

        return BuildIconTextContent(showIcon ? command.Icon : null, showText ? command.Text : null);
    }

    private Element BuildIconTextContent(IconTemplate? icon, string? text)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        if (icon is IconTemplate template)
        {
            var size = IconTemplate.ResolveSize(Theme.Metrics.CommandIconSize, GetDpi() / 96.0);
            var built = template.Build(size);
            built.Width = size.Dip;
            built.Height = size.Dip;

            // Centred, not stretched: a template whose shape scales to its box grows past the requested
            // size when the row hands it the whole height.
            built.HorizontalAlignment = HorizontalAlignment.Center;
            built.VerticalAlignment = VerticalAlignment.Center;
            panel.Add(built);
        }

        if (!string.IsNullOrEmpty(text))
        {
            panel.Add(new TextBlock { Text = text }.CenterVertical());
        }

        return panel;
    }

    #endregion

    #region Layout

    protected override Size MeasureContent(Size availableSize)
    {
        EnsureVisuals();

        double bandHeight = BandHeight;
        double entryHeight = Math.Max(0, GroupHeight - (GROUP_PADDING * 2));
        double widest = 0;

        foreach (var visual in _visuals)
        {
            widest = Math.Max(widest, visual.Measure(entryHeight));
        }

        return new Size(widest, (_visuals.Count + (HasPendingBand ? 1 : 0)) * bandHeight);
    }

    protected override void ArrangeContent(Rect bounds)
    {
        EnsureVisuals();

        var content = bounds.Deflate(Padding);
        double bandHeight = BandHeight;
        double y = content.Y;

        foreach (var visual in _visuals)
        {
            visual.Arrange(new Rect(content.X, y, content.Width, bandHeight), GROUP_MARGIN, GROUP_PADDING, ENTRY_SPACING);
            y += bandHeight;
        }
    }

    #endregion

    protected override void OnRender(IGraphicsContext context)
    {
        DrawBackgroundAndBorder(context, GetSnappedBorderBounds(Bounds), Background, BorderBrush,
            BorderThickness, CornerRadius);
    }

    protected override void RenderSubtree(IGraphicsContext context)
    {
        foreach (var visual in _visuals)
        {
            visual.Render(context);
        }

        RenderDropIndicator(context);
    }

    protected override UIElement? OnHitTest(Point point)
    {
        if (!IsVisible || !IsHitTestVisible || !IsEffectivelyEnabled)
        {
            return null;
        }

        // While a group is being dragged the toolbar is a drop surface, not a set of buttons: letting the
        // pointer light entries up on the way past says they are being pressed when they are not.
        if (IsReordering)
        {
            return this;
        }

        for (int i = _visuals.Count - 1; i >= 0; i--)
        {
            if (_visuals[i].HitTest(point) is UIElement hit)
            {
                return hit;
            }
        }

        return base.OnHitTest(point);
    }

    bool IVisualTreeHost.VisitChildren(Func<Element, bool> visitor)
    {
        foreach (var visual in _visuals)
        {
            if (!visual.VisitChildren(visitor))
            {
                return false;
            }
        }

        return true;
    }
}
