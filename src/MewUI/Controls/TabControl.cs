using Aprillz.MewUI.Input;
using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.Controls;

/// <summary>
/// A tabbed control with header buttons and content display.
/// </summary>
public sealed class TabControl : Control
    , IVisualTreeHost
    , IFocusTraversalScope
{
    private readonly List<TabItem> _tabs = new();
    private readonly StackPanel _headerStrip;
    private TabItem? _lastTab;
    private int _cachedFocusedHeaderIndex = -1;

    internal override UIElement GetDefaultFocusTarget()
    {
        var target = FocusManager.FindFirstFocusable(SelectedTab?.Content);
        return target ?? this;
    }

    /// <summary>
    /// Gets the collection of tab items.
    /// </summary>
    public IReadOnlyList<TabItem> Tabs => _tabs;

    public static readonly MewProperty<TabPlacement> TabPlacementProperty =
        MewProperty<TabPlacement>.Register<TabControl>(nameof(TabPlacement), TabPlacement.Top,
            MewPropertyOptions.AffectsLayout | MewPropertyOptions.AffectsRender,
            static (self, _, _) => self.OnTabPlacementChanged());

    public static readonly MewProperty<int> SelectedIndexProperty =
        MewProperty<int>.Register<TabControl>(nameof(SelectedIndex), -1,
            MewPropertyOptions.AffectsLayout,
            static (self, _, _) => self.OnSelectedIndexChanged(),
            static (self, value) => value < 0 || value >= self._tabs.Count ? -1 : value);

    /// <summary>
    /// Gets or sets the selected tab index.
    /// </summary>
    public int SelectedIndex
    {
        get => GetValue(SelectedIndexProperty);
        set => SetValue(SelectedIndexProperty, value);
    }

    /// <summary>
    /// Gets or sets where the tab headers are placed.
    /// </summary>
    public TabPlacement TabPlacement
    {
        get => GetValue(TabPlacementProperty);
        set => SetValue(TabPlacementProperty, value);
    }

    private void OnSelectedIndexChanged()
    {
        UpdateSelection();
        SelectionChanged?.Invoke(SelectedItem);
    }

    /// <summary>
    /// Gets the currently selected tab item.
    /// </summary>
    public TabItem? SelectedTab => SelectedIndex >= 0 && SelectedIndex < _tabs.Count ? _tabs[SelectedIndex] : null;

    /// <summary>
    /// Gets the currently selected item object for selection consistency.
    /// </summary>
    public object? SelectedItem => SelectedTab;

    /// <summary>
    /// Occurs when the selected tab changes.
    /// </summary>
    public event Action<object?>? SelectionChanged;

    public override bool Focusable => true;

    public TabControl()
    {
        _headerStrip = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 2,
        };
        _headerStrip.Parent = this;
    }

    private bool IsHorizontalPlacement =>
        TabPlacement is TabPlacement.Top or TabPlacement.Bottom;

    private void OnTabPlacementChanged()
    {
        _headerStrip.Orientation = IsHorizontalPlacement
            ? Orientation.Horizontal
            : Orientation.Vertical;

        for (int i = 0; i < _headerStrip.Count; i++)
        {
            if (_headerStrip[i] is TabHeaderButton button)
            {
                button.Placement = TabPlacement;
            }
        }
    }

    protected override void OnDpiChanged(uint oldDpi, uint newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);

        // Tab contents are detached from the visual tree when not selected.
        // Window DPI broadcasts won't reach them, so their cached fonts/measures can remain stale.
        var selectedContent = SelectedTab?.Content;
        for (int i = 0; i < _tabs.Count; i++)
        {
            var content = _tabs[i].Content;
            if (content == null || content == selectedContent)
            {
                continue;
            }

            VisualTree.Visit(content, element =>
            {
                element.ClearDpiCache();

                if (element is FrameworkElement fe)
                {
                    fe.NotifyDpiChanged(oldDpi, newDpi);
                }
                else
                {
                    element.InvalidateMeasure();
                }
            });
        }
    }

    protected override void OnThemeChanged(Theme oldTheme, Theme newTheme)
    {
        base.OnThemeChanged(oldTheme, newTheme);
        // Tab contents are detached from the visual tree when not selected.
        // Window DPI broadcasts won't reach them, so their cached fonts/measures can remain stale.
        var selectedContent = SelectedTab?.Content;
        for (int i = 0; i < _tabs.Count; i++)
        {
            var content = _tabs[i].Content;
            if (content == null || content == selectedContent)
            {
                continue;
            }

            VisualTree.Visit(content, element =>
            {
                element.ClearDpiCache();

                if (element is FrameworkElement control)
                {
                    control.NotifyThemeChanged(oldTheme, newTheme);
                }
            });
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled || !IsEffectivelyEnabled)
        {
            return;
        }

        // Tab key navigation is handled at the Window backend level (it never reaches controls).
        // Keep TabControl navigation on non-Tab keys.
        if (e.ControlKey)
        {
            if (e.Key == Key.PageUp)
            {
                SelectPreviousTab();
                e.Handled = true;
                return;
            }

            if (e.Key == Key.PageDown)
            {
                SelectNextTab();
                e.Handled = true;
                return;
            }
        }

        if ((IsHorizontalPlacement && e.Key == Key.Left) ||
            (!IsHorizontalPlacement && e.Key == Key.Up))
        {
            SelectPreviousTab();
            e.Handled = true;
            return;
        }

        if ((IsHorizontalPlacement && e.Key == Key.Right) ||
            (!IsHorizontalPlacement && e.Key == Key.Down))
        {
            SelectNextTab();
            e.Handled = true;
            return;
        }
    }

    public void AddTab(TabItem tab)
    {
        ArgumentNullException.ThrowIfNull(tab);
        if (tab.Header == null)
        {
            throw new ArgumentException("TabItem.Header must be set.", nameof(tab));
        }

        if (tab.Content == null)
        {
            throw new ArgumentException("TabItem.Content must be set.", nameof(tab));
        }

        _tabs.Add(tab);
        RebuildHeaders();
        EnsureValidSelection();
        InvalidateMeasure();
        InvalidateVisual();
    }

    bool IVisualTreeHost.VisitChildren(Func<Element, bool> visitor)
    {
        if (!visitor(_headerStrip))
        {
            return false;
        }

        var content = SelectedTab?.Content;
        return content == null || visitor(content);
    }

    Element? IFocusTraversalScope.ActiveTraversalRoot => SelectedTab?.Content;

    public void AddTabs(params TabItem[] tabs)
    {
        ArgumentNullException.ThrowIfNull(tabs);

        for (int i = 0; i < tabs.Length; i++)
        {
            AddTab(tabs[i]);
        }
    }

    public void ClearTabs()
    {
        DetachCurrentContent();
        _tabs.Clear();
        _headerStrip.Clear();
        _lastTab = null;
        SelectedIndex = -1;
        InvalidateMeasure();
        InvalidateVisual();
    }

    public void RemoveTabAt(int index)
    {
        if ((uint)index >= (uint)_tabs.Count)
        {
            return;
        }

        var removedTab = _tabs[index];
        if (_lastTab == removedTab)
        {
            DetachCurrentContent();
            _lastTab = null;
        }

        int oldSelected = SelectedIndex;
        _tabs.RemoveAt(index);

        // Closing the active tab falls back to the previous tab; closing a tab before
        // the active one shifts the index down so the same TabItem stays selected.
        int newSelected;
        if (_tabs.Count == 0)
            newSelected = -1;
        else if (index == oldSelected)
            newSelected = Math.Max(0, index - 1);
        else if (index < oldSelected)
            newSelected = oldSelected - 1;
        else
            newSelected = oldSelected;

        RebuildHeaders();
        SelectedIndex = newSelected;
        EnsureValidSelection();
        InvalidateMeasure();
        InvalidateVisual();
    }

    protected override Size MeasureContent(Size availableSize)
    {
        var borderInset = GetBorderVisualInset();
        var border = borderInset > 0 ? new Thickness(borderInset) : Thickness.Zero;

        var inner = availableSize.Deflate(border);

        _headerStrip.Measure(IsHorizontalPlacement
            ? new Size(inner.Width, double.PositiveInfinity)
            : new Size(double.PositiveInfinity, inner.Height));

        double headerSize = IsHorizontalPlacement
            ? _headerStrip.DesiredSize.Height
            : _headerStrip.DesiredSize.Width;

        double contentW = IsHorizontalPlacement
            ? inner.Width
            : SubtractAvailable(inner.Width, headerSize);
        double contentH = IsHorizontalPlacement
            ? SubtractAvailable(inner.Height, headerSize)
            : inner.Height;

        var contentDesired = Size.Empty;
        var content = SelectedTab?.Content;
        if (content != null)
        {
            var contentAvailable = new Size(contentW, contentH).Deflate(Padding);
            content.Measure(contentAvailable);
            contentDesired = content.DesiredSize.Inflate(Padding);
        }

        double desiredW = IsHorizontalPlacement
            ? Math.Max(_headerStrip.DesiredSize.Width, contentDesired.Width)
            : _headerStrip.DesiredSize.Width + contentDesired.Width;
        double desiredH = IsHorizontalPlacement
            ? _headerStrip.DesiredSize.Height + contentDesired.Height
            : Math.Max(_headerStrip.DesiredSize.Height, contentDesired.Height);

        return new Size(desiredW, desiredH).Inflate(border);
    }

    protected override void ArrangeContent(Rect bounds)
    {
        var borderInset = GetBorderVisualInset();
        var border = borderInset > 0 ? new Thickness(borderInset) : Thickness.Zero;

        var inner = bounds.Deflate(border);

        var (headerBounds, contentBounds) = GetLayoutRects(inner);
        _headerStrip.Arrange(headerBounds);
        SelectedTab?.Content?.Arrange(contentBounds.Deflate(Padding));
    }

    protected override void OnRender(IGraphicsContext context)
    {
        // Header must render BEFORE content background so the background
        // paints over the header's bottom edge, visually connecting the
        // selected tab to the content area.
        _headerStrip.Render(context);

        var bounds = GetSnappedBorderBounds(Bounds);
        var borderInset = GetBorderVisualInset();
        var inner = bounds.Deflate(new Thickness(borderInset));

        var contentBg = GetValue(BackgroundProperty);
        var (_, contentRect) = GetLayoutRects(inner);

        var outline = BorderBrush;

        if (contentRect.Width <= 0 || contentRect.Height <= 0)
        {
            return;
        }

        var cornerRadius = TabPlacement switch
        {
            TabPlacement.Bottom => new CornerRadius(CornerRadius, CornerRadius, 0, 0),
            TabPlacement.Left => new CornerRadius(0, CornerRadius, CornerRadius, 0),
            TabPlacement.Right => new CornerRadius(CornerRadius, 0, 0, CornerRadius),
            _ => new CornerRadius(0, 0, CornerRadius, CornerRadius),
        };

        DrawBackgroundAndBorder(context, contentRect, contentBg, outline,
            new Thickness(BorderThickness), cornerRadius);
        if (borderInset > 0)
        {
            DrawContentOutline(context, contentRect, contentBg, borderInset);
        }
    }

    protected override void RenderSubtree(IGraphicsContext context)
    {
        SelectedTab?.Content?.Render(context);
    }

    protected override UIElement? OnHitTest(Point point)
    {
        if (!IsVisible || !IsHitTestVisible || !IsEffectivelyEnabled)
        {
            return null;
        }

        var headerHit = _headerStrip.HitTest(point);
        if (headerHit != null)
        {
            return headerHit;
        }

        if (SelectedTab?.Content is UIElement uiContent)
        {
            var contentHit = uiContent.HitTest(point);
            if (contentHit != null)
            {
                return contentHit;
            }
        }

        return Bounds.Contains(point) ? this : null;
    }

    private void RebuildHeaders()
    {
        _headerStrip.Clear();

        for (int i = 0; i < _tabs.Count; i++)
        {
            var tab = _tabs[i];
            var header = new TabHeaderButton
            {
                Index = i,
                IsSelected = i == SelectedIndex,
                IsEnabled = tab.IsEnabled,
                Content = tab.Header!,
                Placement = TabPlacement,
            };
            header.ClickedCallback = idx =>
            {
                SelectedIndex = idx;
                var root = FindVisualRoot();
                if (root is Window window)
                {
                    window.FocusManager.SetFocus(this, resolveDefault: false);
                }
            };

            _headerStrip.Add(header);
        }
    }

    private void EnsureValidSelection()
    {
        if (_tabs.Count == 0)
        {
            SelectedIndex = -1;
            return;
        }

        if (SelectedIndex < 0 || SelectedIndex >= _tabs.Count)
        {
            SelectedIndex = 0;
        }
        else
        {
            UpdateSelection();
        }
    }

    private void DetachCurrentContent()
    {
        if (_lastTab?.Content != null)
        {
            _lastTab.Content.Parent = null;
        }
    }

    private void UpdateSelection()
    {
        var root = FindVisualRoot();
        var window = root as Window;
        var oldContent = _lastTab?.Content;
        bool focusWasInOldContent = false;

        if (window != null && oldContent != null)
        {
            var focused = window.FocusManager.FocusedElement;
            for (Element? current = focused; current != null; current = current.Parent)
            {
                if (ReferenceEquals(current, oldContent))
                {
                    focusWasInOldContent = true;
                    break;
                }
            }
        }

        RefreshFocusCache();
        for (int i = 0; i < _headerStrip.Count; i++)
        {
            if (_headerStrip[i] is TabHeaderButton btn)
            {
                btn.IsSelected = i == SelectedIndex;
                btn.IsEnabled = i >= 0 && i < _tabs.Count && _tabs[i].IsEnabled;
                btn.InvalidateVisual();
            }
        }

        var selected = SelectedTab;
        if (!ReferenceEquals(_lastTab, selected))
        {
            if (oldContent != null)
            {
                oldContent.Parent = null;
            }
            if (selected?.Content != null)
            {
                selected.Content.Parent = this;
            }
            _lastTab = selected;
        }

        InvalidateMeasure();
        InvalidateVisual();

        if (window != null)
        {
            // If the selected tab swap detached the focused element, move focus into the new tab
            // so KeyUp/Focus-based RequerySuggested keeps working (and key events don't go to a detached element).
            if (focusWasInOldContent)
            {
                if (!window.FocusManager.SetFocus(this, resolveDefault: false))
                {
                    window.RequerySuggested();
                }
            }
            else
            {
                window.RequerySuggested();
            }
        }
    }

    private void SelectPreviousTab()
    {
        if (_tabs.Count == 0)
        {
            return;
        }

        int i = SelectedIndex < 0 ? 0 : SelectedIndex;
        for (int step = 0; step < _tabs.Count; step++)
        {
            i = (i - 1 + _tabs.Count) % _tabs.Count;
            if (_tabs[i].IsEnabled)
            {
                SelectedIndex = i;
                return;
            }
        }
    }

    private void SelectNextTab()
    {
        if (_tabs.Count == 0)
        {
            return;
        }

        int i = SelectedIndex < 0 ? -1 : SelectedIndex;
        for (int step = 0; step < _tabs.Count; step++)
        {
            i = (i + 1) % _tabs.Count;
            if (_tabs[i].IsEnabled)
            {
                SelectedIndex = i;
                return;
            }
        }
    }

    private void RefreshFocusCache()
    {
        _cachedFocusedHeaderIndex = -1;
        for (int i = 0; i < _headerStrip.Count; i++)
        {
            if (_headerStrip[i] is TabHeaderButton btn && btn.IsFocused)
            {
                _cachedFocusedHeaderIndex = i;
                break;
            }
        }
    }

    protected override void OnVisualStateChanged(VisualState oldState, VisualState newState)
    {
        base.OnVisualStateChanged(oldState, newState);

        bool focusChanged = oldState.IsFocused != newState.IsFocused;
        if (!focusChanged)
        {
            return;
        }

        for (int i = 0; i < _headerStrip.Count; i++)
        {
            if (_headerStrip[i] is TabHeaderButton btn)
            {
                btn.RefreshOwnerState();
            }
        }
    }

    private static double SubtractAvailable(double available, double amount) =>
        double.IsPositiveInfinity(available) ? double.PositiveInfinity : Math.Max(0, available - amount);

    private (Rect Header, Rect Content) GetLayoutRects(Rect inner)
    {
        double headerWidth = Math.Clamp(_headerStrip.DesiredSize.Width, 0, inner.Width);
        double headerHeight = Math.Clamp(_headerStrip.DesiredSize.Height, 0, inner.Height);

        return TabPlacement switch
        {
            TabPlacement.Bottom => (
                new Rect(inner.X, inner.Bottom - headerHeight, inner.Width, headerHeight),
                new Rect(inner.X, inner.Y, inner.Width, Math.Max(0, inner.Height - headerHeight))),
            TabPlacement.Left => (
                new Rect(inner.X, inner.Y, headerWidth, inner.Height),
                new Rect(inner.X + headerWidth, inner.Y, Math.Max(0, inner.Width - headerWidth), inner.Height)),
            TabPlacement.Right => (
                new Rect(inner.Right - headerWidth, inner.Y, headerWidth, inner.Height),
                new Rect(inner.X, inner.Y, Math.Max(0, inner.Width - headerWidth), inner.Height)),
            _ => (
                new Rect(inner.X, inner.Y, inner.Width, headerHeight),
                new Rect(inner.X, inner.Y + headerHeight, inner.Width, Math.Max(0, inner.Height - headerHeight))),
        };
    }

    private void DrawContentOutline(IGraphicsContext context, Rect contentRect, Color color, double thickness)
    {
        if (contentRect.Width <= 0 || contentRect.Height <= 0)
        {
            return;
        }

        if (SelectedIndex >= 0 &&
            SelectedIndex < _headerStrip.Count &&
            _headerStrip[SelectedIndex] is TabHeaderButton btn)
        {
            Rect rect;
            if (IsHorizontalPlacement)
            {
                if (btn.Bounds.Width <= 0)
                {
                    return;
                }

                double gapLeft = Math.Clamp(btn.Bounds.Left + thickness, contentRect.Left, contentRect.Right);
                double gapRight = Math.Clamp(btn.Bounds.Right - thickness, contentRect.Left, contentRect.Right);
                if (gapRight <= gapLeft)
                {
                    return;
                }

                double edgeY = TabPlacement == TabPlacement.Bottom
                    ? contentRect.Bottom
                    : contentRect.Top;
                // Centre a two-border-thickness seam on the content edge. This fully covers the
                // outline for both inward-facing Top and outward-facing Bottom placements.
                rect = new Rect(gapLeft, edgeY - thickness, gapRight - gapLeft, thickness * 2);
            }
            else
            {
                if (btn.Bounds.Height <= 0)
                {
                    return;
                }

                double gapTop = Math.Clamp(btn.Bounds.Top + thickness, contentRect.Top, contentRect.Bottom);
                double gapBottom = Math.Clamp(btn.Bounds.Bottom - thickness, contentRect.Top, contentRect.Bottom);
                if (gapBottom <= gapTop)
                {
                    return;
                }

                double edgeX = TabPlacement == TabPlacement.Right
                    ? contentRect.Right
                    : contentRect.Left;
                // Right places the shared edge at the content's outer bound, so use the same
                // centred seam as MewDock to avoid leaving a rounded/snapped border sliver.
                rect = new Rect(edgeX - thickness, gapTop, thickness * 2, gapBottom - gapTop);
            }

            context.FillRectangle(rect, color);
        }
    }
}

/// <summary>
/// Specifies where a <see cref="TabControl"/> places its tab headers.
/// </summary>
public enum TabPlacement
{
    Top,
    Bottom,
    Left,
    Right,
}
