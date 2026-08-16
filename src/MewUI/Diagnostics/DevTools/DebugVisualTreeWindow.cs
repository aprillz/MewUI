using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.Diagnostics;

internal sealed class DebugVisualTreeWindow : Window
{
    private readonly Window _target;
    private readonly TreeView _tree;
    private readonly TextBlock _selectedLabel;
    private readonly TextBlock _modeLabel;
    private readonly CheckBox _followFocus;
    private readonly CheckBox _autoExpandFocus;
    private readonly CheckBox _logicalMode;
    private readonly DebugPropertyPanel _propertyPanel;
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
        ExcludeFromProfiler = true;
        _target = target;

        Title = "Live Visual Tree";
        WindowSize = WindowSize.Resizable(960, 720);

        _selectedLabel = new TextBlock { Text = "Selected: (none)", TextTrimming = TextTrimming.CharacterEllipsis };
        _modeLabel = new TextBlock { Text = "Mode: Follow/Peek" };

        _followFocus = new CheckBox { Content = new TextBlock { Text = "Follow Focus", VerticalTextAlignment = TextAlignment.Center }, IsChecked = true };
        _autoExpandFocus = new CheckBox { Content = new TextBlock { Text = "Auto Expand Focus", VerticalTextAlignment = TextAlignment.Center }, IsChecked = true };
        _logicalMode = new CheckBox { Content = new TextBlock { Text = "Logical Tree", VerticalTextAlignment = TextAlignment.Center }, IsChecked = false };
        _followFocus.CheckedChanged += _ => UpdateFollowUi();
        _logicalMode.CheckedChanged += _ =>
        {
            Title = _logicalMode.IsChecked == true ? "Live Logical Tree" : "Live Visual Tree";
            Refresh(preserveExpansion: false, preserveSelection: true);
        };

        _tree = new TreeView()
            .ItemHeight(24)
            .ExpandTrigger(TreeViewExpandTrigger.ClickNode);

        _tree.ItemTemplate<VisualTreeNodeModel>(
            build: ctx => new TextBlock().CenterVertical(),
            bind: (view, item, _, ctx) =>
            {
                ((TextBlock)view).Text(item.DisplayText).WithTheme((t, c) => c.FontWeight(item.Element is FrameworkElement fe && fe.Focusable ? FontWeight.SemiBold : FontWeight.Normal));
            });

        var refreshBtn = new Button().Content("Refresh");
        refreshBtn.Click += Refresh;

        _goFocusButton = new Button().Content("Go Focus");
        _goFocusButton.Click += () => PeekElement(_lastNonNullFocused ?? _target.FocusManager.FocusedElement);

        var pickBtn = new Button().Content("Pick (Click)");
        _pickButton = pickBtn;
        pickBtn.Click += TogglePick;

        _propertyPanel = new DebugPropertyPanel();

        var clearBtn = new Button().Content("Clear Selection");
        clearBtn.Click += () =>
        {
            if (_target.DebugInspectorOverlay != null)
            {
                _target.DebugInspectorOverlay.HighlightedElement = null;
                _target.RequestRender();
            }

            if (_items != null)
            {
                _items.SelectedIndex = -1;
            }
            _selectedLabel.Text = "Selected: (none)";
            _propertyPanel.SetTarget(null);
        };

        Content = new DockPanel()
            .Spacing(8)
            .Children(
                new StackPanel()
                    .DockTop()
                    .Horizontal()
                    .Spacing(8)
                    .Children(refreshBtn, _goFocusButton, pickBtn, clearBtn),
                new DockPanel()
                    .DockTop()
                    .Spacing(12)
                    .Children(
                        _modeLabel.DockRight().CenterVertical(),
                        new StackPanel()
                            .Horizontal()
                            .Spacing(12)
                            .Children(_followFocus, _autoExpandFocus, _logicalMode)),
                new Border()
                    .DockTop()
                    .Padding(8, 2)
                    .Child(_selectedLabel),
                new SplitPanel()
                    .Horizontal()
                    .FirstLength(GridLength.Stars(1))
                    .SecondLength(GridLength.Stars(1))
                    .MinFirst(240)
                    .MinSecond(280)
                    .First(_tree)
                    .Second(_propertyPanel)
            );

