using Aprillz.MewUI.Animation;
using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.Controls;

/// <summary>
/// A navigation shell: a recessed pane (toggle + <see cref="NavigationList"/>) beside a content region.
/// Fills its slot (no outer border). Supports Expanded / Compact (icon rail) / Minimal (overlay) via
/// <see cref="PaneDisplayMode"/>; <see cref="PaneDisplayMode.Auto"/> resolves from the available width.
/// Minimal is width-driven (the pane collapses to an overlay below a threshold); above it the toggle
/// collapses/expands the inline pane between Expanded and Compact. In Minimal the top-bar hamburger opens
/// the pane as an overlay flyout (item pick or outside click dismisses it).
/// </summary>
public sealed class NavigationView : Control, IVisualTreeHost
{
    // One square slot size shared by the item height, the compact rail width, the Minimal top bar, and the
    // toggle, so compact rows are square and the hamburger has an identical footprint across modes.
    private const double CompactSlot = 40;
    private const double ToggleSize = CompactSlot;
    // Below this available width the pane collapses to Minimal (overlay); above it the toggle chooses
    // between Expanded and Compact.
    private const double MinimalThreshold = 1000;
    // Top bar reserved in Minimal for the hamburger, so content sits below it (no left rail).
    private const double MinimalBarHeight = CompactSlot;

    private readonly NavigationList _pane;
    private readonly NavigationList _footerPane;
    private readonly ContentControl _contentHost;
    private readonly Border _paneHost;
    private readonly Button _paneToggle;
    private readonly Dictionary<object, Element?> _contentCache = new();
    private Func<object?, Element?>? _contentSelector;
    private Func<object?, Element?>? _footerContentSelector;
    private bool _footerActive;          // true when the selected item lives in the footer region
    private bool _suppressSelectionSync; // guards the two lists clearing each other

    private PaneDisplayMode _effectiveMode = PaneDisplayMode.Expanded;
    private bool _isPaneOpen;
    private bool _userCollapsed;   // toggle preference for the inline pane: Expanded (false) vs Compact (true)
    private Rect _paneRect;
    private Rect _contentRect;

    // Animated inline pane width, tweened on Expanded <-> Compact transitions.
    private double _inlineWidth = double.NaN;
    private double _widthFrom;
    private double _widthTo;
    private AnimationClock? _widthClock;

    // Minimal overlay open fraction (0 = closed/off-screen left, 1 = fully slid in).
    private double _overlayProgress;
    private double _overlayFrom;
    private double _overlayTo;
    private AnimationClock? _overlayClock;

    public static readonly MewProperty<double> PaneWidthProperty =
        MewProperty<double>.Register<NavigationView>(nameof(PaneWidth), 220.0, MewPropertyOptions.AffectsLayout);

    // Matches the item height so a compact row is square.
    public static readonly MewProperty<double> CompactPaneWidthProperty =
        MewProperty<double>.Register<NavigationView>(nameof(CompactPaneWidth), CompactSlot, MewPropertyOptions.AffectsLayout);

    public static readonly MewProperty<PaneDisplayMode> PaneDisplayModeProperty =
        MewProperty<PaneDisplayMode>.Register<NavigationView>(nameof(PaneDisplayMode), PaneDisplayMode.Auto,
            MewPropertyOptions.AffectsLayout, static (self, _, _) => self.InvalidateMeasure());

