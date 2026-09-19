using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Rendering.Gdi;
using MewUI.Test.Infrastructure;

namespace MewUI.Test.Rendering;

/// <summary>
/// A state change that animates (a colour transition, a fade, a sliding thumb) changes what is drawn on
/// every frame of the animation, not only on the first. Each control is taken through pointer over,
/// press, release and pointer away with the animation clock stepped a frame at a time, and every frame
/// is held against a frame drawn straight from the visuals.
/// Not parallelizable: assigns the process-wide Application.DefaultGraphicsFactory.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class RetainedAnimationSweepTests
{
    private const int WIDTH = 420;
    private const int HEIGHT = 300;
    private const int FRAMES_PER_STATE = 14;

    private sealed record Row(string Name, int Role);

    public static IEnumerable<object[]> Controls()
    {
        foreach (string name in new[]
        {
            "Button", "ToggleButton", "RepeatButton", "CheckBox", "RadioButton", "ToggleSwitch", "Slider",
            "ProgressBarIndeterminate", "ProgressRing", "ComboBox", "NumericUpDown", "TextBox", "PasswordBox",
            "MultiLineTextBox", "Expander", "GroupBox", "TabControl", "SegmentedControl", "ButtonGroup",
            "ListBox", "TreeView", "GridView", "NavigationView", "ToolBar", "Calendar", "DatePicker",
            "ColorPicker", "MenuBar", "SplitButton", "DropDownButton", "ScrollViewerAutoHide", "Label",
        })
        {
            yield return [name];
        }
    }

    [TestMethod]
    [DynamicData(nameof(Controls))]
    public void EveryAnimationFrame_MatchesTheReference(string name)
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
        }

        using var factory = new GdiGraphicsFactory();
        Application.DefaultGraphicsFactory = factory;

        var control = Create(name);
        control.HorizontalAlignment = HorizontalAlignment.Left;
        control.VerticalAlignment = VerticalAlignment.Top;
        control.Margin = new Thickness(16);

        var window = HeadlessWindow.Create(WIDTH, HEIGHT);
        window.Content = control;
        window.PerformLayout();
        using var surface = factory.CreateSurface(RenderSurfaceDescriptor.Offscreen(WIDTH, HEIGHT, 1.0, hasAlpha: false));

        long start = System.Diagnostics.Stopwatch.GetTimestamp();
        long frame = System.Diagnostics.Stopwatch.Frequency / 60;
        int step = 0;
        void Run(string state)
        {
            for (int index = 0; index < FRAMES_PER_STATE; index++)
            {
                step++;
                Aprillz.MewUI.Animation.AnimationManager.Instance.UpdateAt(start + (frame * step));
                window.UpdateVisualStates();
                window.PerformLayout();
                window.RenderFrameToSurface(surface);
                AssertMatchesReference(factory, window, surface, $"{name}, {state}, frame {index + 1}");
            }
        }

        Run("at rest");

        // Two points: the middle of the control and a point near its right edge, where drop-down
        // arrows, spinner buttons and scroll bars live.
        var bounds = control.Bounds;
        var points = new[]
        {
            new Point(bounds.X + Math.Min(bounds.Width, WIDTH - 40) / 2, bounds.Y + Math.Min(bounds.Height, HEIGHT - 40) / 2),
            new Point(bounds.X + Math.Min(bounds.Width, WIDTH - 40) - 8, bounds.Y + Math.Min(14, bounds.Height / 2)),
            new Point(bounds.X + 40, bounds.Y + 12),
        };

        foreach (var point in points)
        {
            window.SendMouseMove(point);
            Run($"pointer at {point}");
            window.SendMouseDown(point);
            Run($"pressed at {point}");
            window.SendMouseUp(point);
            Run($"released at {point}");
            window.SendMouseMove(new Point(WIDTH - 3, HEIGHT - 3));
            Run($"pointer away after {point}");
        }
    }

    /// <summary>
    /// Keyboard focus moving from control to control starts and ends a transition on each of them, and
    /// keys change their state without the pointer ever being near.
    /// </summary>
    [TestMethod]
    [DataRow("buttons")]
    [DataRow("inputs")]
    [DataRow("lists")]
    [DataRow("containers")]
    public void FocusAndKeys_MatchTheReferenceAtEveryFrame(string group)
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
        }

        using var factory = new GdiGraphicsFactory();
        Application.DefaultGraphicsFactory = factory;

        string[] names = group switch
        {
            "buttons" => ["Button", "ToggleButton", "CheckBox", "RadioButton", "ToggleSwitch", "SplitButton", "DropDownButton"],
            "inputs" => ["TextBox", "PasswordBox", "NumericUpDown", "ComboBox", "Slider", "DatePicker"],
            "lists" => ["ListBox", "TreeView"],
            _ => ["TabControl", "SegmentedControl", "ButtonGroup", "Expander"],
        };

        var stack = new StackPanel { Orientation = Orientation.Vertical, Spacing = 6, Margin = new Thickness(12) };
        foreach (string controlName in names)
        {
            var control = Create(controlName);
            control.HorizontalAlignment = HorizontalAlignment.Left;
            if (control.Height > 120 || double.IsNaN(control.Height) && controlName is "ListBox" or "TreeView" or "TabControl")
            {
                control.Height = 110;
            }

            stack.Children(control);
        }

        var window = HeadlessWindow.Create(WIDTH, HEIGHT + 120);
        window.Content = stack;
        window.PerformLayout();
        using var surface = factory.CreateSurface(RenderSurfaceDescriptor.Offscreen(WIDTH, HEIGHT + 120, 1.0, hasAlpha: false));

        long start = System.Diagnostics.Stopwatch.GetTimestamp();
        long frame = System.Diagnostics.Stopwatch.Frequency / 60;
        int step = 0;
        void Run(string state)
        {
            for (int index = 0; index < 10; index++)
            {
                step++;
                Aprillz.MewUI.Animation.AnimationManager.Instance.UpdateAt(start + (frame * step));
                window.UpdateVisualStates();
                window.PerformLayout();
                window.RenderFrameToSurface(surface);
                AssertMatchesReference(factory, window, surface, $"{group}, {state}, frame {index + 1}", WIDTH, HEIGHT + 120);
            }
        }

        var focused = new HashSet<UIElement>(ReferenceEqualityComparer.Instance);
        Run("at rest");
        for (int stop = 0; stop < names.Length + 2; stop++)
        {
            window.FocusManager.MoveFocusNext();
            if (window.FocusManager.FocusedElement is UIElement holder)
            {
                focused.Add(holder);
            }

            Run($"focus moved {stop + 1} times");

            window.SendKeyDown(Key.Space);
            Run($"Space down at focus stop {stop + 1}");
            window.SendKeyUp(Key.Space);
            Run($"Space up at focus stop {stop + 1}");

            window.SendKeyPress(Key.Down);
            Run($"Down at focus stop {stop + 1}");
            window.SendKeyPress(Key.Right);
            Run($"Right at focus stop {stop + 1}");
            window.SendKeyPress(Key.Escape);
            Run($"Escape at focus stop {stop + 1}");
        }

        window.FocusManager.MoveFocusPrevious();
        Run("focus moved back");
        Assert.IsTrue(focused.Count >= 2, $"{group}: focus reached {focused.Count} visuals, so the keys proved nothing");
    }

    private static FrameworkElement Create(string name)
    {
        switch (name)
        {
            case "Button": return new Button { Content = new TextBlock { Text = "Button" }, Width = 120 };
            case "ToggleButton": return new ToggleButton { Content = new TextBlock { Text = "Toggle" }, Width = 120 };
            case "RepeatButton": return new RepeatButton { Content = new TextBlock { Text = "Repeat" }, Width = 120 };
            case "CheckBox": return new CheckBox { Content = new TextBlock { Text = "Check" } };
            case "RadioButton": return new RadioButton { Content = new TextBlock { Text = "Radio" } };
            case "ToggleSwitch": return new ToggleSwitch();
            case "Slider": return new Slider { Width = 200, Minimum = 0, Maximum = 100, Value = 30 };
            case "ProgressBarIndeterminate": return new ProgressBar { Width = 200, Height = 8, IsIndeterminate = true };
            case "ProgressRing": return new ProgressRing { Width = 40, Height = 40, IsActive = true };
            case "ComboBox": return new ComboBox { Width = 160 }.Items(new[] { "Alpha", "Beta", "Gamma" }).SelectedIndex(0);
            case "NumericUpDown": return new NumericUpDown { Width = 160, Value = 5 };
            case "TextBox": return new TextBox { Width = 200, Text = "text" };
            case "PasswordBox": return new PasswordBox { Width = 200 };
            case "MultiLineTextBox": return new MultiLineTextBox { Width = 260, Height = 120, Text = "first\nsecond\nthird" };
            case "Expander": return new Expander { Width = 240 }.Header("Expander").Content(new TextBlock { Text = "content" });
            case "GroupBox": return new GroupBox { Width = 240 }.Header("Group").Content(new Button { Content = new TextBlock { Text = "inside" } });
            case "TabControl":
                return new TabControl { Width = 320, Height = 160 }.TabItems(
                    new TabItem().Header("First").Content(new TextBlock { Text = "one" }),
                    new TabItem().Header("Second").Content(new TextBlock { Text = "two" }),
                    new TabItem().Header("Third").Content(new TextBlock { Text = "three" }));
            case "SegmentedControl": return new SegmentedControl().Items("Day", "Week", "Month").SelectedIndex(0);
            case "ButtonGroup": return new ButtonGroup().Items("Cut", "Copy", "Paste");
            case "ListBox": return new ListBox { Width = 200, Height = 160 }.Items(Enumerable.Range(0, 30).Select(index => $"item {index}").ToArray());
            case "TreeView":
                var tree = new TreeView { Width = 220, Height = 160 };
                var root = new TreeViewNode("root", [new TreeViewNode("child"), new TreeViewNode("sibling")]);
                tree.ItemsSource([root, new TreeViewNode("leaf")]);
                tree.Expand(root);
                return tree;
            case "GridView":
                return new GridView { Width = 360, Height = 200 }
                    .ItemsSource(Enumerable.Range(0, 20).Select(index => new Row($"row {index}", index % 3)).ToArray())
                    .Columns(
                        new GridViewColumn<Row>().Header("Name").Width(120).Text(row => row.Name),
                        new GridViewColumn<Row>().Header("Role").Width(120).Template(
                            build: _ => new ComboBox().Items(new[] { "User", "Admin", "Guest" }).CenterVertical(),
                            bind: (view, row) => view.SelectedIndex = row.Role));
            case "NavigationView":
                var navigation = new NavigationView { Width = 380, Height = 260, PaneWidth = 120, PaneDisplayMode = PaneDisplayMode.Inline, IsPaneOpen = true };
                navigation.Items(new[] { "One", "Two", "Three" }, title => title, content: title => new TextBlock { Text = title });
                navigation.SelectedIndex = 0;
                return navigation;
            case "ToolBar":
                var bar = new ToolBar { Width = 380 };
                bar.Bands.Add(new ToolBarBand(new ToolBarGroup(Item("open"), Item("save"), Item("print")), new ToolBarGroup(Item("cut"), Item("copy"))));
                return bar;
            case "Calendar": return new Calendar { DisplayDate = new DateTime(2026, 3, 15) };
            case "DatePicker": return new DatePicker { Width = 180 };
            case "ColorPicker": return new ColorPicker { Width = 180 };
            case "MenuBar":
                return new MenuBar { Width = 300 }.Items(
                    new MenuItem("_File"),
                    new MenuItem("_Edit"));
            case "SplitButton": return new SplitButton { Content = new TextBlock { Text = "Split" }, Width = 140 };
            case "DropDownButton": return new DropDownButton { Content = new TextBlock { Text = "Drop" }, Width = 140 };
            case "ScrollViewerAutoHide":
                var rows = new StackPanel { Orientation = Orientation.Vertical };
                for (int index = 0; index < 60; index++)
                {
                    rows.Children(new TextBlock { Text = $"row {index}" });
                }

                return new ScrollViewer { Width = 220, Height = 160, VerticalScroll = ScrollMode.Auto, AutoHideScrollBars = true, Content = rows };
            case "Label": return new Label().Text("_Label text");
            default: throw new ArgumentOutOfRangeException(nameof(name), name, "no control of that name in the sweep");
        }
    }

    private static ToolBarItem Item(string id)
        => new(new Command(id, id)) { Presentation = CommandPresentationMode.Text };

    private static void AssertMatchesReference(GdiGraphicsFactory factory, Window window, IRenderSurface actual, string label)
        => AssertMatchesReference(factory, window, actual, label, WIDTH, HEIGHT);

    private static void AssertMatchesReference(GdiGraphicsFactory factory, Window window, IRenderSurface actual, string label, int width, int height)
    {
        using var reference = factory.CreateSurface(RenderSurfaceDescriptor.Offscreen(width, height, 1.0, hasAlpha: false));
        window.RenderReferenceFrameToSurface(reference);
        ReadOnlySpan<byte> expected = ((ICpuPixelSurface)reference).GetReadOnlyPixelSpan();
        ReadOnlySpan<byte> shown = ((ICpuPixelSurface)actual).GetReadOnlyPixelSpan();
        int differing = 0;
        int minX = int.MaxValue, minY = int.MaxValue, maxX = -1, maxY = -1;
        for (int offset = 0; offset + 3 < expected.Length; offset += 4)
        {
            if (expected[offset] != shown[offset] || expected[offset + 1] != shown[offset + 1] || expected[offset + 2] != shown[offset + 2])
            {
                differing++;
                int pixel = offset / 4;
                minX = Math.Min(minX, pixel % width);
                maxX = Math.Max(maxX, pixel % width);
                minY = Math.Min(minY, pixel / width);
                maxY = Math.Max(maxY, pixel / width);
            }
        }

        Assert.AreEqual(
            0,
            differing,
            $"{label}: {differing} pixels differ from a frame drawn straight from the visuals, inside ({minX},{minY})-({maxX},{maxY}); damage {string.Join(" ", window.LastRetainedDamageAreas)}");
    }
}
