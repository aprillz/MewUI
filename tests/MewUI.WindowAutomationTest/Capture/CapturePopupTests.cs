using System.Reflection;
using Aprillz.MewUI;
using Aprillz.MewUI.Controls;

namespace MewUI.WindowAutomationTest;

/// <summary>
/// An open popup keeps its dismiss watch through presses inside it and through element captures in its
/// owner, so an outside press, or a press on another application, still closes it.
/// </summary>
[TestClass]
public sealed class CapturePopupTests
{
    [TestMethod]
    public Task OutsidePress_DismissesAfterADragInsideThePopup() => CaptureScene.RunAsync(async scene =>
    {
        var (window, anchor) = await scene.ShowAnchorAsync();
        var slider = new Slider { Minimum = 0, Maximum = 100, Value = 50, Width = 220, Height = 24 };
        var popup = await CaptureScene.OpenPopupAsync(anchor, slider);

        await DragSliderInsidePopupAsync(scene, window, slider);
        Assert.IsTrue(popup.IsOpen, "the drag inside the popup closed it");

        await scene.PressOutsideAsync(window);
        Assert.IsFalse(popup.IsOpen, "an outside press no longer dismisses the popup");
    });

    [TestMethod]
    public Task OutsidePress_DismissesAfterAClickInsideThePopup() => CaptureScene.RunAsync(async scene =>
    {
        var (window, anchor) = await scene.ShowAnchorAsync();
        var inner = CaptureScene.NewButton("inner");
        int clicks = 0;
        inner.Click += () => clicks++;
        var popup = await CaptureScene.OpenPopupAsync(anchor, inner);

        var (surface, center) = CaptureScene.SurfaceCenter(inner, window);
        await scene.Input.MoveAsync(surface, center);
        await scene.Input.PressAsync(surface, center);
        await scene.Input.ReleaseAsync(surface, center);
        Assert.AreEqual(1, clicks, "precondition: the button inside the popup did not click once");
        Assert.IsTrue(popup.IsOpen, "the click inside the popup closed it");

        await scene.PressOutsideAsync(window);
        Assert.IsFalse(popup.IsOpen, "an outside press no longer dismisses the popup");
    });

    [TestMethod]
    public Task OutsidePress_DismissesAPopupOpenedAfterAReleaseAwayInTheOwner() => CaptureScene.RunAsync(async scene =>
    {
        var (window, anchor) = await scene.ShowAnchorAsync();

        await scene.PressAndLeaveAsync(window, anchor);
        await scene.Input.ReleaseAsync(window, CaptureScene.Away(window));

        var popup = await CaptureScene.OpenPopupAsync(anchor, CaptureScene.NewButton("inner"));

        await scene.PressOutsideAsync(window);
        Assert.IsFalse(popup.IsOpen, "an outside press no longer dismisses a popup opened after a release away");
    });

    [TestMethod]
    public Task ComboBox_ClosesOnAnOutsideClick() => CaptureScene.RunAsync(async scene =>
    {
        var (window, combo) = await ShowComboBoxAsync(scene);

        await scene.ClickAsync(window, combo);
        Assert.IsTrue(combo.IsDropDownOpen, "precondition: clicking the combo box opened it");

        await scene.PressOutsideAsync(window);
        Assert.IsFalse(combo.IsDropDownOpen, "an outside click did not close the drop-down");
        scene.CheckPlatformCaptureFree(window, "after the drop-down closed");
    });

    [TestMethod]
    public Task ComboBox_ItemClickSelectsAndCloses() => CaptureScene.RunAsync(async scene =>
    {
        var (window, combo) = await ShowComboBoxAsync(scene);

        await scene.ClickAsync(window, combo);
        Assert.IsTrue(combo.IsDropDownOpen, "precondition: clicking the combo box opened it");

        var list = (ListBox)typeof(ComboBox).GetField("_popupList", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(combo)!;
        var row = (UIElement)VisualTree.Find(list, static element => element is TextBlock text && text.Text == "item 2")!;
        for (int attempt = 0; attempt < 20 && row.ResolveInputHostWindow()?.Handle is null or 0; attempt++)
        {
            await Task.Delay(50);
        }

        var host = row.ResolveInputHostWindow();
        Assert.IsTrue(host != null && host.Handle != 0, "precondition: the drop-down surface has no platform window");
        var (surface, center) = CaptureScene.SurfaceCenter(row, window);
        var centerInOwner = window.ScreenToClient(surface.ClientToScreen(center));
        await scene.Input.MoveAsync(surface, center);
        await scene.Input.PressAsync(surface, center);

        // Selecting on press closes the drop-down and destroys its surface, so release over the same spot through the owner.
        if (surface.Handle != 0)
        {
            await scene.Input.ReleaseAsync(surface, center);
        }
        else
        {
            await scene.Input.ReleaseAsync(window, centerInOwner);
        }

        await Task.Delay(150);

        Assert.AreEqual(2, combo.SelectedIndex, "the item click did not select the item");
        Assert.IsFalse(combo.IsDropDownOpen, "the item click did not close the drop-down");
        scene.CheckPlatformCaptureFree(window, "after the drop-down closed");
    });

    [TestMethod]
    public Task PressOnAnotherApplication_DismissesAnUntouchedPopup() => CaptureScene.RunAsync(async scene =>
    {
        var (_, anchor) = await scene.ShowAnchorAsync();
        var popup = await CaptureScene.OpenPopupAsync(anchor, CaptureScene.NewButton("inner"));

        await scene.Input.ClickForeignWindowAsync();
        Assert.IsFalse(popup.IsOpen, "a press on another application did not dismiss the popup");
    });

    [TestMethod]
    public Task PressOnAnotherApplication_DismissesAfterADragInsideThePopup() => CaptureScene.RunAsync(async scene =>
    {
        var (window, anchor) = await scene.ShowAnchorAsync();
        var slider = new Slider { Minimum = 0, Maximum = 100, Value = 50, Width = 220, Height = 24 };
        var popup = await CaptureScene.OpenPopupAsync(anchor, slider);

        await DragSliderInsidePopupAsync(scene, window, slider);
        Assert.IsTrue(popup.IsOpen, "the drag inside the popup closed it");

        await scene.Input.ClickForeignWindowAsync();
        Assert.IsFalse(popup.IsOpen, "a press on another application no longer dismisses the popup");
    });

    private static async Task DragSliderInsidePopupAsync(CaptureScene scene, Window owner, Slider slider)
    {
        var (surface, thumb) = CaptureScene.SurfaceCenter(slider, owner);
        var dragged = new Point(thumb.X + 40, thumb.Y);
        await scene.Input.MoveAsync(surface, thumb);
        await scene.Input.PressAsync(surface, thumb);
        Assert.IsTrue(slider.IsMouseCaptured, "precondition: the press inside the popup captured");
        await scene.Input.MoveAsync(surface, dragged);
        await scene.Input.ReleaseAsync(surface, dragged);
    }

    private static async Task<(Window Window, ComboBox Combo)> ShowComboBoxAsync(CaptureScene scene)
    {
        var combo = new ComboBox
        {
            Width = 200,
            HorizontalAlignment = HorizontalAlignment.Left,
            ItemsSource = ItemsView.Create(new[] { "item 0", "item 1", "item 2", "item 3" }),
        };
        var panel = new StackPanel { Margin = new Thickness(20) };
        panel.Add(combo);
        var window = await scene.ShowAsync(panel);
        return (window, combo);
    }
}