    public NavigationView()
    {
        _pane = new NavigationList { BorderThickness = 0, Background = Color.Transparent, ItemHeight = CompactSlot }.Cached();
        _pane.SelectionChanged += OnMainSelectionChanged;

        _footerPane = new NavigationList { BorderThickness = 0, Background = Color.Transparent, ItemHeight = CompactSlot }.Cached();
        _footerPane.SelectionChanged += OnFooterSelectionChanged;

        // The hamburger is a standalone element pinned to the top-left in every mode, so the pane can slide
        // (Minimal overlay) or resize (Expanded/Compact) underneath it without the toggle ever moving.
        _paneToggle = MakeToggle();
        _paneToggle.Parent = this;

        // Pane: scrolling main list fills, footer list pinned at the bottom; a top inset clears the toggle.
        var paneLayout = new DockPanel();
        paneLayout.Children(_footerPane.DockBottom(), _pane);
        _paneHost = new Border { Child = paneLayout, Padding = new Thickness(0, CompactSlot, 0, 0) };
        _paneHost.WithTheme((t, b) => b.Background = t.Palette.ContainerBackground);
        _paneHost.Parent = this;

        _contentHost = new ContentControl { BorderThickness = 0 };
        _contentHost.Parent = this;
    }

    private Button MakeToggle()
    {
        var button = new Button
        {
            BorderThickness = 0,
            Background = Color.Transparent,
            MinWidth = ToggleSize
        };
        button.Content(new GlyphElement
        {
            Kind = GlyphKind.Hamburger,
            GlyphSize = 8,
        });
        button.Click += OnToggle;
        return button;
    }

    /// <summary>Gets the navigation pane list. Prefer the typed <c>Items</c> extension.</summary>
    public NavigationList Pane => _pane;

    /// <summary>Gets the bottom-pinned footer list. Prefer the typed <c>FooterItems</c> extension.</summary>
    public NavigationList FooterPane => _footerPane;

    /// <summary>Gets or sets the expanded pane width.</summary>
    public double PaneWidth
    {
        get => GetValue(PaneWidthProperty);
        set => SetValue(PaneWidthProperty, value);
    }

    /// <summary>Gets or sets the pane width in the compact icon rail.</summary>
    public double CompactPaneWidth
    {
        get => GetValue(CompactPaneWidthProperty);
        set => SetValue(CompactPaneWidthProperty, value);
    }

    /// <summary>Gets or sets the pane display mode. <see cref="PaneDisplayMode.Auto"/> resolves from width.</summary>
    public PaneDisplayMode PaneDisplayMode
    {
        get => GetValue(PaneDisplayModeProperty);
        set => SetValue(PaneDisplayModeProperty, value);
    }

    /// <summary>Gets or sets the selected item index (-1 = none).</summary>
    public int SelectedIndex
    {
        get => _pane.SelectedIndex;
        set => _pane.SelectedIndex = value;
    }

    /// <summary>Gets the selected item object (from either the main or footer region), or <see langword="null"/>.</summary>
    public object? SelectedItem => _footerActive ? _footerPane.SelectedItem : _pane.SelectedItem;

    /// <summary>True when the pane currently shows item text (Expanded, or an open Minimal overlay).</summary>
    public bool PaneShowsText => _effectiveMode == PaneDisplayMode.Expanded
        || (_effectiveMode == PaneDisplayMode.Minimal && _isPaneOpen);

    /// <summary>True when the pane is a compact icon-only rail.</summary>
    public bool PaneIsRail => _effectiveMode == PaneDisplayMode.Compact;

    /// <summary>
    /// Gets or sets the selector mapping the selected item to its content element. Built content is cached
    /// per item. Prefer supplying this via the typed <c>Items</c> extension.
    /// </summary>
    public Func<object?, Element?>? ContentSelector
    {
        get => _contentSelector;
        set
        {
            _contentSelector = value;
            _contentCache.Clear();
            UpdateContent();
        }
    }

    /// <summary>
    /// Gets or sets the selector mapping a selected footer item to its content element. Prefer supplying this
    /// via the typed <c>FooterItems</c> extension.
    /// </summary>
    public Func<object?, Element?>? FooterContentSelector
    {
        get => _footerContentSelector;
        set
        {
            _footerContentSelector = value;
            _contentCache.Clear();
            UpdateContent();
        }
    }

    /// <summary>Occurs when the selection changes.</summary>
    public event Action<object?>? SelectionChanged;

