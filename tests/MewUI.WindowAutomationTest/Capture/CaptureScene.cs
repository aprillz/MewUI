using Aprillz.MewUI;
using Aprillz.MewUI.Controls;

namespace MewUI.WindowAutomationTest;

/// <summary>
/// One mouse capture scenario on the real application loop: the windows it opens, the platform input driver,
/// and the steps and checks the capture tests share. Everything it opens is closed and any held button or key
/// released when the scenario ends.
/// </summary>
internal sealed class CaptureScene
{
    private const double WINDOW_WIDTH = 460;
    private const double WINDOW_HEIGHT = 340;

    private readonly List<Window> _windows = new();
    private Window? _spaceDownIn;

    private CaptureScene(RealInput input) => Input = input;

    public RealInput Input { get; }

    /// <summary>Runs the scenario on the UI thread, then closes what it opened even when it fails.</summary>
    public static Task RunAsync(Func<CaptureScene, Task> body)
    {
        if (!RealAppSession.IsAvailable)
        {
            Assert.Inconclusive("Needs the real application loop.");
        }

        return RealAppSession.RunAsync(async () =>
        {
            var scene = new CaptureScene(RealInput.Current);
            bool completed = false;
            try
            {
                await body(scene);
                completed = true;
            }
            finally
            {
                try
                {
                    await scene.CleanupAsync();
                }
                // A cleanup failure after a failed scenario would replace the assertion that explains it.
                catch when (!completed)
                {
                }
            }
        });
    }

    /// <summary>Shows a window with the content, places it beside the scene's other windows and makes it active.</summary>
    public async Task<Window> ShowAsync(UIElement content)
    {
        var window = new Window
        {
            Title = "WindowAutomationTest capture",
            StartupLocation = WindowStartupLocation.Manual,
            WindowSize = WindowSize.Fixed(WINDOW_WIDTH, WINDOW_HEIGHT),
            Content = content,
        };
        window.Show();
        // Side by side, so a press meant for one window never lands on another.
        window.MoveTo(60 + _windows.Count * (WINDOW_WIDTH + 40), 80);
        _windows.Add(window);
        await Task.Delay(250);
        await Input.ActivateAsync(window);
        return window;
    }

    public static Point Center(UIElement element)
        => new(element.Bounds.X + element.Bounds.Width / 2, element.Bounds.Y + element.Bounds.Height / 2);

    /// <summary>Empty client area near the bottom-right corner, away from the top-left content.</summary>
    public static Point Away(Window window)
        => new(window.ClientSize.Width - 30, window.ClientSize.Height - 30);

    /// <summary>The window that receives input at the element's center, and that point in its client area.</summary>
    public static (Window Surface, Point Client) SurfaceCenter(UIElement element, Window owner)
    {
        var inOwner = element.TranslatePoint(new Point(element.Bounds.Width / 2, element.Bounds.Height / 2), owner);
        var surface = element.ResolveInputHostWindow() ?? owner;
        return (surface, surface.VisualTreePointToSurface(inOwner));
    }

    public static Button NewButton(string text)
        => new() { Content = new TextBlock { Text = text }, Width = 140, Height = 36, HorizontalAlignment = HorizontalAlignment.Left };

    /// <summary>Fails when the platform still reports a mouse capture; platforms without one pass.</summary>
    public void CheckPlatformCaptureFree(Window window, string when)
    {
        var state = Input.OsCapture(window);
        Assert.IsTrue(state is OsCaptureState.NotApplicable or OsCaptureState.Free, $"platform capture is {state} {when}");
    }

    /// <summary>Fails when the platform reports no mouse capture; platforms without one pass.</summary>
    public void CheckPlatformCaptureHeld(Window holder, string when)
    {
        var state = Input.OsCapture(holder);
        Assert.IsTrue(state is OsCaptureState.NotApplicable or OsCaptureState.Held, $"platform capture is {state} {when}");
    }

    public async Task ClickAsync(Window window, UIElement element)
    {
        var center = Center(element);
        await Input.MoveAsync(window, center);
        await Input.PressAsync(window, center);
        await Input.ReleaseAsync(window, center);
    }

    /// <summary>Presses or releases Space, remembering a held key so cleanup releases it: a key left down stays down in the display server.</summary>
    public async Task SpaceAsync(Window window, bool isDown)
    {
        await Input.SpaceAsync(window, isDown);
        _spaceDownIn = isDown ? window : null;
    }

    public async Task PressAndLeaveAsync(Window window, UIElement target)
    {
        var center = Center(target);
        await Input.MoveAsync(window, center);
        await Input.PressAsync(window, center);
        Assert.IsTrue(target.IsMouseCaptured, "precondition: the press captured");
        await Input.MoveAsync(window, Away(window));
    }

    public async Task<(Window Window, Slider Slider)> ShowSliderAsync()
    {
        var slider = new Slider { Minimum = 0, Maximum = 100, Width = 220, Height = 24, HorizontalAlignment = HorizontalAlignment.Left };
        var panel = new StackPanel { Margin = new Thickness(20) };
        panel.Add(slider);
        var window = await ShowAsync(panel);
        return (window, slider);
    }

    public async Task PressSliderAsync(Window window, Slider slider)
    {
        var center = Center(slider);
        await Input.MoveAsync(window, center);
        await Input.PressAsync(window, center);
        Assert.IsTrue(slider.IsMouseCaptured, "precondition: the press captured");
    }

    public async Task<(Window Window, Button Anchor)> ShowAnchorAsync()
    {
        var anchor = NewButton("anchor");
        var panel = new StackPanel { Margin = new Thickness(20) };
        panel.Add(anchor);
        var window = await ShowAsync(panel);
        return (window, anchor);
    }

    public static async Task<Popup> OpenPopupAsync(UIElement anchor, UIElement child)
    {
        var content = new StackPanel { Margin = new Thickness(12) };
        content.Add(child);
        var popup = new Popup { Content = content };
        popup.ShowAt(anchor, anchor.Bounds);
        await Task.Delay(300);
        Assert.IsTrue(popup.IsOpen, "precondition: the popup opened");
        return popup;
    }

    public async Task PressOutsideAsync(Window window)
    {
        var away = Away(window);
        await Input.MoveAsync(window, away);
        await Input.PressAsync(window, away);
        await Input.ReleaseAsync(window, away);
        await Task.Delay(150);
    }

    private async Task CleanupAsync()
    {
        if (_spaceDownIn != null)
        {
            await Input.SpaceAsync(_spaceDownIn, isDown: false);
            _spaceDownIn = null;
        }

        if (Input.IsButtonDown && _windows.Count > 0)
        {
            await Input.ReleaseAsync(_windows[0], Away(_windows[0]));
        }

        for (int index = _windows.Count - 1; index >= 0; index--)
        {
            _windows[index].Close();
        }

        _windows.Clear();
        await Task.Delay(200);
    }
}
