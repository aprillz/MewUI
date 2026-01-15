using Aprillz.MewUI.Input;
using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.Controls;

public sealed class TabControl : Control
    , IVisualTreeHost
{
    private readonly record struct TabScrollOffsets(double Horizontal, double Vertical);

    private readonly List<TabItem> _tabs = new();
    private readonly StackPanel _headerStrip;
    private readonly ScrollViewer _contentHost;
    private readonly Dictionary<TabItem, TabScrollOffsets> _tabOffsets = new();
    private TabItem? _lastTab;
    private bool _pendingOffsetRestore;
    private TabScrollOffsets _pendingOffsets;
    private int _cachedFocusedHeaderIndex = -1;
    private Thickness _chromePadding = new(1);

    internal override UIElement GetDefaultFocusTarget()
    {
        var target = FocusManager.FindFirstFocusable(SelectedTab?.Content);
        return target ?? this;
    }

    public IReadOnlyList<TabItem> Tabs => _tabs;

    public int SelectedIndex
    {
        get;
        set
        {
            int clamped = _tabs.Count == 0 ? -1 : Math.Clamp(value, 0, _tabs.Count - 1);
            if (field == clamped)
            {
                return;
            }

            field = clamped;
            UpdateSelection();
            SelectionChanged?.Invoke(field);
        }
    } = -1;

    public TabItem? SelectedTab => SelectedIndex >= 0 && SelectedIndex < _tabs.Count ? _tabs[SelectedIndex] : null;

    public event Action<int>? SelectionChanged;

    public ScrollMode VerticalScroll
    {
        get => _contentHost.VerticalScroll;
        set
        {
            if (_contentHost.VerticalScroll == value)
            {
                return;
            }

            _contentHost.VerticalScroll = value;
            InvalidateMeasure();
            InvalidateVisual();
        }
    }

    public ScrollMode HorizontalScroll
    {
        get => _contentHost.HorizontalScroll;
        set
        {
            if (_contentHost.HorizontalScroll == value)
            {
                return;
            }

            _contentHost.HorizontalScroll = value;
            InvalidateMeasure();
            InvalidateVisual();
        }
    }

    public new Thickness Padding
    {
        get => _contentHost.Padding;
        set
        {
            if (_contentHost.Padding == value)
            {
                return;
            }

            _contentHost.Padding = value;
            InvalidateMeasure();
            InvalidateVisual();
        }
    }

    protected override Color DefaultBackground => Theme.Current.Palette.ContainerBackground;

    protected override Color DefaultBorderBrush => Theme.Current.Palette.ControlBorder;

    public override bool Focusable => true;

    public TabControl()
    {
        BorderThickness = 1;
        base.Padding = Thickness.Zero;

        _headerStrip = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 2,
            Padding = new Thickness(2),
        };
        _headerStrip.Parent = this;

        _contentHost = new ScrollViewer
        {
            VerticalScroll = ScrollMode.Disabled,
            HorizontalScroll = ScrollMode.Disabled,
        };
        _contentHost.Parent = this;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled || !IsEnabled)
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

    void IVisualTreeHost.VisitChildren(Action<Element> visitor)
    {
        visitor(_headerStrip);
        visitor(_contentHost);
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
        _tabs.Clear();
        _headerStrip.Clear();
        _contentHost.Content = null;
        _tabOffsets.Clear();
        _lastTab = null;
        _pendingOffsetRestore = false;
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
        _tabs.RemoveAt(index);
        _tabOffsets.Remove(removedTab);
        if (_lastTab == removedTab)
        {
            _lastTab = null;
        }
        RebuildHeaders();
        EnsureValidSelection();
        InvalidateMeasure();
        InvalidateVisual();
    }

    protected override Size MeasureContent(Size availableSize)
    {
        var inner = availableSize.Deflate(_chromePadding);

        _headerStrip.Measure(new Size(inner.Width, double.PositiveInfinity));
        double headerH = _headerStrip.DesiredSize.Height;

        double contentW = inner.Width;
        double contentH = double.IsPositiveInfinity(inner.Height) ? double.PositiveInfinity : Math.Max(0, inner.Height - headerH);

        _contentHost.Measure(new Size(contentW, contentH));

        double desiredW = Math.Max(_headerStrip.DesiredSize.Width, _contentHost.DesiredSize.Width);
        double desiredH = headerH + _contentHost.DesiredSize.Height;

        return new Size(desiredW, desiredH).Inflate(_chromePadding);
    }

    protected override void ArrangeContent(Rect bounds)
    {
        var inner = bounds.Deflate(_chromePadding);

        double headerH = _headerStrip.DesiredSize.Height;
        _headerStrip.Arrange(new Rect(inner.X, inner.Y, inner.Width, headerH));

        var contentBounds = new Rect(inner.X, inner.Y + headerH, inner.Width, Math.Max(0, inner.Height - headerH));
        _contentHost.Arrange(contentBounds);

        if (_pendingOffsetRestore)
        {
            _pendingOffsetRestore = false;
            _contentHost.SetScrollOffsets(_pendingOffsets.Horizontal, _pendingOffsets.Vertical);
        }
    }

    protected override void OnRender(IGraphicsContext context)
    {
        var theme = GetTheme();
        var bounds = GetSnappedBorderBounds(Bounds);
        var borderInset = GetBorderVisualInset();
        var inner = bounds.Deflate(new Thickness(borderInset));

        double headerH = _headerStrip.Bounds.Height;
        if (headerH <= 0)
        {
            headerH = _headerStrip.DesiredSize.Height;
        }

        var stripBg = GetTabStripBackground(theme);
        var contentBg = theme.Palette.ContainerBackground;

        var headerRect = new Rect(inner.X, inner.Y, inner.Width, Math.Max(0, headerH));
        var contentRect = new Rect(
            inner.X,
            inner.Y + headerRect.Height - theme.ControlCornerRadius + borderInset,
            inner.Width,
            Math.Max(0, inner.Height - headerRect.Height + theme.ControlCornerRadius - borderInset));

        if (contentRect.Height > 0)
        {
            context.FillRectangle(contentRect, contentBg);
        }

        // Outline: focus color should wrap selected header + content.
        var outline = GetOutlineColor(theme);
        DrawContentOutline(context, contentRect, outline, thickness: Math.Max(1, borderInset));
        //context.DrawRectangle(contentRect, outline, borderInset);
    }

    public override void Render(IGraphicsContext context)
    {
        _headerStrip.Render(context);
        base.Render(context);
        _contentHost.Render(context);
    }

    public override UIElement? HitTest(Point point)
    {
        if (!IsVisible || !IsHitTestVisible)
        {
            return null;
        }

        var headerHit = _headerStrip.HitTest(point);
        if (headerHit != null)
        {
            return headerHit;
        }

        var contentHit = _contentHost.HitTest(point);
        if (contentHit != null)
        {
            return contentHit;
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
                IsTabEnabled = tab.IsEnabled,
                Content = tab.Header!,
            };
            header.Clicked += idx =>
            {
                SelectedIndex = idx;
                Focus();
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

    private void UpdateSelection()
    {
        var root = FindVisualRoot();
        var window = root as Window;
        var oldContent = _contentHost.Content;
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
                btn.IsTabEnabled = i >= 0 && i < _tabs.Count && _tabs[i].IsEnabled;
                btn.InvalidateVisual();
            }
        }

        if (_lastTab != null)
        {
            _tabOffsets[_lastTab] = new TabScrollOffsets(_contentHost.HorizontalOffset, _contentHost.VerticalOffset);
        }

        // The ScrollViewer persists offsets across content swaps; clear immediately so the previous tab's
        // offsets don't "bleed" into the newly selected tab before we restore the saved offsets.
        _contentHost.SetScrollOffsets(0, 0);

        var selected = SelectedTab;
        _contentHost.Content = SelectedTab?.Content;

        _lastTab = selected;
        if (selected != null && _tabOffsets.TryGetValue(selected, out var offsets))
        {
            _pendingOffsets = offsets;
        }
        else
        {
            _pendingOffsets = default;
        }

        _pendingOffsetRestore = true;
        InvalidateMeasure();
        InvalidateVisual();

        if (window != null)
        {
            // If the selected tab swap detached the focused element, move focus into the new tab
            // so KeyUp/Focus-based RequerySuggested keeps working (and key events don't go to a detached element).
            if (focusWasInOldContent)
            {
                if (!window.FocusManager.SetFocus(this))
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

    private bool HasFocusWithin() => IsFocusWithin;

    internal Color GetOutlineColor(Theme theme) => HasFocusWithin() ? theme.Palette.Accent : theme.Palette.ControlBorder;

    internal Color GetTabStripBackground(Theme theme) => theme.Palette.ButtonFace;

    internal Color GetTabBackground(Theme theme, bool isSelected) => isSelected ? theme.Palette.ContainerBackground : GetTabStripBackground(theme);

    private void DrawContentOutline(IGraphicsContext context, Rect contentRect, Color color, double thickness)
    {
        if (contentRect.Width <= 0 || contentRect.Height <= 0)
        {
            return;
        }

        thickness = Math.Max(1, thickness);

        var topY = contentRect.Y;
        var leftX = contentRect.X;
        var rightX = contentRect.Right;
        var bottomY = contentRect.Bottom;

        // Left / Right / Bottom always.
        context.DrawLine(new Point(leftX, topY), new Point(leftX, bottomY), color, thickness);
        context.DrawLine(new Point(rightX, topY), new Point(rightX, bottomY), color, thickness);
        context.DrawLine(new Point(leftX, bottomY), new Point(rightX, bottomY), color, thickness);

        // Top: leave a gap under the selected tab so the outline wraps header + content.
        if (SelectedIndex >= 0 &&
            SelectedIndex < _headerStrip.Count &&
            _headerStrip[SelectedIndex] is TabHeaderButton btn &&
            btn.Bounds.Width > 0)
        {
            double gapL = Math.Clamp(btn.Bounds.X, leftX, rightX);
            double gapR = Math.Clamp(btn.Bounds.Right, leftX, rightX);

            if (gapL > leftX)
            {
                context.DrawLine(new Point(leftX, topY), new Point(gapL + thickness, topY), color, thickness);
            }

            if (gapR < rightX)
            {
                context.DrawLine(new Point(gapR - thickness, topY), new Point(rightX, topY), color, thickness);
            }
        }
        else
        {
            context.DrawLine(new Point(leftX, topY), new Point(rightX, topY), color, thickness);
        }
    }
}