    private void OnToggle()
    {
        if (_effectiveMode == PaneDisplayMode.Minimal)
        {
            // Too narrow to sit inline: slide the pane in/out as an overlay flyout.
            _isPaneOpen = !_isPaneOpen;
            if (_isPaneOpen)
            {
                _pane.RefreshItems();
                _footerPane.RefreshItems();
            }
            AnimateOverlay(_isPaneOpen ? 1.0 : 0.0);
        }
        else
        {
            // Collapse/expand the inline pane between Expanded and Compact. This is a preference, not the
            // display mode, so width-driven Minimal still applies when the window shrinks.
            _userCollapsed = !_userCollapsed;
        }

        InvalidateMeasure();
        InvalidateVisual();
    }

    private void OnMainSelectionChanged(object? item)
    {
        if (!_suppressSelectionSync)
        {
            HandleSelection(fromFooter: false, item);
        }
    }

    private void OnFooterSelectionChanged(object? item)
    {
        if (!_suppressSelectionSync)
        {
            HandleSelection(fromFooter: true, item);
        }
    }

    private void HandleSelection(bool fromFooter, object? item)
    {
        // A pick in one region clears the other, so the whole pane has a single selection.
        if (item != null)
        {
            _footerActive = fromFooter;
            _suppressSelectionSync = true;
            try
            {
                if (fromFooter)
                {
                    _pane.SelectedIndex = -1;
                }
                else
                {
                    _footerPane.SelectedIndex = -1;
                }
            }
            finally
            {
                _suppressSelectionSync = false;
            }
        }

        if (_effectiveMode == PaneDisplayMode.Minimal && _isPaneOpen)
        {
            _isPaneOpen = false;   // dismiss the overlay after a pick
            AnimateOverlay(0.0);
            InvalidateVisual();
        }
        UpdateContent();
        SelectionChanged?.Invoke(item);
    }

    private void UpdateContent()
    {
        var selector = _footerActive ? _footerContentSelector : _contentSelector;
        if (selector == null)
        {
            return;
        }

        var item = _footerActive ? _footerPane.SelectedItem : _pane.SelectedItem;
        Element? content;
        if (item != null)
        {
            if (!_contentCache.TryGetValue(item, out content))
            {
                content = selector(item);
                _contentCache[item] = content;
            }
        }
        else
        {
            content = selector(null);
        }

        _contentHost.Content = content;
    }

    private PaneDisplayMode ResolveMode(double width)
    {
        if (PaneDisplayMode != PaneDisplayMode.Auto)
        {
            return PaneDisplayMode;
        }
        if (!double.IsPositiveInfinity(width) && width < MinimalThreshold)
        {
            return PaneDisplayMode.Minimal;
        }
        return _userCollapsed ? PaneDisplayMode.Compact : PaneDisplayMode.Expanded;
    }

    // Target width the pane occupies inline (0 for Minimal, which overlays instead).
    private double InlinePaneWidth() => _effectiveMode switch
    {
        PaneDisplayMode.Expanded => PaneWidth,
        PaneDisplayMode.Compact => CompactPaneWidth,
        _ => 0,
    };

    // Tweens the inline width toward the current target (Expanded <-> Compact); snaps on first layout.
    private void AnimateInlineWidth()
    {
        double target = InlinePaneWidth();
        if (double.IsNaN(_inlineWidth) || Math.Abs(_inlineWidth - target) < 0.5)
        {
            _inlineWidth = target;
            _widthClock?.Stop();
            return;
        }

        _widthFrom = _inlineWidth;
        _widthTo = target;
        _widthClock ??= CreateWidthClock();
        _widthClock.Stop();
        _widthClock.Start();
    }

    private AnimationClock CreateWidthClock()
    {
        var clock = new AnimationClock(TimeSpan.FromMilliseconds(200), Easing.EaseOutCubic);
        clock.TickCallback = progress =>
        {
            _inlineWidth = _widthFrom + (_widthTo - _widthFrom) * progress;
            InvalidateMeasure();
        };
        return clock;
    }

