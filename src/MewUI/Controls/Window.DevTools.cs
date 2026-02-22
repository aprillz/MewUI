#if DEBUG
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Input;
using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI;

public partial class Window
{
    private Adorner? _debugInspectorAdorner;
    private DebugInspectorOverlay? _debugInspectorOverlay;
    private DebugVisualTreeWindow? _debugVisualTreeWindow;

#if DEBUG
    public void DevToolsToggleInspector() => ToggleDebugInspector();

    public void DevToolsToggleVisualTree() => ToggleDebugVisualTree();

    public bool DevToolsInspectorIsOpen => _debugInspectorAdorner != null;

    public bool DevToolsVisualTreeIsOpen => _debugVisualTreeWindow != null;

    public event Action<bool>? DevToolsInspectorOpenChanged;

    public event Action<bool>? DevToolsVisualTreeOpenChanged;
#endif

    private void InitializeDebugDevTools()
    {
        PreviewKeyDown += OnDebugDevToolsPreviewKeyDown;
    }

    private void OnDebugDevToolsPreviewKeyDown(KeyEventArgs e)
    {
        var chord = e.ShiftKey &&
                    (e.Modifiers.HasFlag(ModifierKeys.Control) || e.Modifiers.HasFlag(ModifierKeys.Windows));

        // Ctrl/Cmd + Shift + I: toggle inspector overlay.
        if (chord && e.Key == Key.I)
        {
            ToggleDebugInspector();
            e.Handled = true;
            return;
        }

        // Ctrl/Cmd + Shift + T: toggle visual tree window.
        if (chord && e.Key == Key.T)
        {
            ToggleDebugVisualTree();
            e.Handled = true;
        }
    }

    private void ToggleDebugInspector()
    {
        if (_debugInspectorAdorner != null)
        {
            AdornerLayer.Remove(_debugInspectorAdorner);
            _debugInspectorAdorner = null;
            _debugInspectorOverlay = null;
            RequestLayout();
            RequestRender();
#if DEBUG
            DevToolsInspectorOpenChanged?.Invoke(false);
#endif
            return;
        }

        _debugInspectorOverlay = new DebugInspectorOverlay(this)
        {
            IsHitTestVisible = false,
            IsVisible = true,
        };

        _debugInspectorAdorner = new Adorner(this, _debugInspectorOverlay)
        {
            IsHitTestVisible = false,
            IsVisible = true,
        };

        AdornerLayer.Add(_debugInspectorAdorner);
#if DEBUG
        DevToolsInspectorOpenChanged?.Invoke(true);
#endif
    }

    private void ToggleDebugVisualTree()
    {
        if (_debugVisualTreeWindow != null)
        {
            try { _debugVisualTreeWindow.Close(); } catch { }
            _debugVisualTreeWindow = null;
#if DEBUG
            DevToolsVisualTreeOpenChanged?.Invoke(false);
#endif
            return;
        }

        // The tree window is much more useful with the overlay on (selection highlighting),
        // so ensure it's enabled.
        if (_debugInspectorOverlay == null)
        {
            ToggleDebugInspector();
        }

        var treeWindow = new DebugVisualTreeWindow(this);
        _debugVisualTreeWindow = treeWindow;

        treeWindow.Closed += () =>
        {
            if (ReferenceEquals(_debugVisualTreeWindow, treeWindow))
            {
                _debugVisualTreeWindow = null;
#if DEBUG
                DevToolsVisualTreeOpenChanged?.Invoke(false);
#endif
            }

            if (_debugInspectorOverlay != null)
            {
                _debugInspectorOverlay.HighlightedElement = null;
                RequestRender();
            }
        };

        Closed += CloseTreeOnOwnerClose;
        void CloseTreeOnOwnerClose()
        {
            Closed -= CloseTreeOnOwnerClose;
            try { _debugVisualTreeWindow?.Close(); } catch { }
            _debugVisualTreeWindow = null;
#if DEBUG
            DevToolsVisualTreeOpenChanged?.Invoke(false);
#endif
        }

        treeWindow.Show();
#if DEBUG
        DevToolsVisualTreeOpenChanged?.Invoke(true);
#endif
    }

    partial void DebugOnAfterMouseDownHitTest(Point positionInWindow, MouseButton button, UIElement? element)
    {
        _debugVisualTreeWindow?.OnTargetMouseDown(positionInWindow, button, element);
    }

    private sealed class DebugInspectorOverlay : Control
    {
        private readonly Window _window;
        private string? _cachedText;
        private UIElement? _cachedHovered;
        private UIElement? _cachedFocused;
        private UIElement? _cachedPinned;

