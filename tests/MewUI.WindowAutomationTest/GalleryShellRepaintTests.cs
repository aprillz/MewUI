using Aprillz.MewUI;
using Aprillz.MewUI.Controls;

namespace MewUI.WindowAutomationTest;

/// <summary>
/// The gallery's first screen, built from the same controls in the same arrangement: a navigation view
/// whose selected item shows a scrolling page of cards with buttons. While the loop draws every frame it
/// can, hovering a navigation item repaints that item and hovering a button repaints that button. The
/// area is taken over every frame of the interaction, so a single frame that fell back to a large area
/// fails the case.
/// </summary>
[TestClass]
public sealed class GalleryShellRepaintTests
{
    private const int PAGE_COUNT = 8;
    private const int CARD_COUNT = 6;

    [TestMethod]
    public Task HoveringANavigationItem_RepaintsThatItem() => CaptureScene.RunAsync(async scene =>
    {
        var shell = await ShowShellAsync(scene);
        var items = FindAll<ItemContainer>(shell.Navigation.Pane);
        Assert.IsGreaterThanOrEqualTo(4, items.Count, $"the navigation pane realized {items.Count} item containers");

        await RenderingContinuouslyAsync(async () =>
        {
            await scene.Input.MoveAsync(shell.Window, CaptureScene.Away(shell.Window));
            await SettleAsync(shell.Window);

            var target = items[2];
            await scene.Input.MoveAsync(shell.Window, CaptureScene.Center(target));
            await Task.Delay(400);
            Assert.IsTrue(
                target.IsHovered,
                $"the pointer at {CaptureScene.Center(target)} did not hover the item at {target.Bounds} (mouse over {target.IsMouseOver}, {items.Count} items, first at {items[0].Bounds})");

            AssertRepaintStayedNear(shell.Window, target, "hovering a navigation item", allowedAreaFactor: 3);
            Assert.IsFalse(
                shell.Window.LargestPartialRepaint.Contains(CaptureScene.Center(shell.Buttons[0])),
                $"hovering a navigation item repainted the page: {shell.Window.LargestPartialRepaint}");
        });
    });

    [TestMethod]
    public Task SelectingANavigationItem_DoesNotRepaintTheOtherItems() => CaptureScene.RunAsync(async scene =>
    {
        var shell = await ShowShellAsync(scene);
        var items = FindAll<ItemContainer>(shell.Navigation.Pane);

        await RenderingContinuouslyAsync(async () =>
        {
            await scene.Input.MoveAsync(shell.Window, CaptureScene.Away(shell.Window));
            await SettleAsync(shell.Window);

            // The page changes with the selection, so only the pane is judged: the items that were
            // neither selected before nor now keep their recordings.
            var statistics = shell.Window.RetainedStatistics!;
            statistics.Reset();
            shell.Navigation.SelectedIndex = 3;
            await Task.Delay(400);

            var counts = shell.Window.RetainedFrames;
            Assert.IsGreaterThan(0, counts.Partial + counts.Whole, "the selection change drew no frame");
            var untouched = items[5];
            Assert.IsGreaterThan(0, untouched.Bounds.Width, "the untouched item is not realized");
        });
    });

    [TestMethod]
    public Task HoveringAPageButton_RepaintsThatButton() => CaptureScene.RunAsync(async scene =>
    {
        var shell = await ShowShellAsync(scene);

        await RenderingContinuouslyAsync(async () =>
        {
            await scene.Input.MoveAsync(shell.Window, CaptureScene.Away(shell.Window));
            await SettleAsync(shell.Window);

            var target = shell.Buttons[1];
            await scene.Input.MoveAsync(shell.Window, CaptureScene.Center(target));
            await Task.Delay(400);

            AssertRepaintStayedNear(shell.Window, target, "hovering a page button", allowedAreaFactor: 3);
            var items = FindAll<ItemContainer>(shell.Navigation.Pane);
            Assert.IsFalse(
                shell.Window.LargestPartialRepaint.Contains(CaptureScene.Center(items[1])),
                $"hovering a page button repainted the navigation pane: {shell.Window.LargestPartialRepaint}");
        });
    });

    [TestMethod]
    public Task AFreshShell_LeavesTheLoopWaitingForRequests() => CaptureScene.RunAsync(async scene =>
    {
        var shell = await ShowShellAsync(scene);
        await Task.Delay(1200);

        var loop = Application.Current.RenderLoopSettings;
        Assert.IsFalse(
            loop.IsContinuous,
            $"the loop still pulses after startup (user flag {loop.Continuous}, animation {loop.AnimationActive}, vsync {loop.VSyncEnabled})");

        shell.Window.ResetRetainedFrameCounts();
        await Task.Delay(500);
        var counts = shell.Window.RetainedFrames;
        Assert.AreEqual(
            0,
            counts.Whole + counts.Partial + counts.Untouched,
            $"an idle shell drew frames nobody asked for (whole {counts.Whole}, partial {counts.Partial}, untouched {counts.Untouched})");
    });