    // Slides the Minimal overlay in (target 1) or out (target 0).
    private void AnimateOverlay(double target)
    {
        if (Math.Abs(_overlayProgress - target) < 0.001)
        {
            _overlayProgress = target;
            return;
        }

        _overlayFrom = _overlayProgress;
        _overlayTo = target;
        _overlayClock ??= CreateOverlayClock();
        _overlayClock.Stop();
        _overlayClock.Duration = TimeSpan.FromMilliseconds(target > _overlayFrom ? 200 : 170);
        _overlayClock.Start();
    }

    private AnimationClock CreateOverlayClock()
    {
        var clock = new AnimationClock(TimeSpan.FromMilliseconds(200), Easing.EaseOutCubic);
        clock.TickCallback = progress =>
        {
            _overlayProgress = _overlayFrom + (_overlayTo - _overlayFrom) * progress;
            InvalidateArrange();
            InvalidateVisual();
        };
        return clock;
    }

    protected override Size MeasureContent(Size availableSize)
    {
        var borderInset = GetBorderVisualInset();
        var border = borderInset > 0 ? new Thickness(borderInset) : Thickness.Zero;
        var inner = availableSize.Deflate(border).Deflate(Padding);

        var mode = ResolveMode(inner.Width);
        if (mode != _effectiveMode)
        {
            _effectiveMode = mode;
            if (mode != PaneDisplayMode.Minimal)
            {
                _isPaneOpen = false;   // the overlay-open state only applies in Minimal
                _overlayProgress = 0;
                _overlayClock?.Stop();
            }
            // Rebind rows for the new mode here, before measuring the pane, so the rebind and the
            // re-arrange complete within this same layout pass. Deferring to ArrangeContent would issue
            // the invalidation mid-arrange, where it can be dropped, leaving the previous mode's rows
            // visible until a manual scroll re-arranges.
            _pane.RefreshItems();
            _footerPane.RefreshItems();
            AnimateInlineWidth();
        }

        if (double.IsNaN(_inlineWidth))
        {
            _inlineWidth = InlinePaneWidth();
        }

        double inlinePane = double.IsPositiveInfinity(inner.Width) ? _inlineWidth : Math.Min(_inlineWidth, inner.Width);
        bool minimal = _effectiveMode == PaneDisplayMode.Minimal;
        // Minimal: content is full width below a top bar holding the hamburger. Inline modes: content sits
        // right of the pane.
        double leftInset = minimal ? 0 : inlinePane;
        double topInset = minimal ? MinimalBarHeight : 0;
        double contentWidth = double.IsPositiveInfinity(inner.Width) ? double.PositiveInfinity : Math.Max(0, inner.Width - leftInset);
        double contentHeight = double.IsPositiveInfinity(inner.Height) ? double.PositiveInfinity : Math.Max(0, inner.Height - topInset);

        _paneToggle.Measure(new Size(CompactSlot, CompactSlot));
        _paneHost.Measure(new Size(minimal ? PaneWidth : Math.Max(0, inlinePane), inner.Height));
        _contentHost.Measure(new Size(contentWidth, contentHeight));

        double desiredWidth = double.IsPositiveInfinity(inner.Width) ? inlinePane + _contentHost.DesiredSize.Width : inner.Width;
        double desiredHeight = double.IsPositiveInfinity(inner.Height)
            ? Math.Max(_paneHost.DesiredSize.Height, _contentHost.DesiredSize.Height)
            : inner.Height;

        return new Size(desiredWidth, desiredHeight).Inflate(Padding).Inflate(border);
    }

