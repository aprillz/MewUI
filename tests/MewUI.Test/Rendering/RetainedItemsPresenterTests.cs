using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Rendering.Gdi;
using Aprillz.MewUI.Rendering.Retained;

namespace MewUI.Test.Rendering;

/// <summary>
/// Validates the composition the items presenters declare against the order their own render path
/// draws in: a captured scene and a pure replay must paint the same pixels as a direct render, and
/// each realized item has to be a node of its own so one item can be re-recorded alone.
/// Not parallelizable: assigns the process-wide Application.DefaultGraphicsFactory.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class RetainedItemsPresenterTests
{
    private const int SURFACE_WIDTH = 320;
    private const int SURFACE_HEIGHT = 300;
    private const double FIXED_ITEM_HEIGHT = 24;

    /// <summary>Own-content slots of one item view: the Border's background and its stroke.</summary>
    private const int ITEM_ROOT_SLOT_COUNT = 2;

    [TestMethod]
    public void ListBoxScene_CaptureAndReplay_MatchImmediateRenderPixelForPixel()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        using var factory = new GdiGraphicsFactory();
        Application.DefaultGraphicsFactory = factory;

        var root = BuildListBoxTree();
        Layout(root);

        byte[] immediate = RenderSurface(factory, context => root.Render(context));

        using var scene = new RenderScene();
        byte[] captured = RenderSurface(factory, context => new SceneCapture().Capture(scene, root, context));
        byte[] replayed = RenderSurface(factory, context => FrameRenderer.Replay(scene, context));

        AssertPixelsEqual(immediate, captured, "capture pass");
        AssertPixelsEqual(immediate, replayed, "replay pass");
    }

    [TestMethod]
    public void PresenterScene_CaptureAndReplay_MatchImmediateRenderPixelForPixel()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        using var factory = new GdiGraphicsFactory();
        Application.DefaultGraphicsFactory = factory;

        var root = BuildPresenterTree(out _, out _, out _);
        Layout(root);

        byte[] immediate = RenderSurface(factory, context => root.Render(context));

        using var scene = new RenderScene();
        byte[] captured = RenderSurface(factory, context => new SceneCapture().Capture(scene, root, context));
        byte[] replayed = RenderSurface(factory, context => FrameRenderer.Replay(scene, context));

        AssertPixelsEqual(immediate, captured, "capture pass");
        AssertPixelsEqual(immediate, replayed, "replay pass");
    }

    [TestMethod]
    public void ScrolledPresenterScene_CaptureAndReplay_MatchImmediateRenderPixelForPixel()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        using var factory = new GdiGraphicsFactory();
        Application.DefaultGraphicsFactory = factory;

        var root = BuildPresenterTree(out var fixedHost, out _, out var variableHost);
        Layout(root);

        // Scrolling moves the realized range, so the declaration has to follow it.
        fixedHost.SetScrollOffsets(0, 3 * FIXED_ITEM_HEIGHT + 5);
        variableHost.SetScrollOffsets(0, 37);
        Layout(root);

        byte[] immediate = RenderSurface(factory, context => root.Render(context));

        using var scene = new RenderScene();
        byte[] captured = RenderSurface(factory, context => new SceneCapture().Capture(scene, root, context));
        byte[] replayed = RenderSurface(factory, context => FrameRenderer.Replay(scene, context));

        AssertPixelsEqual(immediate, captured, "capture pass");
        AssertPixelsEqual(immediate, replayed, "replay pass");
    }

    [TestMethod]
    public void EachRealizedItem_BecomesItsOwnSceneNode()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        using var factory = new GdiGraphicsFactory();
        Application.DefaultGraphicsFactory = factory;

        var root = BuildPresenterTree(out _, out _, out _);
        Layout(root);

        using var scene = new RenderScene();
        var capture = new SceneCapture();
        RenderSurface(factory, context => capture.Capture(scene, root, context));

        foreach (var presenter in CollectPresenters(root))
        {
            int realized = 0;
            presenter.VisitRealized(element =>
            {
                realized++;
                Assert.IsNotNull(
                    scene.FindNode((UIElement)element),
                    $"{presenter.GetType().Name} did not declare a realized item, so the scene owns it inside " +
                    "the presenter's own content instead of as a node.");
            });

            Assert.IsGreaterThan(0, realized, $"{presenter.GetType().Name} realized no item to declare.");
        }
    }

    [TestMethod]
    public void ChangingOneItem_ReRecordsThatItemAlone()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        using var factory = new GdiGraphicsFactory();
        Application.DefaultGraphicsFactory = factory;

        var root = BuildPresenterTree(out _, out var fixedPresenter, out _);
        Layout(root);

        using var scene = new RenderScene();
        var capture = new SceneCapture();
        RenderSurface(factory, context => capture.Capture(scene, root, context));

        Assert.AreEqual(0, scene.Statistics.RejectedSlotCount,
            "A rejected slot is re-recorded on every pass, which would mask the per-item behaviour.");

        UIElement? second = null;
        fixedPresenter.VisitRealized((index, element) =>
        {
            if (index == 1)
            {
                second = element;
            }
        });

        Assert.IsNotNull(second, "the second item was not realized");

        scene.Statistics.Reset();
        var dirty = new HashSet<UIElement>(ReferenceEqualityComparer.Instance) { second };
        RenderSurface(factory, context => capture.Capture(scene, root, context, dirty));

        // The item view is a Border, which owns a background slot and a border-stroke slot.
        Assert.AreEqual(ITEM_ROOT_SLOT_COUNT, scene.Statistics.ContentRecordCount,
            $"one item's change re-recorded {scene.Statistics.ContentRecordCount} content slots, " +
            $"not just the {ITEM_ROOT_SLOT_COUNT} the changed item owns.");
        Assert.IsGreaterThan(0, scene.Statistics.ContentReplayCount,
            "the items that did not change were not replayed.");
    }

    /// <summary>Two list boxes: the default fixed-height path and the non-virtualizing stack path.</summary>
    private static Border BuildListBoxTree()
    {
        var fixedList = new ListBox
        {
            Height = 120,
            ItemHeight = FIXED_ITEM_HEIGHT,
            ItemsSource = ItemsView.Create(CreateItems(20)),
            SelectedIndex = 2,
        };

        var stackList = new ListBox
        {
            Height = 100,
            ItemsSource = ItemsView.Create(CreateItems(6)),
            SelectedIndex = 1,
        };
        stackList.SetPresenter(new StackItemsPresenter());

        var stack = new StackPanel { Orientation = Orientation.Vertical };
        stack.Children(fixedList, stackList);

        return new Border
        {
            BorderThickness = 4,
            BorderBrush = Color.FromArgb(255, 30, 30, 30),
            Background = Color.FromArgb(255, 250, 250, 250),
            Padding = new Thickness(6),
            Child = stack,
        };
    }

    /// <summary>
    /// The three declared presenters hosted directly by scroll viewers, so the capture reaches them
    /// through declared composition rather than through a list box's compatibility subtree.
    /// </summary>
    private static Border BuildPresenterTree(
        out ScrollViewer fixedHost,
        out FixedHeightItemsPresenter fixedPresenter,
        out ScrollViewer variableHost)
    {
        fixedPresenter = new FixedHeightItemsPresenter
        {
            ItemHeight = FIXED_ITEM_HEIGHT,
            ItemsSource = ItemsView.Create(CreateItems(30)),
            ItemTemplate = CreateItemTemplate(Color.FromArgb(255, 180, 210, 240)),
        };

        fixedHost = new ScrollViewer
        {
            Width = 200,
            Height = 96,
            Background = Color.FromArgb(255, 245, 245, 245),
            Content = fixedPresenter,
        };

        var stackPresenter = new StackItemsPresenter
        {
            ItemHeightHint = 22,
            ItemsSource = ItemsView.Create(CreateItems(5)),
            ItemTemplate = CreateItemTemplate(Color.FromArgb(255, 240, 210, 180)),
        };

        var stackHost = new ScrollViewer
        {
            Width = 200,
            Height = 80,
            Background = Color.FromArgb(255, 235, 235, 235),
            Content = stackPresenter,
        };

        var variablePresenter = new VariableHeightItemsPresenter
        {
            ItemHeightHint = 26,
            ItemsSource = ItemsView.Create(CreateItems(24)),
            ItemTemplate = CreateItemTemplate(Color.FromArgb(255, 200, 240, 200)),
        };

        var variableHostLocal = new ScrollViewer
        {
            Width = 200,
            Height = 90,
            Background = Color.FromArgb(255, 250, 240, 250),
            Content = variablePresenter,
        };
        variableHost = variableHostLocal;

        var stack = new StackPanel { Orientation = Orientation.Vertical };
        stack.Children(fixedHost, stackHost, variableHostLocal);

        return new Border
        {
            BorderThickness = 3,
            BorderBrush = Color.FromArgb(255, 40, 40, 40),
            Background = Color.FromArgb(255, 252, 252, 252),
            Padding = new Thickness(5),
            Child = stack,
        };
    }

    private static List<IItemsPresenter> CollectPresenters(UIElement root)
    {
        var presenters = new List<IItemsPresenter>();
        VisualTree.Visit(root, element =>
        {
            if (element is IItemsPresenter presenter)
            {
                presenters.Add(presenter);
            }
        });

        return presenters;
    }

    private static List<string> CreateItems(int count)
    {
        var items = new List<string>(count);
        for (int index = 0; index < count; index++)
        {
            items.Add($"Item {index}");
        }

        return items;
    }

    /// <summary>An item view that overflows its slot, so a missing clip or a wrong order shows up.</summary>
    private static IDataTemplate CreateItemTemplate(Color background)
        => new DelegateTemplate<object?>(
            build: _ => new Border
            {
                Background = background,
                BorderThickness = 1,
                BorderBrush = Color.FromArgb(255, 60, 60, 60),
                Child = new TextBlock(),
            },
            bind: (view, item, index, _) =>
            {
                if (view is Border border && border.Child is TextBlock label)
                {
                    label.Text = item?.ToString() ?? index.ToString();
                }
            });

    private static void Layout(UIElement root)
    {
        root.Measure(new Size(SURFACE_WIDTH, SURFACE_HEIGHT));
        root.Arrange(new Rect(0, 0, SURFACE_WIDTH, SURFACE_HEIGHT));
    }

    private static byte[] RenderSurface(GdiGraphicsFactory factory, Action<IGraphicsContext> draw)
    {
        using var surface = factory.CreateSurface(
            RenderSurfaceDescriptor.Offscreen(SURFACE_WIDTH, SURFACE_HEIGHT, 1.0, hasAlpha: false));
        using (var context = factory.CreateContext(surface))
        {
            context.BeginFrame(surface);
            context.Clear(Color.White);
            draw(context);
            context.EndFrame();
        }

        var cpu = (ICpuPixelSurface)surface;
        var pixels = cpu.GetReadOnlyPixelSpan();
        int stride = cpu.StrideBytes;
        var copy = new byte[SURFACE_WIDTH * SURFACE_HEIGHT * 4];
        for (int row = 0; row < SURFACE_HEIGHT; row++)
        {
            pixels.Slice(row * stride, SURFACE_WIDTH * 4).CopyTo(copy.AsSpan(row * SURFACE_WIDTH * 4));
        }

        return copy;
    }

    private static void AssertPixelsEqual(byte[] expected, byte[] actual, string label)
    {
        int differing = 0;
        int firstX = -1;
        int firstY = -1;
        for (int row = 0; row < SURFACE_HEIGHT; row++)
        {
            for (int column = 0; column < SURFACE_WIDTH; column++)
            {
                int offset = (row * SURFACE_WIDTH + column) * 4;
                if (expected[offset] == actual[offset] &&
                    expected[offset + 1] == actual[offset + 1] &&
                    expected[offset + 2] == actual[offset + 2] &&
                    expected[offset + 3] == actual[offset + 3])
                {
                    continue;
                }

                differing++;
                if (firstX < 0)
                {
                    firstX = column;
                    firstY = row;
                }
            }
        }

        if (differing == 0)
        {
            return;
        }

        int firstOffset = (firstY * SURFACE_WIDTH + firstX) * 4;
        Assert.Fail(
            $"{label} differs from the immediate render at {differing} pixels. " +
            $"First at ({firstX},{firstY}): expected BGRA=" +
            $"({expected[firstOffset]},{expected[firstOffset + 1]},{expected[firstOffset + 2]},{expected[firstOffset + 3]}) " +
            $"actual BGRA=" +
            $"({actual[firstOffset]},{actual[firstOffset + 1]},{actual[firstOffset + 2]},{actual[firstOffset + 3]}).");
    }
}
