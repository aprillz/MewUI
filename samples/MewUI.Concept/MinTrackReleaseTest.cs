using Aprillz.MewUI.Controls;

namespace Aprillz.MewUI.Concept;

// ─────────────────────────────────────────────────────────────────────────────
// Verifies the Win32 min-track release for windows without visible non-client chrome.
//
// The OS default minimum track size (~136px wide) exists for caption buttons. Transparent
// and borderless windows have no visible chrome, so HandleGetMinMaxInfo floors
// ptMinTrackSize at 1px and only WindowSize.Min applies.
//
// Check (drag each window smaller):
//   - "Transparent, Min 0"   : native edges. Must shrink far below 91 DIP (136px @150%).
//   - "Borderless, Min 0"    : no native edges; drag the orange corner grip. Same expectation.
//   - "Transparent, Min 200" : must stop exactly at 200x150 (WindowSize.Min still applies).
// The readout and stderr show the live client size.
// ─────────────────────────────────────────────────────────────────────────────
internal static class MinTrackReleaseTest
{
    internal static Window Create()
    {
        var window = new Window()
            .Title("Min-track release - chrome-less windows resize below the OS minimum")
            .Resizable(760, 220);

        window.Content = new StackPanel()
            .Vertical()
            .Margin(12)
            .Spacing(8)
            .Children(
                new TextBlock
                {
                    Text = "Drag each window smaller. Chrome-less windows must go far below 91 DIP wide;\n" +
                           "the Min 200 case must stop at exactly 200x150.",
                    FontSize = 12,
                },
                new StackPanel()
                    .Horizontal()
                    .Spacing(8)
                    .Children(
                        new Button().Content("Transparent, Min 0").OnClick(() => OpenTransparent(minWidth: 0, minHeight: 0)),
                        new Button().Content("Borderless, Min 0").OnClick(OpenBorderless),
                        new Button().Content("Transparent, Min 200").OnClick(() => OpenTransparent(minWidth: 200, minHeight: 150))));

        return window;
    }

    /// <summary>Transparent custom chrome: native resize edges via the backend hit test.</summary>
    private static void OpenTransparent(double minWidth, double minHeight)
    {
        string label = minWidth > 0 ? $"transparent min {minWidth:0}" : "transparent min 0";
        Window window = null!;
        TextBlock readout = null!;

        window = new Window()
            .Title($"Min-track: {label}")
            .Resizable(320, 220, minWidth, minHeight)
            .OnBuild(x =>
            {
                x.AllowsTransparency = true;
                x.Background = Color.Transparent;
                x.Padding = new Thickness(0);
                x.Content = new Border
                {
                    Background = Color.SteelBlue,
                    Child = BuildContent(() => window, includeGrip: false, out readout),
                };
            });

        AttachReadout(window, label, () => readout);
        window.Show();
    }

    /// <summary>Borderless: no native edges, resize through DragResize from a corner grip.</summary>
    private static void OpenBorderless()
    {
        const string label = "borderless min 0";
        Window window = null!;
        TextBlock readout = null!;

        window = new Window()
            .Title($"Min-track: {label}")
            .Resizable(320, 220)
            .OnBuild(x =>
            {
                x.Borderless = true;
                x.Padding = new Thickness(0);
                x.Content = new Border
                {
                    Background = Color.DarkSeaGreen,
                    Child = BuildContent(() => window, includeGrip: true, out readout),
                };
            });

        AttachReadout(window, label, () => readout);
        window.Show();
    }

    private static UIElement BuildContent(Func<Window> getWindow, bool includeGrip, out TextBlock readout)
    {
        var panel = new DockPanel()
            .Margin(8)
            .Children(
                new TextBlock { Text = "drag me (title area)", FontSize = 11 }
                    .DockTop()
                    .OnMouseDown(mouseEvent =>
                    {
                        if (mouseEvent.Button == MouseButton.Left)
                        {
                            getWindow().DragMove();
                            mouseEvent.Handled = true;
                        }
                    }));

        if (includeGrip)
        {
            panel.Children(
                new Border
                {
                    Width = 18,
                    Height = 18,
                    Background = Color.Orange,
                    HorizontalAlignment = HorizontalAlignment.Right,
                }
                    .DockBottom()
                    .OnMouseDown(mouseEvent =>
                    {
                        if (mouseEvent.Button == MouseButton.Left)
                        {
                            getWindow().DragResize(ResizeEdge.BottomRight);
                            mouseEvent.Handled = true;
                        }
                    }));
        }

        var text = new TextBlock
        {
            FontFamily = "Consolas",
            FontSize = 12,
        };
        panel.Children(text);
        readout = text;

        return panel;
    }

    private static void AttachReadout(Window window, string label, Func<TextBlock?> getReadout)
    {
        void Report(Size clientSize)
        {
            string line = $"client={clientSize.Width:0.#}x{clientSize.Height:0.#}";
            if (getReadout() is TextBlock readout)
            {
                readout.Text = line;
            }
            Console.Error.WriteLine($"[mintrack/{label}] {line}");
        }

        window.OnLoaded(() => Report(window.ClientSize));
        window.ClientSizeChanged += Report;
    }
}