        PreviewKeyDown += e =>
        {
            if (e.Key == Key.F5)
            {
                Refresh();
                _propertyPanel.Rebuild();
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
            _pickButton.Content(_pickArmed ? "Pick: ARMED (click target)" : "Pick (Click)");
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
            if (_target.DebugInspectorOverlay != null)
            {
                _target.DebugInspectorOverlay.HighlightedElement = null;
                _target.RequestRender();
            }
            if (_items != null)
            {
                _items.SelectedIndex = -1;
            }
            _selectedLabel.Text = "Selected: (none)";
            _propertyPanel.SetTarget(null);
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
            _propertyPanel.RefreshValues();
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
        bool logical = _logicalMode.IsChecked == true;

        // Logical mode starts from the user content; visual mode from the effective root
        // (the template root when the window is templated).
        var rootElement = logical ? _target.Content : _target.EffectiveVisualRoot;
        if (rootElement != null)
        {
            var contentRoot = new VisualTreeNodeModel(key: "root:content", text: "Content", element: rootElement, children: [BuildModel(rootElement, parentKey: "root:content", logical)]);
            roots.Add(contentRoot);
        }
        else
        {
            roots.Add(new VisualTreeNodeModel(key: "root:content", text: "Content (null)", element: null, children: Array.Empty<VisualTreeNodeModel>()));
        }

        // Popups/adorners are window-level visual layers; the logical tree shows user ownership only.
        if (logical)
        {
            return roots;
        }

        if (_target.DebugPopupCount > 0)
        {
            var popupModels = new List<VisualTreeNodeModel>(_target.DebugPopupCount);
            for (int i = 0; i < _target.DebugPopupCount; i++)
            {
                popupModels.Add(BuildModel(_target.DebugPopupAt(i), parentKey: "root:popups", logical: false));
            }

            roots.Add(new VisualTreeNodeModel(key: "root:popups", text: "Popups", element: null, children: popupModels));
        }

        if (_target.DebugAdornerCount > 0)
        {
            var adornerModels = new List<VisualTreeNodeModel>(_target.DebugAdornerCount);
            for (int i = 0; i < _target.DebugAdornerCount; i++)
            {
                adornerModels.Add(BuildModel(_target.DebugAdornerElementAt(i), parentKey: "root:adorners", logical: false));
            }

            roots.Add(new VisualTreeNodeModel(key: "root:adorners", text: "Adorners", element: null, children: adornerModels));
        }

        return roots;
    }

    private VisualTreeNodeModel BuildModel(Element element, object parentKey, bool logical)
    {
        var children = new List<VisualTreeNodeModel>();
        if (logical)
        {
            if (element is ILogicalTreeHost logicalHost)
            {
                logicalHost.VisitLogicalChildren(child =>
                {
                    children.Add(BuildModel(child, parentKey: element, logical));
                    return true;
                });
            }
        }
        else if (element is IVisualTreeHost host)
        {
            host.VisitChildren(child =>
            {
                children.Add(BuildModel(child, parentKey: element, logical));
                return true;
            });
        }

        // Visual mode marks elements without a logical owner as [Type]: template parts,
        // presenters, and other machinery stand out from user-owned structure.
        string text = !logical && element.LogicalParent == null
            ? $"[{element.GetType().Name}]"
            : element.GetType().Name;

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
        _propertyPanel.SetTarget(element);

        if (_target.DebugInspectorOverlay != null)
        {
            _target.DebugInspectorOverlay.HighlightedElement = element;
            _target.RequestRender();
        }

        if (element == null)
        {
            _selectedLabel.Text = "Selected: (none)";
        }
        else
        {
            var logicalRoot = element.FindLogicalRoot();
            _selectedLabel.Text =
                $"Selected: {element.GetType().Name} {FormatRect(GetElementRectInWindow(element))}   " +
                $"visual parent={element.Parent?.GetType().Name ?? "(none)"} root={element.FindVisualRoot()?.GetType().Name ?? "(none)"}   " +
                $"logical parent={element.LogicalParent?.GetType().Name ?? "(none)"} root={(ReferenceEquals(logicalRoot, element) ? "(self)" : logicalRoot.GetType().Name)}";
        }

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