        public UIElement? HighlightedElement { get; set; }

        public DebugInspectorOverlay(Window window)
        {
            _window = window;
            Background = Color.Transparent;
        }

        public override bool Focusable => false;

        protected override void OnRender(IGraphicsContext context)
        {
            base.OnRender(context);

            var mousePos = _window.LastMousePositionDip;
            var hovered = _window.HitTest(mousePos);

            // Don't highlight the inspector itself (it should not be hit-testable, but keep this defensive).
            if (hovered is Adorner)
            {
                hovered = null;
            }

            var focused = _window.FocusManager.FocusedElement;
            var pinned = HighlightedElement;

            if (hovered != null)
            {
                DrawElementBounds(context, hovered, Color.FromArgb(255, 80, 160, 255));
            }

            if (focused != null)
            {
                DrawElementBounds(context, focused, Color.FromArgb(255, 80, 255, 120));
            }

            if (pinned != null)
            {
                DrawElementBounds(context, pinned, Color.FromArgb(255, 255, 120, 80));
            }

            DrawInfoPanel(context, hovered, focused, pinned);
        }

        private void DrawElementBounds(IGraphicsContext context, UIElement element, Color color)
        {
            var rect = GetElementRectInWindow(element);
            if (rect.Width <= 0 || rect.Height <= 0)
            {
                return;
            }

            context.DrawRectangle(rect, color, thickness: 2);
        }

        private void DrawInfoPanel(IGraphicsContext context, UIElement? hovered, UIElement? focused, UIElement? pinned)
        {
            var font = GetFont();
            string text = GetOrBuildInspectorText(hovered, focused, pinned);

            var pad = 8.0;
            var size = context.MeasureText(text, font, maxWidth: 420);
            var panelRect = new Rect(Bounds.X + 8, Bounds.Y + 8, size.Width + pad * 2, size.Height + pad * 2);
            var bg = Color.FromArgb(190, 20, 20, 20);
            var border = Color.FromArgb(220, 80, 160, 255);
            context.FillRoundedRectangle(panelRect, 6, 6, bg);
            context.DrawRoundedRectangle(panelRect, 6, 6, border, 1);
            context.DrawText(text, panelRect.Deflate(new Thickness(pad)), font, Color.White, TextAlignment.Left, TextAlignment.Top, TextWrapping.Wrap);
        }

        private string GetOrBuildInspectorText(UIElement? hovered, UIElement? focused, UIElement? pinned)
        {
            if (ReferenceEquals(_cachedHovered, hovered) &&
                ReferenceEquals(_cachedFocused, focused) &&
                ReferenceEquals(_cachedPinned, pinned) &&
                _cachedText != null)
            {
                return _cachedText;
            }

            _cachedHovered = hovered;
            _cachedFocused = focused;
            _cachedPinned = pinned;

            string hoverText = hovered != null ? $"{hovered.GetType().Name} {FormatRect(GetElementRectInWindow(hovered))}" : "(none)";
            string focusText = focused != null ? $"{focused.GetType().Name} {FormatRect(GetElementRectInWindow(focused))}" : "(none)";
            string pinText = pinned != null ? $"{pinned.GetType().Name} {FormatRect(GetElementRectInWindow(pinned))}" : "(none)";

            var sb = new System.Text.StringBuilder(512);
            sb.Append("Inspector: Ctrl/Cmd+Shift+I\n");
            sb.Append("VisualTree: Ctrl/Cmd+Shift+T\n");
            sb.Append("Hover: ").Append(hoverText).Append('\n');
            sb.Append("Focus: ").Append(focusText).Append('\n');
            sb.Append("Selected: ").Append(pinText);

            _cachedText = sb.ToString();
            return _cachedText;
        }

        private static Rect GetElementRectInWindow(UIElement element)
        {
            var size = element.RenderSize;
            var local = new Rect(0, 0, size.Width, size.Height);

            // Translate into Window coordinate space (what the overlay draws in).
            if (element.FindVisualRoot() is Window window)
            {
                return element.TranslateRect(local, window);
            }

            // Fallback to whatever we have (debug-only).
            return element.Bounds;
        }

        private static string FormatRect(Rect r)
            => $"[{r.X:0.#},{r.Y:0.#} {r.Width:0.#}x{r.Height:0.#}]";
    }

    private sealed class DebugVisualTreeWindow : Window
    {
        private readonly Window _target;
        private readonly TreeView _tree;
        private readonly Label _selectedLabel;
        private readonly Label _modeLabel;
        private readonly CheckBox _followFocus;
        private readonly CheckBox _autoExpandFocus;
        private Button? _goFocusButton;