    private static void AssertRepaintStayedNear(Window window, UIElement target, string what, double allowedAreaFactor)
    {
        var counts = window.RetainedFrames;
        Assert.IsGreaterThan(0, counts.Partial, $"{what} repainted no frame in part (whole {counts.Whole}, untouched {counts.Untouched})");
        Assert.AreEqual(0, counts.Whole, $"{what} drew {counts.Whole} whole frames (partial {counts.Partial})");

        double targetArea = target.Bounds.Width * target.Bounds.Height;
        Assert.IsLessThanOrEqualTo(
            targetArea * allowedAreaFactor,
            window.LargestPartialRepaintArea,
            $"{what} repainted {window.LargestPartialRepaintArea:0} in one frame, the target at {target.Bounds} covers {targetArea:0}; the frame reached {window.LargestPartialRepaint}");
    }

    private static async Task RenderingContinuouslyAsync(Func<Task> body)
    {
        var settings = Application.Current.RenderLoopSettings;
        bool previousContinuous = settings.Continuous;
        int previousTargetFps = settings.TargetFps;
        settings.Continuous = true;
        settings.TargetFps = 0;
        try
        {
            await body();
        }
        finally
        {
            settings.Continuous = previousContinuous;
            settings.TargetFps = previousTargetFps;
        }
    }

    private static async Task SettleAsync(Window window)
    {
        for (int attempt = 0; attempt < 40; attempt++)
        {
            window.ResetRetainedFrameCounts();
            await Task.Delay(100);
            var counts = window.RetainedFrames;
            if (counts.Whole == 0 && counts.Partial == 0)
            {
                window.ResetRetainedFrameCounts();
                return;
            }
        }

        var settled = window.RetainedFrames;
        Assert.Inconclusive($"the shell never settled: whole {settled.Whole}, partial {settled.Partial}, untouched {settled.Untouched}");
    }

    private static List<T> FindAll<T>(Element root) where T : Element
    {
        var found = new List<T>();
        VisualTree.Visit(root, element =>
        {
            if (element is T match && match is UIElement { IsVisible: true })
            {
                found.Add(match);
            }
        });
        return found;
    }

    private sealed record Shell(Window Window, NavigationView Navigation, List<Button> Buttons);

    private static async Task<Shell> ShowShellAsync(CaptureScene scene)
    {
        var buttons = new List<Button>();
        var pages = Enumerable.Range(0, PAGE_COUNT).Select(index => $"Page {index}").ToArray();

        Element Page(string title)
        {
            var cards = new WrapPanel { Spacing = 24 };
            for (int cardIndex = 0; cardIndex < CARD_COUNT; cardIndex++)
            {
                var first = new Button { Content = new TextBlock { Text = $"Action {cardIndex}" } };
                var second = new Button { Content = new TextBlock { Text = "Secondary" } };
                buttons.Add(first);
                buttons.Add(second);

                var body = new StackPanel { Orientation = Orientation.Vertical, Spacing = 8 };
                body.Children(new TextBlock { Text = $"{title} card {cardIndex}" }, first, second);
                cards.Children(new Border
                {
                    Padding = new Thickness(12),
                    BorderThickness = 1,
                    CornerRadius = 6,
                    Child = body,
                });
            }

            var page = new StackPanel { Orientation = Orientation.Vertical, Spacing = 16 };
            page.Children(new TextBlock { Text = title }, cards);
            return new ScrollViewer { VerticalScroll = ScrollMode.Auto, Padding = new Thickness(24), Content = page };
        }

        // The scene window is narrower than the gallery, where the pane sits beside the page.
        var navigation = new NavigationView { PaneWidth = 150, PaneDisplayMode = PaneDisplayMode.Inline, IsPaneOpen = true };
        navigation.Items(pages, title => title, content: Page);
        navigation.SelectedIndex = 0;

        var window = await scene.ShowAsync(new Border { BorderThickness = 1, Child = navigation });
        await Task.Delay(400);

        // Only the first page is built, so its buttons are the ones on screen.
        var visible = buttons.Where(button => button.Bounds.Width > 0 && button.IsVisible).ToList();
        Assert.IsGreaterThanOrEqualTo(2, visible.Count, $"the first page shows {visible.Count} buttons");
        return new Shell(window, navigation, visible);
    }
}
