using Aprillz.MewUI.Input;
using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.Controls;

/// <summary>
/// A tabbed control with header buttons and content display.
/// </summary>
public sealed class TabControl : Control
    , IVisualTreeHost
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

        if (e.Key == Key.Left)
        {
            SelectPreviousTab();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Right)
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
        _tabs.RemoveAt(index);
        RebuildHeaders();
        EnsureValidSelection();
        InvalidateMeasure();
        InvalidateVisual();
    }

    protected override Size MeasureContent(Size availableSize)
    {
        var borderInset = GetBorderVisualInset();
        var border = borderInset > 0 ? new Thickness(borderInset) : Thickness.Zero;

        var inner = availableSize.Deflate(border);

        _headerStrip.Measure(new Size(inner.Width, double.PositiveInfinity));
        double headerH = _headerStrip.DesiredSize.Height;

        double contentW = inner.Width;
        double contentH = double.IsPositiveInfinity(inner.Height) ? double.PositiveInfinity : Math.Max(0, inner.Height - headerH);

        var contentDesired = Size.Empty;
        var content = SelectedTab?.Content;
        if (content != null)
        {
            var contentAvailable = new Size(contentW, contentH).Deflate(Padding);
            content.Measure(contentAvailable);
            contentDesired = content.DesiredSize.Inflate(Padding);
        }

        double desiredW = Math.Max(_headerStrip.DesiredSize.Width, contentDesired.Width);
        double desiredH = headerH + contentDesired.Height;

        return new Size(desiredW, desiredH).Inflate(border);
    }

    protected override void ArrangeContent(Rect bounds)
    {
        var borderInset = GetBorderVisualInset();
        var border = borderInset > 0 ? new Thickness(borderInset) : Thickness.Zero;

        var inner = bounds.Deflate(border);

        double headerH = _headerStrip.DesiredSize.Height;
        _headerStrip.Arrange(new Rect(inner.X, inner.Y, inner.Width, headerH));

        var contentBounds = new Rect(inner.X, inner.Y + headerH, inner.Width, Math.Max(0, inner.Height - headerH));
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

        double headerH = _headerStrip.Bounds.Height;
        if (headerH <= 0)
        {
            headerH = _headerStrip.DesiredSize.Height;
        }

        var contentBg = GetValue(BackgroundProperty);

        var headerRect = new Rect(inner.X, inner.Y, inner.Width, Math.Max(0, headerH));

        var contentRect = new Rect(
            inner.X,
            inner.Y + headerRect.Height,
            inner.Width,
            Math.Max(0, inner.Height - headerRect.Height));

        var outline = BorderBrush;

        if (contentRect.Height <= 0)
        {
            return;
        }

        DrawBackgroundAndBorder(context, contentRect, contentBg, outline, new Thickness(BorderThickness), new CornerRadius(0, 0, CornerRadius, CornerRadius));
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

    private void DrawContentOutline(IGraphicsContext context, Rect contentRect, Color color, double thickness)
    {
        if (contentRect.Width <= 0 || contentRect.Height <= 0)
        {
            return;
        }

        var halfThickness = (thickness / 2);

        var topY = contentRect.Y;
        var leftX = contentRect.X;
        var rightX = contentRect.Right;

        if (SelectedIndex >= 0 &&
            SelectedIndex < _headerStrip.Count &&
            _headerStrip[SelectedIndex] is TabHeaderButton btn &&
            btn.Bounds.Width > 0)
        {
            double gapL = Math.Clamp(btn.Bounds.Left + thickness, leftX, rightX);
            double gapR = Math.Clamp(btn.Bounds.Right - thickness, leftX, rightX);

            var rect = new Rect(gapL, topY - halfThickness, gapR - gapL, thickness * 2);

            context.FillRectangle(rect, color);
        }
    }
}