    protected override void ArrangeContent(Rect bounds)
    {
        var borderInset = GetBorderVisualInset();
        var border = borderInset > 0 ? new Thickness(borderInset) : Thickness.Zero;
        var inner = bounds.Deflate(border).Deflate(Padding);

        double inlinePane = Math.Min(double.IsNaN(_inlineWidth) ? InlinePaneWidth() : _inlineWidth, inner.Width);
        bool minimal = _effectiveMode == PaneDisplayMode.Minimal;
        double leftInset = minimal ? 0 : inlinePane;
        double topInset = minimal ? MinimalBarHeight : 0;
        _contentRect = new Rect(inner.X + leftInset, inner.Y + topInset,
            Math.Max(0, inner.Width - leftInset), Math.Max(0, inner.Height - topInset));
        _contentHost.Arrange(_contentRect);

        // Minimal slides the full-width pane in from the left (progress 0 = off-screen); others sit inline.
        double paneWidth = minimal ? PaneWidth : inlinePane;
        double paneX = minimal ? inner.X - (1 - _overlayProgress) * PaneWidth : inner.X;
        _paneRect = new Rect(paneX, inner.Y, paneWidth, inner.Height);
        _paneHost.Arrange(_paneRect);

        // The hamburger stays pinned to the top-left in every mode.
        _paneToggle.Arrange(new Rect(inner.X, inner.Y, CompactSlot, CompactSlot));
    }

    protected override void OnRender(IGraphicsContext context)
    {
        var bounds = GetSnappedBorderBounds(Bounds);
        DrawBackgroundAndBorder(context, bounds, GetValue(BackgroundProperty), GetValue(BorderBrushProperty),
            BorderThickness, CornerRadius);
    }

    protected override void RenderSubtree(IGraphicsContext context)
    {
        _contentHost.Render(context);

        // Inline pane, or the Minimal overlay while it is (partly) slid in.
        if (_effectiveMode != PaneDisplayMode.Minimal || _overlayProgress > 0.001)
        {
            _paneHost.Render(context);
            DrawPaneSeparator(context);
        }

        // The pinned hamburger always renders on top so it never moves and is never covered by the pane.
        _paneToggle.Render(context);
    }

    private void DrawPaneSeparator(IGraphicsContext context)
    {
        if (_paneRect.Width <= 0 || _paneRect.Height <= 0)
        {
            return;
        }
        double x = _paneRect.Right;
        context.DrawLine(new Point(x, _paneRect.Top), new Point(x, _paneRect.Bottom), Theme.Palette.ControlBorder, 1.0, true);
    }

    protected override UIElement? OnHitTest(Point point)
    {
        if (!IsVisible || !IsHitTestVisible || !IsEffectivelyEnabled)
        {
            return null;
        }

        // The pinned hamburger is top-most in every mode.
        var toggleHit = _paneToggle.HitTest(point);
        if (toggleHit != null)
        {
            return toggleHit;
        }

        if (_effectiveMode == PaneDisplayMode.Minimal)
        {
            if (_isPaneOpen)
            {
                // Overlay grabs its hits; anywhere else light-dismisses it (handled in OnMouseDown).
                return _paneHost.HitTest(point) ?? (Bounds.Contains(point) ? this : null);
            }

            return _contentHost.HitTest(point) ?? (Bounds.Contains(point) ? this : null);
        }

        var paneHit = _paneHost.HitTest(point) ?? _contentHost.HitTest(point);
        return paneHit ?? (Bounds.Contains(point) ? this : null);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        // Click outside the open overlay dismisses it.
        if (!e.Handled && _effectiveMode == PaneDisplayMode.Minimal && _isPaneOpen && !_paneRect.Contains(e.GetPosition(this)))
        {
            _isPaneOpen = false;
            AnimateOverlay(0.0);
            InvalidateVisual();
            e.Handled = true;
        }
    }

    bool IVisualTreeHost.VisitChildren(Func<Element, bool> visitor)
        => visitor(_paneHost) && visitor(_paneToggle) && visitor(_contentHost);
}

/// <summary>Display mode for a <see cref="NavigationView"/> pane.</summary>
public enum PaneDisplayMode
{
    /// <summary>Resolve Expanded / Compact / Minimal from the available width.</summary>
    Auto,

    /// <summary>Full pane with icon and text.</summary>
    Expanded,

    /// <summary>Narrow icon-only rail; group headers become spacers.</summary>
    Compact,

    /// <summary>Pane hidden behind a toggle that opens it as an overlay.</summary>
    Minimal,
}
