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
    }

    private void ToggleDebugVisualTree()
    {
        if (_debugVisualTreeWindow != null)
        {
            try { _debugVisualTreeWindow.Close(); } catch { }
            _debugVisualTreeWindow = null;
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
        }

        treeWindow.Show();
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

        private readonly Dictionary<UIElement, TreeViewNode> _nodeByElement = new();
        private readonly Dictionary<TreeViewNode, TreeViewNode?> _parentByNode = new();

        private UIElement? _lastFocused;
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
                .ExpandTrigger(TreeViewExpandTrigger.DoubleClickNode);

            _tree.ItemTemplate<TreeViewNode>(
                build: ctx => new Label().CenterVertical().Padding(8, 0),
                bind: (view, item, _, ctx) =>
                {
                    ((Label)view).Text(item.Text);
                });

            _tree.OnSelectionChanged(obj =>
            {
            var node = obj as TreeViewNode;
            var element = node?.Tag as UIElement;

            if (_target._debugInspectorOverlay != null)
            {
                _target._debugInspectorOverlay.HighlightedElement = element;
                _target.RequestRender();
            }

                _selectedLabel.Text = element == null
                    ? "Selected: (none)"
                    : $"Selected: {element.GetType().Name} {FormatRect(GetElementRectInWindow(element))}";
            });

            var refreshBtn = new Button { Content = "Refresh" };
            refreshBtn.Click += Refresh;

            _goFocusButton = new Button { Content = "Go Focus" };
            _goFocusButton.Click += () => PeekElement(_target.FocusManager.FocusedElement);

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

                _tree.SelectedNode = null;
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
                _tree.SelectedNode = null;
                _selectedLabel.Text = "Selected: (none)";
                return;
            }

            // Keep the UI responsive: tree might be slightly stale, so rebuild once if needed.
            if (!_nodeByElement.ContainsKey(element))
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
            var expandedElements = preserveExpansion ? CaptureExpandedElements() : null;
            var selectedElement = preserveSelection ? _tree.SelectedNode?.Tag as UIElement : null;

            var roots = BuildRoots();

            _tree.ItemsSource = ItemsView.Create(roots, n => n.Text);

            if (expandedElements != null)
            {
                RestoreExpandedElements(expandedElements);
            }

            if (selectedElement != null)
            {
                SelectAndReveal(selectedElement);
            }

            if (roots.Count > 0)
            {
                _tree.Expand(roots[0]);
            }
        }

        private IReadOnlyList<TreeViewNode> BuildRoots()
        {
            _nodeByElement.Clear();
            _parentByNode.Clear();

            var roots = new List<TreeViewNode>(4);

            if (_target.Content is Element content)
            {
                var contentNode = new TreeViewNode("Content", new[] { BuildNode(content) }, tag: content);
                RegisterParentChain(contentNode, parent: null);
                roots.Add(contentNode);
            }
            else
            {
                roots.Add(new TreeViewNode("Content (null)"));
            }

            if (_target._popups.Count > 0)
            {
                var popupNodes = new List<TreeViewNode>(_target._popups.Count);
                for (int i = 0; i < _target._popups.Count; i++)
                {
                    popupNodes.Add(BuildNode(_target._popups[i].Element));
                }

                var popupsRoot = new TreeViewNode("Popups", popupNodes);
                RegisterParentChain(popupsRoot, parent: null);
                roots.Add(popupsRoot);
            }

            if (_target._adorners.Count > 0)
            {
                var adornerNodes = new List<TreeViewNode>(_target._adorners.Count);
                for (int i = 0; i < _target._adorners.Count; i++)
                {
                    adornerNodes.Add(BuildNode(_target._adorners[i].Element));
                }

                var adornersRoot = new TreeViewNode("Adorners", adornerNodes);
                RegisterParentChain(adornersRoot, parent: null);
                roots.Add(adornersRoot);
            }

            return roots;
        }

        private TreeViewNode BuildNode(Element element)
        {
            var children = new List<TreeViewNode>();
            if (element is IVisualTreeHost host)
            {
                host.VisitChildren(child => children.Add(BuildNode(child)));
            }

            string text;
            if (element is UIElement u)
            {
                text = $"{u.GetType().Name} {FormatRect(GetElementRectInWindow(u))}";
            }
            else
            {
                text = element.GetType().Name;
            }

            var node = new TreeViewNode(text, children, tag: element);
            RegisterParentChain(node, parent: null);

            if (element is UIElement ue)
            {
                _nodeByElement[ue] = node;
            }

            return node;
        }

        private void RegisterParentChain(TreeViewNode node, TreeViewNode? parent)
        {
            _parentByNode[node] = parent;
            for (int i = 0; i < node.Children.Count; i++)
            {
                RegisterParentChain(node.Children[i], node);
            }
        }

        private HashSet<UIElement> CaptureExpandedElements()
        {
            var set = new HashSet<UIElement>();
            foreach (var kv in _nodeByElement)
            {
                if (_tree.IsExpanded(kv.Value))
                {
                    set.Add(kv.Key);
                }
            }
            return set;
        }

        private void RestoreExpandedElements(HashSet<UIElement> expandedElements)
        {
            foreach (var element in expandedElements)
            {
                if (_nodeByElement.TryGetValue(element, out var node))
                {
                    _tree.Expand(node);
                    ExpandAncestors(node);
                }
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

            if (!_nodeByElement.TryGetValue(element, out var node))
            {
                return;
            }

            ExpandAncestors(node);
            _tree.SelectedNode = node;
        }

        private void ExpandToElement(UIElement? element)
        {
            if (element == null)
            {
                return;
            }

            if (!_nodeByElement.TryGetValue(element, out var node))
            {
                return;
            }

            ExpandAncestors(node);
        }

        private void ExpandAncestors(TreeViewNode node)
        {
            for (TreeViewNode? current = node; current != null; current = _parentByNode.GetValueOrDefault(current))
            {
                _tree.Expand(current);
            }
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