        private readonly Dictionary<object, object?> _parentByKey = new();
        private TreeItemsView<VisualTreeNodeModel>? _items;

        private UIElement? _lastFocused;
        private UIElement? _lastNonNullFocused;
        private long _lastRebuildTick;
        private bool _pickArmed;
        private Button? _pickButton;

        public DebugVisualTreeWindow(Window target)
        {
            _target = target;

            Title = "VisualTree";
            WindowSize = WindowSize.Resizable(520, 720);

            _selectedLabel = new Label { Text = "Selected: (none)" };
            _modeLabel = new Label { Text = "Mode: Follow/Peek" };

            _followFocus = new CheckBox { Text = "Follow Focus", IsChecked = true };
            _autoExpandFocus = new CheckBox { Text = "Auto Expand Focus", IsChecked = true };
            _followFocus.CheckedChanged += _ => UpdateFollowUi();

            _tree = new TreeView()
                .ItemHeight(20)
                .ExpandTrigger(TreeViewExpandTrigger.ClickNode);

            _tree.ItemTemplate<VisualTreeNodeModel>(
                build: ctx => new Label().CenterVertical().Padding(8, 0),
                bind: (view, item, _, ctx) =>
                {
                    ((Label)view).Text(item.DisplayText).WithTheme((t, c) => c.FontWeight(item.Element is FrameworkElement fe && fe.Focusable ? FontWeight.SemiBold: FontWeight.Normal));
                });

            var refreshBtn = new Button { Content = "Refresh" };
            refreshBtn.Click += Refresh;

            _goFocusButton = new Button { Content = "Go Focus" };
            _goFocusButton.Click += () => PeekElement(_lastNonNullFocused ?? _target.FocusManager.FocusedElement);

            var pickBtn = new Button { Content = "Pick (Click)" };
            _pickButton = pickBtn;
            pickBtn.Click += TogglePick;

            var clearBtn = new Button { Content = "Clear Selection" };
            clearBtn.Click += () =>
            {
                if (_target._debugInspectorOverlay != null)
                {
                    _target._debugInspectorOverlay.HighlightedElement = null;
                    _target.RequestRender();
                }

                if (_items != null)
                {
                    _items.SelectedIndex = -1;
                }
                _selectedLabel.Text = "Selected: (none)";
            };


            Content = new DockPanel()
                .Spacing(8)
                .Children(
                    new StackPanel()
                        .DockTop()
                        .Horizontal()
                        .Spacing(8)
                        .Children(refreshBtn, _goFocusButton, pickBtn, clearBtn),
                    new StackPanel()
                        .DockTop()
                        .Horizontal()
                        .Spacing(12)
                        .Children(_followFocus, _autoExpandFocus),
                    new Border()
                        .DockTop()
                        .Padding(8, 4)
                        .Child(_modeLabel),
                    new Border()
                        .DockTop()
                        .Padding(8)
                        .Child(_selectedLabel),
                    _tree
                );

            PreviewKeyDown += e =>
            {
                if (e.Key == Key.F5)
                {
                    Refresh();
                    e.Handled = true;
                }
            };

            _target.FrameRendered += OnTargetFrameRendered;
            Closed += () => _target.FrameRendered -= OnTargetFrameRendered;

            UpdateFollowUi();
            Refresh();
        }

        private void UpdateFollowUi()
        {
            if (_goFocusButton != null)
            {
                _goFocusButton.IsEnabled = _followFocus.IsChecked != true;
            }
        }

        private void TogglePick()
        {
            _pickArmed = !_pickArmed;
            UpdatePickUi();
        }

        private void UpdatePickUi()
        {
            if (_pickButton != null)
            {
                _pickButton.Content = _pickArmed ? "Pick: ARMED (click target)" : "Pick (Click)";
            }

            _modeLabel.Text = _pickArmed ? "Mode: Pick (click in target window to select)" : "Mode: Follow/Peek";
        }

        public void OnTargetMouseDown(Point positionInWindow, MouseButton button, UIElement? element)
        {
            if (!_pickArmed || button != MouseButton.Left)
            {
                return;
            }

            _pickArmed = false;
            UpdatePickUi();

            if (element == null)
            {
                if (_target._debugInspectorOverlay != null)
                {
                    _target._debugInspectorOverlay.HighlightedElement = null;
                    _target.RequestRender();
                }
                if (_items != null)
                {
                    _items.SelectedIndex = -1;
                }
                _selectedLabel.Text = "Selected: (none)";
                return;
            }

            // Keep the UI responsive: tree might be slightly stale, so rebuild once if needed.
            if (!_parentByKey.ContainsKey(element))
            {
                Refresh(preserveExpansion: true, preserveSelection: true);
            }

            SelectAndReveal(element);
        }

