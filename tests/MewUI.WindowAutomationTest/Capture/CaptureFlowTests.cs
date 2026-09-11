using System.Reflection;
using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Platform;

namespace MewUI.WindowAutomationTest;

/// <summary>
/// Everyday gestures built on the mouse capture: each still does its job and leaves no capture behind.
/// </summary>
[TestClass]
public sealed class CaptureFlowTests
{
    [TestMethod]
    public Task SliderDrag_ChangesTheValue() => CaptureScene.RunAsync(async scene =>
    {
        var slider = new Slider { Minimum = 0, Maximum = 100, Value = 50, Width = 220, Height = 24, HorizontalAlignment = HorizontalAlignment.Left };
        var panel = new StackPanel { Margin = new Thickness(20) };
        panel.Add(slider);
        var window = await scene.ShowAsync(panel);

        var center = CaptureScene.Center(slider);
        var dragged = new Point(center.X + 60, center.Y);
        await scene.Input.MoveAsync(window, center);
        await scene.Input.PressAsync(window, center);
        await scene.Input.MoveAsync(window, dragged);
        await scene.Input.ReleaseAsync(window, dragged);

        Assert.IsTrue(slider.Value > 50, $"the drag left the value at {slider.Value}");
        Assert.IsNull(window.CapturedElement, "the slider kept the capture after the drag");
        scene.CheckPlatformCaptureFree(window, "after the drag");
    });

    [TestMethod]
    public Task TextBoxDrag_SelectsText() => CaptureScene.RunAsync(async scene =>
    {
        var editor = new TextBox { Width = 280, Text = "capture regression drag selection", HorizontalAlignment = HorizontalAlignment.Left };
        var panel = new StackPanel { Margin = new Thickness(20) };
        panel.Add(editor);
        var window = await scene.ShowAsync(panel);

        var center = CaptureScene.Center(editor);
        var start = new Point(editor.Bounds.X + 8, center.Y);
        var end = new Point(editor.Bounds.X + editor.Bounds.Width - 8, center.Y);
        await scene.Input.MoveAsync(window, start);
        await scene.Input.PressAsync(window, start);
        await scene.Input.MoveAsync(window, center);
        await scene.Input.MoveAsync(window, end);
        await scene.Input.ReleaseAsync(window, end);

        Assert.IsTrue(editor.SelectionLength > 0, "the drag selected nothing");
        Assert.IsNull(window.CapturedElement, "the text box kept the capture after the drag");
        scene.CheckPlatformCaptureFree(window, "after the drag");
    });

    [TestMethod]
    public Task ScrollBarThumbDrag_Scrolls() => CaptureScene.RunAsync(async scene =>
    {
        var viewer = new ScrollViewer
        {
            Width = 200,
            Height = 200,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(20),
            Content = new Border { Height = 2000 },
        };
        var window = await scene.ShowAsync(viewer);

        var bar = (ScrollBar)typeof(ScrollViewer).GetField("_vBar", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(viewer)!;
        var thumb = new Point(bar.Bounds.X + bar.Bounds.Width / 2, bar.Bounds.Y + 6);
        var dragged = new Point(thumb.X, thumb.Y + 80);
        await scene.Input.MoveAsync(window, thumb);
        await scene.Input.PressAsync(window, thumb);
        await scene.Input.MoveAsync(window, dragged);
        await scene.Input.ReleaseAsync(window, dragged);

        Assert.IsTrue(viewer.VerticalOffset > 0, "the thumb drag did not scroll");
        Assert.IsNull(window.CapturedElement, "the scroll bar kept the capture after the drag");
        scene.CheckPlatformCaptureFree(window, "after the drag");
    });

    [TestMethod]
    public Task ButtonClick_ClicksOnce() => CaptureScene.RunAsync(async scene =>
    {
        var button = CaptureScene.NewButton("button");
        var panel = new StackPanel { Margin = new Thickness(20) };
        panel.Add(button);
        var window = await scene.ShowAsync(panel);
        int clicks = 0;
        button.Click += () => clicks++;

        await scene.ClickAsync(window, button);

        Assert.AreEqual(1, clicks, "a click did not click once");
        Assert.IsNull(window.CapturedElement, "the button kept the capture after the click");
        scene.CheckPlatformCaptureFree(window, "after the click");
    });

    [TestMethod]
    public Task DragAndDrop_BetweenElementsDropsOnce() => CaptureScene.RunAsync(async scene =>
    {
        var source = new DragPad { Width = 140, Height = 40, Background = Color.FromRgb(90, 140, 220), CanDrag = true, HorizontalAlignment = HorizontalAlignment.Left };
        var target = new DragPad { Width = 200, Height = 80, Background = Color.FromRgb(120, 200, 140), AllowDrop = true, HorizontalAlignment = HorizontalAlignment.Left };
        var panel = new StackPanel { Margin = new Thickness(20), Spacing = 40 };
        panel.Add(source);
        panel.Add(target);
        var window = await scene.ShowAsync(panel);

        int drops = 0;
        int completions = 0;
        source.DragStarting += static e =>
        {
            e.Data = new DataObject(new Dictionary<string, object> { ["text/plain"] = "payload" });
            e.AllowedEffects = DragDropEffects.Copy;
        };
        source.DragCompleted += _ => completions++;
        target.DragOver += static e =>
        {
            e.Accepted = true;
            e.Effect = DragDropEffects.Copy;
        };
        target.Drop += e =>
        {
            drops++;
            e.Accepted = true;
            e.Effect = DragDropEffects.Copy;
        };

        var start = CaptureScene.Center(source);
        var end = CaptureScene.Center(target);
        await scene.Input.MoveAsync(window, start);
        await scene.Input.PressAsync(window, start);
        for (int step = 1; step <= 6; step++)
        {
            await scene.Input.MoveAsync(window, new Point(start.X + (end.X - start.X) * step / 6, start.Y + (end.Y - start.Y) * step / 6));
        }

        await scene.Input.ReleaseAsync(window, end);
        await Task.Delay(300);

        Assert.AreEqual(1, drops, "the drop did not reach the target once");
        Assert.AreEqual(1, completions, "the source did not see the drag complete once");
        Assert.IsNull(window.CapturedElement, "an element kept the capture after the drop");
        scene.CheckPlatformCaptureFree(window, "after the drop");
    });

    private sealed class DragPad : ContentControl
    {
    }
}
