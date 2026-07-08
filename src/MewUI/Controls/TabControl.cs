using Aprillz.MewUI.Input;
using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.Controls;

/// <summary>
/// A tabbed control with header buttons and content display.
/// </summary>
public sealed class TabControl : Control, ISelector, IIndexedSelector
    , IVisualTreeHost
    , IFocusTraversalScope
{
    private const double HeaderSpacing = 2.0;

    private readonly List<TabItem> _tabs = new();
    private readonly List<TabHeaderButton> _headers = new();
    private readonly HashSet<TabHeaderButton> _hiddenHeaders = new();
    private readonly Button _overflowButton;
    private TabItem? _lastTab;
    private Element? _lastContent;
    private int _cachedFocusedHeaderIndex = -1;
    private Size _headerDesiredSize;
    private bool _overflowActive;

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

    public static readonly MewProperty<object?> SelectedItemProperty =
        MewProperty<object?>.Register<TabControl>(nameof(SelectedItem), null,
            MewPropertyOptions.BindsTwoWayByDefault,
            static (self, _, newVal) => self.OnSelectedItemPropertyChanged(newVal));

    private bool _syncingSelection;

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
        SyncSelectedItemFromIndex();
        SelectionChanged?.Invoke(SelectedTab);
    }

    private void OnSelectedItemPropertyChanged(object? item)
    {
        if (_syncingSelection) return;
        _syncingSelection = true;
        try { SelectedIndex = item is TabItem tab ? _tabs.IndexOf(tab) : -1; }
        finally { _syncingSelection = false; }
        SyncSelectedItemFromIndex();
    }

    private void SyncSelectedItemFromIndex()
    {
        if (_syncingSelection) return;
        _syncingSelection = true;
        try { SetValue(SelectedItemProperty, SelectedTab); }
        finally { _syncingSelection = false; }
    }

    /// <summary>
    /// Gets the currently selected tab item.
    /// </summary>
    public TabItem? SelectedTab => SelectedIndex >= 0 && SelectedIndex < _tabs.Count ? _tabs[SelectedIndex] : null;

    /// <summary>
    /// Gets the currently selected item object for selection consistency.
    /// </summary>
    public object? SelectedItem
    {
        get => GetValue(SelectedItemProperty);
        set => SetValue(SelectedItemProperty, value);
    }

    /// <summary>
    /// Occurs when the selected tab changes.
    /// </summary>
    public event Action<object?>? SelectionChanged;

    static TabControl()
    {
        FocusableProperty.OverrideDefaultValue<TabControl>(true);
    }

    public TabControl()
    {
        _overflowButton = new Button
        {
            Content = new GlyphElement { Kind = GlyphKind.ChevronDown },
            StyleName = BuiltInStyles.FlatButton,
            Padding = new Thickness(0),
            MinWidth = 18,
            MinHeight = 18,
            Focusable = false,
            IsTabStop = false,
        };
        _overflowButton.Click += ShowOverflowMenu;
        _overflowButton.Parent = this;
    }

    private bool IsHorizontalPlacement =>
        TabPlacement is TabPlacement.Top or TabPlacement.Bottom;

    private void OnTabPlacementChanged()
    {
        for (int i = 0; i < _headers.Count; i++)
        {
            _headers[i].Placement = TabPlacement;
        }

        InvalidateMeasure();
        InvalidateVisual();
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
        AttachTab(tab);
        RebuildHeaders();
        EnsureValidSelection();
        InvalidateMeasure();
        InvalidateVisual();
    }

    bool IVisualTreeHost.VisitChildren(Func<Element, bool> visitor)
    {
        for (int i = 0; i < _headers.Count; i++)
        {
            if (!visitor(_headers[i]))
            {
                return false;
            }
        }

        if (!visitor(_overflowButton))
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
        for (int i = 0; i < _tabs.Count; i++)
        {
            DetachTab(_tabs[i]);
        }
        _tabs.Clear();
        ClearHeaders();
        _lastTab = null;
        _lastContent = null;
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
        DetachTab(removedTab);
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

        MeasureHeaders(inner);

        double headerSize = IsHorizontalPlacement
            ? _headerDesiredSize.Height
            : _headerDesiredSize.Width;

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
            ? Math.Max(_headerDesiredSize.Width, contentDesired.Width)
            : _headerDesiredSize.Width + contentDesired.Width;
        double desiredH = IsHorizontalPlacement
            ? _headerDesiredSize.Height + contentDesired.Height
            : Math.Max(_headerDesiredSize.Height, contentDesired.Height);

        return new Size(desiredW, desiredH).Inflate(border);
    }

    protected override void ArrangeContent(Rect bounds)
    {
        var borderInset = GetBorderVisualInset();
        var border = borderInset > 0 ? new Thickness(borderInset) : Thickness.Zero;

        var inner = bounds.Deflate(border);

        var (headerBounds, contentBounds) = GetLayoutRects(inner);
        ArrangeHeaders(headerBounds);
        SelectedTab?.Content?.Arrange(contentBounds.Deflate(Padding));
    }

    protected override void OnRender(IGraphicsContext context)
    {
        // Header must render BEFORE content background so the background
        // paints over the header's bottom edge, visually connecting the
        // selected tab to the content area.
        RenderHeaders(context);

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

        if (_overflowActive && _overflowButton.HitTest(point) is UIElement overflowHit)
        {
            return overflowHit;
        }

        for (int i = 0; i < _headers.Count; i++)
        {
            var header = _headers[i];
            if (_hiddenHeaders.Contains(header))
            {
                continue;
            }

            var headerHit = header.HitTest(point);
            if (headerHit != null)
            {
                return headerHit;
            }
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
        ClearHeaders();

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
                Parent = this,
            };
            header.ClickedCallback = idx =>
            {
                SelectTabFromHeader(idx);
            };

            _headers.Add(header);
        }
    }

    private void AttachTab(TabItem tab)
    {
        tab.Changed += OnTabItemChanged;
    }

    private void DetachTab(TabItem tab)
    {
        tab.Changed -= OnTabItemChanged;
    }

    private void ClearHeaders()
    {
        for (int i = 0; i < _headers.Count; i++)
        {
            _headers[i].Content = null;
            _headers[i].Parent = null;
        }

        _headers.Clear();
        _hiddenHeaders.Clear();
        _overflowActive = false;
    }

    private void OnTabItemChanged(TabItem tab, TabItemChange change)
    {
        int index = _tabs.IndexOf(tab);
        if (index < 0)
        {
            return;
        }

        switch (change)
        {
            case TabItemChange.Header:
                if (index < _headers.Count)
                {
                    _headers[index].Content = tab.Header;
                }
                InvalidateMeasure();
                InvalidateVisual();
                break;

            case TabItemChange.Content:
                if (ReferenceEquals(tab, SelectedTab))
                {
                    UpdateSelection();
                }
                else
                {
                    InvalidateMeasure();
                    InvalidateVisual();
                }
                break;

            case TabItemChange.IsEnabled:
                if (index < _headers.Count)
                {
                    _headers[index].IsEnabled = tab.IsEnabled;
                    _headers[index].InvalidateVisual();
                }
                break;

            case TabItemChange.HeaderText:
                break;
        }
    }

    private void SelectTabFromHeader(int index)
    {
        SelectedIndex = index;
        var root = FindVisualRoot();
        if (root is Window window)
        {
            window.FocusManager.SetFocus(this, resolveDefault: false);
        }
    }

    private void MeasureHeaders(Size inner)
    {
        if (_headers.Count == 0)
        {
            _headerDesiredSize = Size.Empty;
            _hiddenHeaders.Clear();
            _overflowActive = false;
            _overflowButton.Measure(Size.Empty);
            return;
        }

        double width = 0;
        double height = 0;

        if (IsHorizontalPlacement)
        {
            for (int i = 0; i < _headers.Count; i++)
            {
                var header = _headers[i];
                header.Measure(new Size(double.PositiveInfinity, inner.Height));
                width += header.DesiredSize.Width + (i > 0 ? HeaderSpacing : 0);
                height = Math.Max(height, header.DesiredSize.Height);
            }

            _overflowButton.Measure(new Size(double.PositiveInfinity, inner.Height));
            if (_headers.Count > 1)
            {
                height = Math.Max(height, _overflowButton.DesiredSize.Height);
            }
        }
        else
        {
            for (int i = 0; i < _headers.Count; i++)
            {
                var header = _headers[i];
                header.Measure(new Size(inner.Width, double.PositiveInfinity));
                width = Math.Max(width, header.DesiredSize.Width);
                height += header.DesiredSize.Height + (i > 0 ? HeaderSpacing : 0);
            }

            _overflowButton.Measure(Size.Empty);
            _hiddenHeaders.Clear();
            _overflowActive = false;
        }

        _headerDesiredSize = new Size(width, height);
    }

    private void ArrangeHeaders(Rect headerBounds)
    {
        if (_headers.Count == 0)
        {
            _hiddenHeaders.Clear();
            _overflowActive = false;
            _overflowButton.Arrange(Rect.Empty);
            return;
        }

        if (!IsHorizontalPlacement)
        {
            _hiddenHeaders.Clear();
            _overflowActive = false;
            _overflowButton.Arrange(Rect.Empty);

            double y = headerBounds.Y;
            for (int i = 0; i < _headers.Count; i++)
            {
                var header = _headers[i];
                double height = header.DesiredSize.Height;
                header.Arrange(new Rect(headerBounds.X, y, headerBounds.Width, height));
                y += height + HeaderSpacing;
            }
            return;
        }

        var visible = ResolveVisibleHeaders(headerBounds.Width);
        double x = headerBounds.X;
        for (int i = 0; i < visible.Count; i++)
        {
            var header = visible[i];
            double width = header.DesiredSize.Width;
            header.Arrange(new Rect(x, headerBounds.Y, width, headerBounds.Height));
            x += width + HeaderSpacing;
        }

        for (int i = 0; i < _headers.Count; i++)
        {
            var header = _headers[i];
            if (_hiddenHeaders.Contains(header))
            {
                header.Arrange(new Rect(headerBounds.X, headerBounds.Y, 0, 0));
            }
        }

        if (_overflowActive)
        {
            double width = Math.Min(_overflowButton.DesiredSize.Width, headerBounds.Width);
            double height = Math.Min(_overflowButton.DesiredSize.Height, headerBounds.Height);
            double buttonX = Math.Max(headerBounds.X, headerBounds.Right - width);
            double buttonY = headerBounds.Y + Math.Max(0, (headerBounds.Height - height) / 2);
            _overflowButton.Arrange(new Rect(buttonX, buttonY, width, height));
        }
        else
        {
            _overflowButton.Arrange(Rect.Empty);
        }
    }

    private List<TabHeaderButton> ResolveVisibleHeaders(double availableForHeaders)
    {
        _hiddenHeaders.Clear();

        var visible = new List<TabHeaderButton>();
        if (_headers.Count == 0)
        {
            _overflowActive = false;
            return visible;
        }

        double total = 0;
        for (int i = 0; i < _headers.Count; i++)
        {
            total += _headers[i].DesiredSize.Width + (i > 0 ? HeaderSpacing : 0);
        }

        if (_headers.Count <= 1 || total <= availableForHeaders)
        {
            _overflowActive = false;
            visible.AddRange(_headers);
            return visible;
        }

        double availableForTabs = Math.Max(0, availableForHeaders - _overflowButton.DesiredSize.Width - HeaderSpacing);

        int fitCount = 0;
        double accumulated = 0;
        for (int i = 0; i < _headers.Count; i++)
        {
            double step = _headers[i].DesiredSize.Width + (i > 0 ? HeaderSpacing : 0);
            if (accumulated + step > availableForTabs)
            {
                break;
            }

            accumulated += step;
            fitCount++;
        }

        int selectedIndex = SelectedIndex >= 0 && SelectedIndex < _headers.Count ? SelectedIndex : -1;
        int leadCount = fitCount;
        if (selectedIndex >= leadCount && leadCount > 0)
        {
            leadCount--;
        }

        for (int i = 0; i < _headers.Count; i++)
        {
            if (i < leadCount || i == selectedIndex)
            {
                visible.Add(_headers[i]);
            }
            else
            {
                _hiddenHeaders.Add(_headers[i]);
            }
        }

        if (visible.Count == 0)
        {
            var fallback = selectedIndex >= 0 ? _headers[selectedIndex] : _headers[0];
            visible.Add(fallback);
            _hiddenHeaders.Remove(fallback);
        }

        _overflowActive = _hiddenHeaders.Count > 0;
        return visible;
    }

    private void RenderHeaders(IGraphicsContext context)
    {
        for (int i = 0; i < _headers.Count; i++)
        {
            var header = _headers[i];
            if (!_hiddenHeaders.Contains(header))
            {
                header.Render(context);
            }
        }

        if (_overflowActive)
        {
            _overflowButton.Render(context);
        }
    }

    private void ShowOverflowMenu()
    {
        if (_hiddenHeaders.Count == 0)
        {
            return;
        }

        var menu = new ContextMenu();
        for (int i = 0; i < _tabs.Count && i < _headers.Count; i++)
        {
            if (!_hiddenHeaders.Contains(_headers[i]))
            {
                continue;
            }

            int index = i;
            var tab = _tabs[i];
            menu.AddItem(GetOverflowMenuText(tab, i), () => SelectedIndex = index, tab.IsEnabled);
        }

        var buttonBounds = _overflowButton.Bounds;
        menu.ShowAt(_overflowButton, new Point(buttonBounds.X, buttonBounds.Bottom));
    }

    private static string GetOverflowMenuText(TabItem tab, int index)
    {
        if (!string.IsNullOrEmpty(tab.HeaderText))
        {
            return tab.HeaderText;
        }

        if (tab.Header is TextBlock textBlock && !string.IsNullOrEmpty(textBlock.Text))
        {
            return textBlock.Text;
        }

        return $"Tab {index + 1}";
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
        if (_lastContent != null)
        {
            _lastContent.Parent = null;
            _lastContent = null;
        }
    }

    private void UpdateSelection()
    {
        var root = FindVisualRoot();
        var window = root as Window;
        var oldContent = _lastContent;
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
        for (int i = 0; i < _headers.Count; i++)
        {
            var btn = _headers[i];
            btn.IsSelected = i == SelectedIndex;
            btn.IsEnabled = i >= 0 && i < _tabs.Count && _tabs[i].IsEnabled;
            btn.InvalidateVisual();
        }

        var selected = SelectedTab;
        var selectedContent = selected?.Content;
        if (!ReferenceEquals(_lastTab, selected) || !ReferenceEquals(_lastContent, selectedContent))
        {
            if (oldContent != null)
            {
                oldContent.Parent = null;
            }
            if (selectedContent != null)
            {
                selectedContent.Parent = this;
            }
            _lastTab = selected;
            _lastContent = selectedContent;
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
        for (int i = 0; i < _headers.Count; i++)
        {
            if (_headers[i].IsFocused)
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

        for (int i = 0; i < _headers.Count; i++)
        {
            _headers[i].RefreshOwnerState();
        }
    }

    private static double SubtractAvailable(double available, double amount) =>
        double.IsPositiveInfinity(available) ? double.PositiveInfinity : Math.Max(0, available - amount);

    private (Rect Header, Rect Content) GetLayoutRects(Rect inner)
    {
        double headerWidth = Math.Clamp(_headerDesiredSize.Width, 0, inner.Width);
        double headerHeight = Math.Clamp(_headerDesiredSize.Height, 0, inner.Height);

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
            SelectedIndex < _headers.Count)
        {
            var btn = _headers[SelectedIndex];
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