        private void OnTargetFrameRendered()
        {
            // Throttle rebuilds: a full tree walk is expensive, even for debug tools.
            // Still keep selection syncing responsive.
            long now = Environment.TickCount64;
            bool rebuild = now - _lastRebuildTick >= 250;

            var focused = _target.FocusManager.FocusedElement;
            bool focusChanged = !ReferenceEquals(_lastFocused, focused);
            _lastFocused = focused;
            if (focused != null)
            {
                _lastNonNullFocused = focused;
            }

            if (rebuild)
            {
                _lastRebuildTick = now;
                Refresh(preserveExpansion: true, preserveSelection: true);
            }

            if (_followFocus.IsChecked == true && focusChanged)
            {
                SelectAndReveal(focused);
            }
            else if (_autoExpandFocus.IsChecked == true && focusChanged)
            {
                ExpandToElement(focused);
            }
        }

        private void Refresh()
        {
            Refresh(preserveExpansion: false, preserveSelection: false);
        }

        private void Refresh(bool preserveExpansion, bool preserveSelection)
        {
            var expandedKeys = preserveExpansion ? CaptureExpandedKeys() : null;
            var selectedKey = preserveSelection ? _items?.SelectedItem?.Key : null;

            var roots = BuildRoots();

            if (_items != null)
            {
                _items.SelectionChanged -= OnItemsSelectionChanged;
            }

            _items = TreeItemsView.Create(
                roots,
                childrenSelector: n => n.Children,
                textSelector: n => n.DisplayText,
                keySelector: n => n.Key);
            _items.SelectionChanged += OnItemsSelectionChanged;

            _tree.ItemsSource = _items;

            if (expandedKeys != null)
            {
                RestoreExpandedKeys(expandedKeys);
            }

            if (selectedKey != null)
            {
                ExpandAncestors(selectedKey);
                SelectByKey(selectedKey);
                _tree.ScrollIntoViewSelected();
            }

            if (!preserveExpansion && roots.Count > 0)
            {
                ExpandByKey(roots[0].Key);
            }
        }

        private IReadOnlyList<VisualTreeNodeModel> BuildRoots()
        {
            _parentByKey.Clear();

            var roots = new List<VisualTreeNodeModel>(4);

            if (_target.Content is Element content)
            {
                var contentRoot = new VisualTreeNodeModel(key: "root:content", text: "Content", element: content, children: new[] { BuildModel(content, parentKey: "root:content") });
                roots.Add(contentRoot);
            }
            else
            {
                roots.Add(new VisualTreeNodeModel(key: "root:content", text: "Content (null)", element: null, children: Array.Empty<VisualTreeNodeModel>()));
            }

            if (_target._popupManager.Count > 0)
            {
                var popupModels = new List<VisualTreeNodeModel>(_target._popupManager.Count);
                for (int i = 0; i < _target._popupManager.Count; i++)
                {
                    popupModels.Add(BuildModel(_target._popupManager.ElementAt(i), parentKey: "root:popups"));
                }

                roots.Add(new VisualTreeNodeModel(key: "root:popups", text: "Popups", element: null, children: popupModels));
            }

            if (_target._adorners.Count > 0)
            {
                var adornerModels = new List<VisualTreeNodeModel>(_target._adorners.Count);
                for (int i = 0; i < _target._adorners.Count; i++)
                {
                    adornerModels.Add(BuildModel(_target._adorners[i].Element, parentKey: "root:adorners"));
                }

                roots.Add(new VisualTreeNodeModel(key: "root:adorners", text: "Adorners", element: null, children: adornerModels));
            }

            return roots;
        }

        private VisualTreeNodeModel BuildModel(Element element, object parentKey)
        {
            var children = new List<VisualTreeNodeModel>();
            if (element is IVisualTreeHost host)
            {
                host.VisitChildren(child => children.Add(BuildModel(child, parentKey: element)));
            }

            string text = element.GetType().Name;

            _parentByKey[element] = parentKey;
            return new VisualTreeNodeModel(key: element, text: text, element: element, children: children);
        }

        private void ExpandAncestors(object key)
        {
            // Expand must happen from root -> leaf; otherwise keys for collapsed descendants won't be visible yet.
            var chain = new List<object>(8);
            for (object? current = key; current != null; current = _parentByKey.GetValueOrDefault(current))
            {
                chain.Add(current);
            }

            for (int i = chain.Count - 1; i >= 0; i--)
            {
                ExpandByKey(chain[i]);
            }
        }

