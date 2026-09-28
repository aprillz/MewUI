using System.Collections.ObjectModel;
using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Rendering.Gdi;
using MewUI.Test.Infrastructure;

namespace MewUI.Test.Controls;

/// <summary>
/// A Reset tells a list that anything may have changed, and the list binds every visible row again. The
/// row that showed an item still shows it afterwards, wherever the item now sits, so text that did not
/// change is not drawn again, and focus stays with the item it was on.
/// Not parallelizable: assigns the process-wide Application.DefaultGraphicsFactory.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class ItemsResetReuseTests
{
    private const int WIDTH = 520;
    private const int HEIGHT = 400;
    private const int ROWS = 40;

    // An edge the repaint pads for antialiasing, in DIPs.
    private const double EDGE_SLACK = 2;

    // How far text is taken to ink past the box it was measured in, for a line this tall.
    private const double TEXT_INK_SLACK = 6;

    [TestMethod]
    public void AGridViewResetWithTheSameValues_DrawsNothingAgain()
    {
        using var scene = new GridScene();
        var before = scene.CellVersions();

        scene.ResetWithTheSameNodes();

        var after = scene.CellVersions();
        var redrawn = before.Where(pair => after.TryGetValue(pair.Key, out int version) && version != pair.Value).Select(pair => pair.Key.Text).ToList();
        Assert.IsEmpty(redrawn, $"{redrawn.Count} unchanged cells were drawn again, e.g. {string.Join(", ", redrawn.Take(5))}");
        Assert.IsTrue(
            scene.Window.LastRetainedDirtyRect is Rect area && (area.Width <= 0 || area.Height <= 0),
            $"nothing changed and the frame repainted {scene.Window.LastRetainedDirtyRect?.ToString() ?? "everything"}");
    }

    [TestMethod]
    public void AGridViewResetWithOneValueChanged_RepaintsThatCell()
    {
        using var scene = new GridScene();
        scene.Nodes[3].Cpu = 55;

        scene.ResetWithTheSameNodes();

        var cell = scene.CellsShowing("55.0%").Single();
        var bounds = cell.Bounds.Inflate(TEXT_INK_SLACK, TEXT_INK_SLACK);
        var dirty = scene.Window.LastRetainedDirtyRect;
        Assert.IsTrue(
            dirty is Rect area && area.Width > 0 && area.Y >= bounds.Y - EDGE_SLACK && area.Bottom <= bounds.Bottom + EDGE_SLACK &&
                area.X >= bounds.X - EDGE_SLACK && area.Right <= bounds.Right + EDGE_SLACK,
            $"one cell at {bounds} changed and the frame repainted {dirty?.ToString() ?? "everything"}");
    }

    [TestMethod]
    public void AGridViewResetThatReordersTheItems_KeepsEachItemsRow()
    {
        using var scene = new GridScene();
        var rowsBefore = scene.RowsByName();

        scene.ResetWithNeighborsSwapped();

        var rowsAfter = scene.RowsByName();
        var moved = rowsAfter.Where(pair => rowsBefore.TryGetValue(pair.Key, out var row) && !ReferenceEquals(row, pair.Value)).Select(pair => pair.Key).ToList();
        Assert.IsNotEmpty(rowsAfter, "no rows were realized after the Reset");
        Assert.IsEmpty(moved, $"{moved.Count} items visible before and after the Reset got another row, e.g. {string.Join(", ", moved.Take(5))}");
    }

    [TestMethod]
    public void AListBoxResetThatMovesTheFocusedItem_KeepsFocusOnIt()
    {
        using var scene = new ListScene();
        var editor = scene.EditorShowing("item 02");
        scene.Window.FocusManager.SetFocus(editor);

        scene.Reset(["item 00", "item 01", "item 03", "item 04", "item 02", "item 05"]);

        var focused = scene.Window.FocusManager.FocusedElement;
        Assert.IsTrue(
            focused is TextBox box && box.Text == "item 02",
            $"focus was on the editor of item 02 and is now on {Describe(focused)}");
    }

    [TestMethod]
    public void AListBoxResetThatRemovesTheFocusedItem_FocusesNoOtherItem()
    {
        using var scene = new ListScene();
        scene.Window.FocusManager.SetFocus(scene.EditorShowing("item 02"));

        scene.Reset(["item 00", "item 01", "item 03", "item 04", "item 05"]);

        // Focus leaves with the item; it does not land on whatever row took the item's place.
        var focused = scene.Window.FocusManager.FocusedElement;
        Assert.IsFalse(
            focused is TextBox,
            $"the focused item left the list and focus went to {Describe(focused)}");
    }

    [TestMethod]
    public void AListBoxResetWithTheSameItems_DrawsNothingAgain()
    {
        using var scene = new ListScene(editors: false);
        var before = VersionsOf(scene.List);

        scene.Reset(["item 00", "item 01", "item 02", "item 03", "item 04", "item 05"]);

        AssertNoneRedrawn(before, VersionsOf(scene.List));
    }

    [TestMethod]
    public void ATreeViewResetWithTheSameNodes_DrawsNothingAgain()
    {
        var previousFactory = Application.DefaultGraphicsFactory;
        using var factory = new GdiGraphicsFactory();
        Application.DefaultGraphicsFactory = factory;
        try
        {
            var roots = new ObservableCollection<Node>();
            for (int index = 0; index < ROWS; index++)
            {
                roots.Add(new Node("node " + index.ToString("00"), index));
            }

            var tree = new TreeView { ItemsSource = TreeItemsView.Create<Node>(roots, node => node.Children, textSelector: node => node.Name) };
            var window = HeadlessWindow.Create(WIDTH, HEIGHT);
            window.Content = tree;
            using var surface = factory.CreateSurface(RenderSurfaceDescriptor.Offscreen(WIDTH, HEIGHT, 1.0, hasAlpha: false));
            Render(window, surface);
            Render(window, surface);
            var before = VersionsOf(tree);

            var copy = roots.ToArray();
            roots.Clear();
            foreach (var node in copy)
            {
                roots.Add(node);
            }

            Render(window, surface);
            AssertNoneRedrawn(before, VersionsOf(tree));
        }
        finally
        {
            Application.DefaultGraphicsFactory = previousFactory;
        }
    }

    private static void AssertNoneRedrawn(Dictionary<TextBlock, int> before, Dictionary<TextBlock, int> after)
    {
        var redrawn = before.Where(pair => after.TryGetValue(pair.Key, out int version) && version != pair.Value).Select(pair => pair.Key.Text).ToList();
        var gone = before.Keys.Count(block => !after.ContainsKey(block));
        Assert.AreEqual(0, gone, $"{gone} text blocks that showed items were replaced");
        Assert.IsEmpty(redrawn, $"{redrawn.Count} unchanged texts were drawn again, e.g. {string.Join(", ", redrawn.Take(5))}");
    }

    private static string Describe(Element? element) => element switch
    {
        null => "nothing",
        TextBox box => $"the editor of {box.Text}",
        _ => element.GetType().Name,
    };

    private static Dictionary<TextBlock, int> VersionsOf(Element root)
    {
        var versions = new Dictionary<TextBlock, int>(ReferenceEqualityComparer.Instance);
        Visit(root, element =>
        {
            if (element is TextBlock block && !string.IsNullOrEmpty(block.Text))
            {
                versions[block] = block.RenderContentVersion;
            }
        });
        return versions;
    }

    private static void Visit(Element element, Action<Element> visitor)
    {
        visitor(element);
        if (element is IVisualTreeHost host)
        {
            host.VisitChildren(child =>
            {
                Visit(child, visitor);
                return true;
            });
        }
    }

    private static void Render(Window window, IRenderSurface surface)
    {
        window.PerformLayout();
        window.RenderFrameToSurface(surface);
    }

    private sealed class Node(string name, double cpu)
    {
        public string Name { get; } = name;
        public double Cpu { get; set; } = cpu;
        public ObservableCollection<Node> Children { get; } = [];
    }

    /// <summary>A grid over a tree of nodes, refreshed the way a monitor refreshes it: cleared, filled again, invalidated.</summary>
    private sealed class GridScene : IDisposable
    {
        private readonly IGraphicsFactory _previousFactory = Application.DefaultGraphicsFactory;
        private readonly GdiGraphicsFactory _factory = new();
        private readonly IRenderSurface _surface;
        private readonly ObservableCollection<Node> _roots = [];
        private readonly TreeItemsView<Node> _tree;
        private readonly GridView _grid;

        public GridScene()
        {
            Application.DefaultGraphicsFactory = _factory;
            for (int index = 0; index < ROWS; index++)
            {
                var node = new Node("process " + index.ToString("00"), index % 7);
                Nodes.Add(node);
                _roots.Add(node);
            }

            _tree = TreeItemsView.Create(_roots, node => node.Children, node => node.Name, node => node.Name);
            _grid = new GridView { ItemsSource = _tree, ZebraStriping = false, ShowGridLines = false };
            _grid.Columns(
                new GridViewColumn<Node>().Header("Name").Width(200).Bind(
                    _ => new TextBlock(), (view, node) => view.Text(node.Name)),
                new GridViewColumn<Node>().Header("CPU").Width(90).Bind(
                    _ => new TextBlock { TextAlignment = TextAlignment.Right }, (view, node) => view.Text($"{node.Cpu:0.0}%")));

            Window = HeadlessWindow.Create(WIDTH, HEIGHT);
            Window.Content = _grid;
            _surface = _factory.CreateSurface(RenderSurfaceDescriptor.Offscreen(WIDTH, HEIGHT, 1.0, hasAlpha: false));
            Render(Window, _surface);
            Render(Window, _surface);
        }

        public Window Window { get; }

        public List<Node> Nodes { get; } = [];

        public void ResetWithTheSameNodes() => ResetWith(Nodes);

        // Every item moves by one place and the visible ones stay visible.
        public void ResetWithNeighborsSwapped() => ResetWith(Nodes.Select((_, index) => Nodes[index ^ 1]).ToList());

        private void ResetWith(IReadOnlyList<Node> nodes)
        {
            _roots.Clear();
            foreach (var node in nodes)
            {
                _roots.Add(node);
            }

            _tree.Invalidate();
            Render(Window, _surface);
        }

        public Dictionary<TextBlock, int> CellVersions() => VersionsOf(_grid);

        public List<TextBlock> CellsShowing(string text) => CellVersions().Keys.Where(block => block.Text == text).ToList();

        public Dictionary<string, GridViewRow> RowsByName()
        {
            var rows = new Dictionary<string, GridViewRow>();
            Visit(_grid, element =>
            {
                if (element is GridViewRow row)
                {
                    string? name = null;
                    Visit(row, inner =>
                    {
                        if (name == null && inner is TextBlock block && block.Text.StartsWith("process ", StringComparison.Ordinal))
                        {
                            name = block.Text;
                        }
                    });
                    if (name != null)
                    {
                        rows[name] = row;
                    }
                }
            });
            return rows;
        }

        public void Dispose()
        {
            Application.DefaultGraphicsFactory = _previousFactory;
            _surface.Dispose();
            _factory.Dispose();
        }
    }

    /// <summary>A list of strings, each row an editor showing its item, or plain text.</summary>
    private sealed class ListScene : IDisposable
    {
        private readonly IGraphicsFactory _previousFactory = Application.DefaultGraphicsFactory;
        private readonly GdiGraphicsFactory _factory = new();
        private readonly IRenderSurface _surface;
        private readonly ObservableCollection<string> _items = ["item 00", "item 01", "item 02", "item 03", "item 04", "item 05"];

        public ListScene(bool editors = true)
        {
            Application.DefaultGraphicsFactory = _factory;
            List = new ListBox
            {
                ItemsSource = ItemsView.Create(_items),
                ItemTemplate = new DelegateTemplate<object?>(
                    build: _ => editors ? new TextBox() : (FrameworkElement)new TextBlock(),
                    bind: (view, item, _, _) =>
                    {
                        if (view is TextBox box)
                        {
                            box.Text = (string)item!;
                        }
                        else
                        {
                            ((TextBlock)view).Text = (string)item!;
                        }
                    }),
            };
            Window = HeadlessWindow.Create(WIDTH, HEIGHT);
            Window.Content = List;
            _surface = _factory.CreateSurface(RenderSurfaceDescriptor.Offscreen(WIDTH, HEIGHT, 1.0, hasAlpha: false));
            Render(Window, _surface);
            Render(Window, _surface);
        }

        public Window Window { get; }

        public ListBox List { get; }

        public TextBox EditorShowing(string item)
        {
            TextBox? found = null;
            Visit(List, element =>
            {
                if (found == null && element is TextBox box && box.Text == item)
                {
                    found = box;
                }
            });
            return found ?? throw new InvalidOperationException($"no editor shows {item}");
        }

        public void Reset(IReadOnlyList<string> items)
        {
            _items.Clear();
            foreach (var item in items)
            {
                _items.Add(item);
            }

            Render(Window, _surface);
        }

        public void Dispose()
        {
            Application.DefaultGraphicsFactory = _previousFactory;
            _surface.Dispose();
            _factory.Dispose();
        }
    }
}