        private sealed class VisualTreeNodeModel
        {
            public object Key { get; }
            public string Text { get; }
            public Element? Element { get; }
            public IReadOnlyList<VisualTreeNodeModel> Children { get; }
            public int DescendantCount { get; }

            public string DisplayText => DescendantCount > 0 ? $"{Text} ({DescendantCount})" : Text;

            public VisualTreeNodeModel(object key, string text, Element? element, IReadOnlyList<VisualTreeNodeModel> children)
            {
                Key = key;
                Text = text ?? string.Empty;
                Element = element;
                Children = children ?? Array.Empty<VisualTreeNodeModel>();
                DescendantCount = CountDescendants(Children);
            }

            private static int CountDescendants(IReadOnlyList<VisualTreeNodeModel> children)
            {
                int count = 0;
                for (int i = 0; i < children.Count; i++)
                {
                    var child = children[i];
                    count += 1 + child.DescendantCount;
                }

                return count;
            }
        }

        private UIElement? GetHoveredElement()
        {
            var pos = _target.LastMousePositionDip;
            var hovered = _target.HitTest(pos);
            if (hovered is Adorner)
            {
                return null;
            }
            return hovered;
        }

        private void PeekElement(UIElement? element)
        {
            if (element == null)
            {
                return;
            }

            // Make sure the node exists in the latest tree.
            Refresh(preserveExpansion: true, preserveSelection: true);
            SelectAndReveal(element);
        }

        private void SelectAndReveal(UIElement? element)
        {
            if (element == null)
            {
                return;
            }

            ExpandAncestors(element);
            SelectByKey(element);
            _tree.ScrollIntoViewSelected();
        }

        private void ExpandToElement(UIElement? element)
        {
            if (element == null)
            {
                return;
            }

            ExpandAncestors(element);
        }

        private void OnItemsSelectionChanged(int index)
        {
            var element = _items?.SelectedItem?.Element as UIElement;

            if (_target._debugInspectorOverlay != null)
            {
                _target._debugInspectorOverlay.HighlightedElement = element;
                _target.RequestRender();
            }

            _selectedLabel.Text = element == null
                ? "Selected: (none)"
                : $"Selected: {element.GetType().Name} {FormatRect(GetElementRectInWindow(element))}";

            _tree.ScrollIntoView(index);
            _tree.InvalidateVisual();
        }

        private List<(object Key, int Depth)> CaptureExpandedKeys()
        {
            var items = _items;
            if (items == null)
            {
                return new List<(object, int)>();
            }

            var result = new List<(object, int)>();
            for (int i = 0; i < items.Count; i++)
            {
                if (!items.GetIsExpanded(i))
                {
                    continue;
                }

                if (items.GetItem(i) is not VisualTreeNodeModel model)
                {
                    continue;
                }

                result.Add((model.Key, items.GetDepth(i)));
            }

            return result;
        }

        private void RestoreExpandedKeys(List<(object Key, int Depth)> expanded)
        {
            if (_items == null || expanded.Count == 0)
            {
                return;
            }

            expanded.Sort(static (a, b) => a.Depth.CompareTo(b.Depth));
            for (int i = 0; i < expanded.Count; i++)
            {
                ExpandByKey(expanded[i].Key);
            }
        }

        private void ExpandByKey(object key)
        {
            var items = _items;
            if (items == null)
            {
                return;
            }

            int index = FindVisibleIndexByKey(items, key);
            if (index < 0 || !items.GetHasChildren(index))
            {
                return;
            }

            items.SetIsExpanded(index, true);
        }

        private void SelectByKey(object key)
        {
            var items = _items;
            if (items == null)
            {
                return;
            }

            int index = FindVisibleIndexByKey(items, key);
            items.SelectedIndex = index;
        }

        private static int FindVisibleIndexByKey(ITreeItemsView items, object key)
        {
            var keySelector = items.KeySelector;
            for (int i = 0; i < items.Count; i++)
            {
                var item = items.GetItem(i);
                if (item == null)
                {
                    continue;
                }

                if (keySelector != null)
                {
                    if (Equals(keySelector(item), key))
                    {
                        return i;
                    }
                }
                else
                {
                    if (ReferenceEquals(item, key) || Equals(item, key))
                    {
                        return i;
                    }
                }
            }

            return -1;
        }

        private Rect GetElementRectInWindow(UIElement element)
        {
            var size = element.RenderSize;
            var local = new Rect(0, 0, size.Width, size.Height);

            return element.TranslateRect(local, _target);
        }

        private static string FormatRect(Rect r)
            => $"[{r.X:0.#},{r.Y:0.#} {r.Width:0.#}x{r.Height:0.#}]";
    }
}
#endif
